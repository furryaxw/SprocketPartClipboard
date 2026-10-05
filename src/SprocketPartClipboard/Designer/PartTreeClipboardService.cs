using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Sprocket;
using Sprocket.Blueprints;
using Sprocket.VehicleDesigner;
using Sprocket.VehicleDesigner.Access;
using Sprocket.VehicleDesigner.Operations;
using Sprocket.Vehicles;
using Sprocket.Vehicles.AttachedBehaviours;
using Sprocket.Vehicles.Operations;
using Sprocket.Vehicles.Serialization;
using Sprocket.Vehicles.VehicleMeshes;
using Sprocket.Vehicles.Weapons;
using SprocketPartClipboard.Clipboard;
using UnityEngine;

namespace SprocketPartClipboard.Designer
{
    internal sealed class ClipboardAction
    {
        private ClipboardAction(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public bool Succeeded { get; }

        public string Message { get; }

        public static ClipboardAction Ok(string message) => new ClipboardAction(true, message);

        public static ClipboardAction Fail(string message) => new ClipboardAction(false, message);
    }

    // 把"设计器里的一棵部件子树"搬进搬出剪贴板库。
    //
    // 做法是让游戏自己的序列化器出活：抓取用 VehicleObjectSerialization.ToBlueprints 把子树导成
    // 对象蓝图数组，塞进一个 objects 只含子树的退化 VehicleBlueprint；粘贴按前序逐个调用注册表的
    // 单对象 Instantiate，把树重建到当前载具上。
    internal sealed class PartTreeClipboardService
    {
        // 复制语义。游戏自己的 VehicleObject.Duplicate 就是：ToBlueprint(item, 复制语义) → SerializeToJSON
        // → DeserializeJSON → register.Instantiate(blueprint, 同一个复制语义)，所以这里两边都用同一组取值。
        //
        // 不能用 FullSave / SpawnStateChange：那两位会触发 LoadVuids，把载荷里的 VUID（含每个组件从
        // bp.State 里取回的组件 VUID）原样装到新对象上，而原树还占着这些编号，数据库注册直接抛
        // "already contains a reference ... using VUID"。
        // 抓取用的序列化上下文：SpawnStateChange(16)。
        //
        // 复制语义 DuplicateCopied(8) 下有些部件的设计数据根本不写进状态——典型是炮座：
        // `Mantlet.SaveDataInternal` 第一句就是 `if (ctx != 1 && ctx != 0x10) return;`，
        // 于是 mantletBlueprint / layingDriveVuid / trunnionsVuid / shieldVuid 全部缺失；
        // 粘贴时 Mantlet 走"按 VUID 找回依赖"的分支、键缺失 → Build 抛异常 → 高低机的耳轴
        // 与运行时行为（ILayingDriveBehaviour，由 LayingDrive.EnableBehaviour 创建）都接不上。
        // SpawnStateChange 是游戏自己留"撤销快照"时用的取值，会写出完整状态。
        private static readonly StateContext CaptureContext = new StateContext
        {
            State = SerializationFlags.SpawnStateChange,
        };

        // 跨载具重建用 FullSave：它会让 InstantiateStructured 调 LoadVuids，把载荷里（已被整体
        // 改指的）编号装到新对象上，部件内部引用才会自洽——这也是游戏装配整车时用的取值。
        private static readonly StateContext FullSaveContext = new StateContext
        {
            State = SerializationFlags.FullSave,
        };

        private readonly ClipboardLibraryStore store;
        private readonly ClipboardLibrary library;

        public PartTreeClipboardService(ClipboardLibraryStore store, ClipboardLibrary library)
        {
            this.store = store;
            this.library = library;
        }

        public ClipboardAction Copy()
        {
            try
            {
                return CopyCore();
            }
            catch (Exception exception)
            {
                // 原生调用抛的是 Il2CppException，接住它比让加载器打一堆栈更好定位。
                return ClipboardAction.Fail($"复制失败：{exception.GetType().Name}: {exception.Message}");
            }
        }

        private ClipboardAction CopyCore()
        {
            if (!DesignerPartAccess.TryOpen(out DesignerPartAccess? access) || access == null)
                return ClipboardAction.Fail("当前不在载具设计器里。");

            VehicleComponent? part = access.SelectedPart();
            if (part == null)
                return ClipboardAction.Fail("没有选中部件，先在设计器里点选一个部件。");

            VehicleObject vehicleObject = part.VehicleObject;
            if (vehicleObject == null || vehicleObject.Pointer == IntPtr.Zero)
                return ClipboardAction.Fail("选中项的部件实例不可用。");

            IVehicleGateway? gateway = Target(access);
            if (gateway == null)
                return ClipboardAction.Fail("拿不到当前载具。");

            StateContext context = CaptureContext;
            Il2CppReferenceArray<VehicleObjectBlueprint> objects =
                VehicleObjectSerialization.ToBlueprints(part.VehicleTransform, context);
            if (objects == null || objects.Length == 0)
                return ClipboardAction.Fail("导出子树失败：游戏返回了空的部件列表。");

            VehicleBlueprint? blueprint = BuildBlueprint(gateway, objects);
            if (blueprint == null)
                return ClipboardAction.Fail("组装子树蓝图失败。");

            string? payload = VehicleBlueprintCodec.Encode(blueprint);
            if (payload == null)
                return ClipboardAction.Fail("序列化子树失败。");

            string check = SelfCheck(payload, objects.Length);

            // 名字取部件实例（VehicleObject）的物体名，而不是被点击的那个组件所在子物体的名字：
            // 复制出来的始终是整个部件及其子树。
            string name = vehicleObject.gameObject != null ? vehicleObject.gameObject.name : vehicleObject.guid;
            ClipboardEntry entry = new ClipboardEntry
            {
                Id = Guid.NewGuid().ToString("n"),
                Name = name,
                CapturedUtc = DateTime.UtcNow.ToString("o"),
                GameVersion = Application.version,
                SourceVehicle = VehicleName(part),
                RootPartId = vehicleObject.guid,
                PartCount = objects.Length,
                ComponentVuids = CollectComponentVuids(gateway, objects),
                Payload = payload,
            };

            library.Add(entry);

            try
            {
                store.Save(library);
            }
            catch (Exception exception)
            {
                // 抓取本身已经成功，落盘失败只影响下次启动能否读回。
                return ClipboardAction.Ok(
                    $"已复制 {name}（{objects.Length} 个部件，载荷 {payload.Length} 字符，{check}）；写入剪贴板文件失败：{exception.Message}");
            }

            return ClipboardAction.Ok(
                $"已复制 {name}（{objects.Length} 个部件，载荷 {payload.Length} 字符，{check}）；剪贴板共 {library.Entries.Count} 项。");
        }

        // 复制侧把这棵子树里全部组件的 VUID 记进条目。粘贴时的组件集合是它与"载荷自己声明的
        // 组件键"的并集（两者是同一批编号，条目记下来是为了早于该字段、载荷里读不到的那些），
        // 因为组件编号并不总是紧挨着所属部件的编号（实测部件 285 带着组件 model=350），
        // 靠数值区间猜会漏，漏掉就与既有部件撞号。
        private static List<int> CollectComponentVuids(
            IVehicleGateway gateway,
            Il2CppReferenceArray<VehicleObjectBlueprint> objects)
        {
            List<int> vuids = new List<int>();

            VehicleObjectRegister? register = gateway.ObjectFactory.TryCast<VehicleObjectRegister>();
            Il2CppSystem.Collections.Generic.List<VehicleObject>? items =
                register?.Items.TryCast<Il2CppSystem.Collections.Generic.List<VehicleObject>>();
            if (items == null)
                return vuids;

            HashSet<int> wanted = new HashSet<int>();
            for (int index = 0; index < objects.Length; index++)
                wanted.Add(objects[index].ID);

            for (int index = 0; index < items.Count; index++)
            {
                VehicleObject item = items[index];
                if (item == null || item.Pointer == IntPtr.Zero || !wanted.Contains((int)item.VUID))
                    continue;

                Il2CppReferenceArray<VehicleComponent> behaviours = item.behaviours;
                if (behaviours == null)
                    continue;

                for (int componentIndex = 0; componentIndex < behaviours.Length; componentIndex++)
                    vuids.Add((int)behaviours[componentIndex].VUID);
            }

            return vuids;
        }

        private static string SelfCheck(string payload, int expectedParts)
        {
            VehicleBlueprint? decoded = VehicleBlueprintCodec.Decode(payload);
            if (decoded == null)
                return "载荷回读失败";

            int parts = decoded.VehicleObjects?.Length ?? 0;
            return parts == expectedParts
                ? "载荷回读一致"
                : $"载荷回读部件数不一致（{parts} != {expectedParts}）";
        }

        public ClipboardAction Paste()
        {
            try
            {
                return PasteCore();
            }
            catch (Exception exception)
            {
                return ClipboardAction.Fail($"粘贴失败：{exception.GetType().Name}: {exception.Message}");
            }
        }

        private ClipboardAction PasteCore()
        {
            ClipboardEntry? entry = library.Active;
            if (entry == null)
                return ClipboardAction.Fail("剪贴板是空的，先 Ctrl+C 复制一棵子树。");

            if (!DesignerPartAccess.TryOpen(out DesignerPartAccess? access) || access == null)
                return ClipboardAction.Fail("当前不在载具设计器里。");

            VehicleOperations? operations = access.Operations();
            if (operations == null)
                return ClipboardAction.Fail("拿不到设计器的操作控制器。");

            IVehicleGateway? gateway = Target(access);
            if (gateway == null)
                return ClipboardAction.Fail("拿不到当前载具。");

            VehicleBlueprint? blueprint = VehicleBlueprintCodec.Decode(entry.Payload);
            if (blueprint == null || blueprint.VehicleObjects == null || blueprint.VehicleObjects.Length == 0)
                return ClipboardAction.Fail($"读不回剪贴板里的“{entry.Name}”，它的数据可能来自另一个游戏版本。");

            VehicleObjectRegister? register = gateway.ObjectFactory.TryCast<VehicleObjectRegister>();
            if (register == null || register.Pointer == IntPtr.Zero)
                return ClipboardAction.Fail("拿不到当前载具的部件注册表。");

            VehicleComponent? anchor = access.SelectedPart();

            Transform parent = anchor != null && anchor.VehicleTransform != null
                ? anchor.VehicleTransform.transform
                : gateway.transform;

            // 跨载具 / 跨启动：按游戏装配整车时的同一配方。
            //   1. 载荷里的编号（部件 / 组件 / 蓝图 / 网格）一起换到目标载具的号段上；
            //   2. 把 blueprints[] / meshes[] 的定义按替换后的编号注册进目标载具；
            //   3. 用 FullSave 上下文实例化：它会让 LoadVuids 把（已替换的）编号装到新对象上，
            //      部件内部引用才会自洽；槽位也会按新编号取回我们刚注册的定义。
            int generation = ++idGeneration;
            int blueprintOffset = FirstBlueprintIdOffset + generation * IdRangeSize;
            int meshOffset = blueprintOffset + MeshIdOffsetWithinGeneration;
            int objectOffset = FirstObjectIdOffset + generation * IdRangeSize;

            IReadOnlyList<int> definitionIds = PayloadIdShift.DefinitionIds(entry.Payload);
            IReadOnlyList<int> meshIds = PayloadIdShift.MeshIds(entry.Payload);
            PayloadIdShift.ShiftResult shifted = PayloadIdShift.Shift(
                entry.Payload, definitionIds, meshIds, entry.ComponentVuids, blueprintOffset, meshOffset, objectOffset);

            VehicleBlueprint local = VehicleBlueprintCodec.Decode(shifted.Payload) ?? blueprint;

            // 注册必须用**改指后**的那一份定义：网格引用（bodyMeshVuid 等）写在蓝图定义里，
            // 用未改指的那份会让定义指向旧网格编号，而网格已注册到新编号 → 装甲没有外形。
            // 这份定义里的 id 与引用已经在 JSON 里改好，所以这里不再叠加偏移。
            int registered = RegisterDefinitions(gateway, local, shifted.InternalReferenceIds, out int meshesRegistered, out int userCounted);

            // 部件自己的状态缺键时游戏会在实例化途中抛异常（例如 `Mantlet.LoadDataInternal` 按 VUID
            // 找回依赖，而载荷里没有那些键），这时它可能已经建出了一部分部件——先清掉再报错，
            // 免得载具里留下半成品。
            int before = register.Count;
            Il2CppReferenceArray<VehicleObject> created;
            try
            {
                created = gateway.ObjectFactory.InstantiateStructured(
                    local.VehicleObjects, parent, FullSaveContext, VehicleInitiationFlags.None);
            }
            catch (Exception exception)
            {
                int removed = DiscardPartial(register, before);
                return ClipboardAction.Fail(
                    $"粘贴 {entry.Name} 失败：这条剪贴板条目的数据在当前载具里无法还原（{exception.Message}）。"
                    + $"已清理 {removed} 个半成品部件，重新复制一次再试。");
            }

            if (created == null || created.Length == 0)
                return ClipboardAction.Fail($"粘贴 {entry.Name} 失败：游戏没有创建任何部件。");

            List<VehicleObject> createdList = new List<VehicleObject>();
            for (int index = 0; index < created.Length; index++)
                createdList.Add(created[index]);

            // 实例化不做对象级基础状态与组件构建趟，这里按游戏装配载具的做法补上两步，
            // 并在随后若干帧里让车辆构建收敛（BuildPump，等价于 VehicleUtilities.FullyBuild 的语义）。
            PostPasteSetup.ApplyBaseStates(local.VehicleObjects, created, FullSaveContext);
            int rebuilt = PostPasteSetup.RequestRebuild(created);
            BuildPump.Arm(access.Editor.target);

            int trunnions = RepairTrunnions(createdList);
            int rootCount = CountRoots(local.VehicleObjects);
            int groupID = operations.GetNewGroupID();
            string how = StartPlacement(operations, created[0], groupID);

            string anchorName = anchor != null && anchor.gameObject != null ? anchor.gameObject.name : "载具根";
            return ClipboardAction.Ok(
                $"已粘贴 {entry.Name}：新建 {created.Length} 个部件（其中 {rootCount} 个作为树根挂到 {anchorName}），"
                + $"注册蓝图定义 {registered} 条（计入使用 {userCounted} 条）+ 网格 {meshesRegistered} 条，"
                + $"重建 {rebuilt} 个组件，补炮座耳轴 {trunnions} 处，手持 {how}。");
        }

        // 实例化中途失败时，注册表里会留下已建出的一部分部件：把这一批销毁掉，别留在载具里。
        private static int DiscardPartial(VehicleObjectRegister register, int createdBefore)
        {
            Il2CppSystem.Collections.Generic.List<VehicleObject>? items =
                register.Items.TryCast<Il2CppSystem.Collections.Generic.List<VehicleObject>>();
            if (items == null)
                return 0;

            int removed = 0;
            for (int index = items.Count - 1; index >= createdBefore && index >= 0; index--)
            {
                try
                {
                    VehicleObject item = items[index];
                    if (item != null && item.Pointer != IntPtr.Zero)
                    {
                        register.Destroy(item);
                        removed++;
                    }
                }
                catch (Exception)
                {
                    // 半成品本来就可能销毁失败；失败不升级成新的错误。
                }
            }

            return removed;
        }

        private static int CountRoots(Il2CppReferenceArray<VehicleObjectBlueprint> objects)
        {
            HashSet<int> ids = new HashSet<int>();
            for (int index = 0; index < objects.Length; index++)
                ids.Add(objects[index].ID);

            int roots = 0;
            for (int index = 0; index < objects.Length; index++)
            {
                if (!ids.Contains(objects[index].ParentID))
                    roots++;
            }

            return roots;
        }

        // 副本已经挂在载具上，拿起来要走 Reattach（先拆卸、再放置）；它对已挂载对象才成立。
        // 只有 Reattach 没接住时才退回 BeginPlacement。
        private static string StartPlacement(VehicleOperations operations, VehicleObject root, int groupID)
        {
            IPlaceOperation placement = operations.Reattach(root.GetReference(), groupID);
            if (placement != null && placement.Pointer != IntPtr.Zero)
                return "Reattach 已开启";

            placement = operations.BeginPlacement(root.GetReference(), groupID);
            return placement != null && placement.Pointer != IntPtr.Zero
                ? "BeginPlacement 已开启"
                : "未开启";
        }

        private const int FirstBlueprintIdOffset = 10000;

        // 部件编号单独一段：目标载具的部件数据库里已经占用了一批小号。
        private const int FirstObjectIdOffset = 100000;

        private const int IdRangeSize = 1000;

        // 同一次粘贴里，网格编号用同一号段的另一个区间：三类编号空间各自独立，不能互相踩。
        private const int MeshIdOffsetWithinGeneration = 500;

        private static int idGeneration;

        // 载荷解码后 blueprints[] / meshes[] 里的定义本身就是活的对象；这里的编号已经是本次粘贴的
        // 新编号（改指在 JSON 层完成），直接按它们注册进目标载具即可。
        // 蓝图用 IBlueprintFactory.Register(Blueprint, int)；网格按游戏自己的做法：
        // MeshTypes.TryGetType(TypeID, out type) 之后再 IVehicleMeshRegister.New(id, type, mesh)，
        // 且 Mesh 为空的条目跳过（与整车加载路径一致）。
        private static int RegisterDefinitions(
            IVehicleGateway gateway,
            VehicleBlueprint blueprint,
            IReadOnlyCollection<int> internalReferenceIds,
            out int meshesRegistered,
            out int userCounted)
        {
            int registered = RegisterBlueprints(
                gateway, blueprint, new HashSet<int>(internalReferenceIds), out userCounted);
            meshesRegistered = RegisterMeshes(gateway, blueprint);
            return registered;
        }

        private static int RegisterBlueprints(
            IVehicleGateway gateway,
            VehicleBlueprint blueprint,
            HashSet<int> internalReferenceIds,
            out int userCounted)
        {
            userCounted = 0;
            Il2CppReferenceArray<SerializableBlueprint>? definitions = blueprint.Blueprints;
            IBlueprintFactory factory = gateway.BlueprintFactory;
            if (definitions == null || factory == null || factory.Pointer == IntPtr.Zero)
                return 0;

            int registered = 0;
            for (int index = 0; index < definitions.Length; index++)
            {
                SerializableBlueprint definition = definitions[index];
                if (definition == null || definition.Pointer == IntPtr.Zero || definition.blueprint == null)
                    continue;

                RegisteredBlueprint entry = factory.Register(definition.blueprint, definition.id);
                registered++;

                // 只被蓝图内部整数键指着的定义（弹药架/炮的 shellID 指向 shellSlot）没有任何槽位链接，
                // userCount 会停在 0，写档案时被 VehicleBlueprintSerializer 静默丢弃；模拟重载后
                // 那条整数引用查不到目标，运行期模型就退化成方块。注册方在这里计入一次使用。
                if (internalReferenceIds.Contains(definition.id) && entry != null && entry.Pointer != IntPtr.Zero)
                {
                    entry.AddUser();
                    userCounted++;
                }
            }

            return registered;
        }

        private static int RegisterMeshes(IVehicleGateway gateway, VehicleBlueprint blueprint)
        {
            Il2CppReferenceArray<SerializableMesh>? meshes = blueprint.Meshes;
            IVehicleMeshRegister register = gateway.Meshes;
            if (meshes == null || register == null || register.Pointer == IntPtr.Zero)
                return 0;

            int registered = 0;
            for (int index = 0; index < meshes.Length; index++)
            {
                SerializableMesh mesh = meshes[index];
                if (mesh == null || mesh.Pointer == IntPtr.Zero || mesh.Mesh == null)
                    continue;

                if (!MeshTypes.TryGetType(mesh.TypeID, out Il2CppSystem.Type type))
                    continue;

                register.New(mesh.MeshID, type, mesh.Mesh);
                registered++;
            }

            return registered;
        }

        // 炮座（Mantlet）把高低机与耳轴存成组件编号，解析出来后由 Mantlet.Build() 调
        // LayingDrive.set_Trunnions(...)。副本如果没走到那一步，Trunnions 就是 null：
        // 编辑器画 gizmo 时 LayingDrive.DrawGizmos 会直接抛 NullReferenceException，
        // 该插件随即被禁用（表现为高低机不正常 + gizmo 消失）。这里按已解析出的两个字段补上。
        private static int RepairTrunnions(List<VehicleObject> objects)
        {
            int repaired = 0;
            for (int index = 0; index < objects.Count; index++)
            {
                Il2CppReferenceArray<VehicleComponent> behaviours = objects[index].behaviours;
                if (behaviours == null)
                    continue;

                for (int componentIndex = 0; componentIndex < behaviours.Length; componentIndex++)
                {
                    Mantlet? mantlet = behaviours[componentIndex].TryCast<Mantlet>();
                    if (mantlet == null)
                        continue;

                    LayingDrive? drive = mantlet.layingDrive;
                    if (drive == null || drive.Trunnions != null)
                        continue;

                    if (mantlet.trunnionRotationArea == null)
                        continue;

                    drive.Trunnions = mantlet.trunnionRotationArea;
                    repaired++;
                }
            }

            return repaired;
        }

        // 逐个部件按前序重建：父部件先建，子部件建之前把 ParentID 改写成父部件刚拿到的新 VUID。
        // 单对象 Instantiate 会给没有编号的对象与组件分配新号并登记进数据库，所以副本不会和原树撞号；
        private static VehicleBlueprint? BuildBlueprint(
            IVehicleGateway gateway,
            Il2CppReferenceArray<VehicleObjectBlueprint> objects)
        {
            VehicleBlueprint? blueprint = new VehicleBlueprintSerializer()
                .ToBlueprint(gateway)
                .TryCast<VehicleBlueprint>();

            if (blueprint == null)
                return null;

            // 只替换实例数组：部件定义与网格沿用整车模板，保证引用的资产在载入时都能解析。
            blueprint.VehicleObjects = objects;
            return blueprint;
        }

        private static IVehicleGateway? Target(DesignerPartAccess access)
        {
            IVehicleEditor editor = access.Editor.Cast<IVehicleEditor>();
            IVehicleGateway target = editor.Target;
            if (target == null || target.Pointer == IntPtr.Zero)
                return null;

            return target;
        }

        private static string VehicleName(VehicleComponent part)
        {
            VehicleObject root = part.VehicleRoot;
            if (root != null && root.Pointer != IntPtr.Zero && root.gameObject != null)
                return root.gameObject.name;

            return "";
        }
    }
}
