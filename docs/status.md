# 实现状态

## 功能

- `Ctrl+C`：把当前选中部件所在的**整棵子树**（含它自己）序列化成载荷存进剪贴板库。
- `Ctrl+V`：在当前载具里把该子树还原出来并进入手持摆放（`Reattach`）。
- 剪贴板跨载具、跨启动保留（`UserData\SprocketPartClipboard\library.json`，原子写 + 容量上限 64）。
- 键位由 SprocketModAPI 管理（动作 ID `copy-part-tree` / `paste-part-tree`）。

## 粘贴路径（同车与跨载具共用同一条）

1. **抓取**用 `StateContext{State = SpawnStateChange(16)}`。
   复制语义 `DuplicateCopied(8)` 下有些部件的状态根本不写——例如
   `Mantlet.SaveDataInternal` 第一句是 `if (ctx != 1 && ctx != 0x10) return;`，
   于是炮座的设计数据（`mantletBlueprint.drive_vuid` / `trunnions_vuid` / `shield_vuid`）缺失。
2. **编号改指**（`PayloadIdShift`）：四类编号一起换到目标载具的号段，规则按键名与精确编号集合判定，
   不按数值大小猜：

   | 空间 | 载荷位置 | 判定 |
   | --- | --- | --- |
   | 部件 | `objects[].vuid` / `pvuid`、`*ID` 形式的部件引用 | 只偏移正数（`0` 是"无父节点"哨兵） |
   | 组件 | `objects[]` 顶层以 `ComponentID` 为键的整数 | 落在组件集合里才偏移；组件集合 = 载荷自己声明的组件键 ∪ 条目记录的 `ComponentVuids` |
   | 定义引用 | 以 `Vuid` 结尾的键 | 键名含 `mesh` → 网格段；含 `blueprint` → 蓝图段；**其余先按部件/组件判**，再蓝图、再网格 |
   | 定义引用（整数键） | `shellID`（弹药架/炮的蓝图指向弹种槽蓝图） | 蓝图段 |
   | 引用列表 | `barrelVuids`、`operatedBehaviours`（整数数组） | 元素与标量同号段；号段**只由键名定**，不按值判 |

   **组件集合的来源**：`objects[]` 条目顶层以组件标识为键的整数就是该组件的编号（
   `{"guid":…,"vuid":260,"pvuid":0,"flags":2,"turretRing":261,…}`），这是权威来源；
   条目的 `ComponentVuids` 是复制那一刻记下的同一批编号，取并集是为了早于该字段的条目。
   不用"部件编号之后的窄带"：实测 `vuid=285` 的部件带着 `model=350`、`vuid=363` 带着 `model=401`，
   离得远的组件会被漏掉，漏掉就与既有部件撞号。

   **判定顺序**：没有号段提示的 `*Vuid` 引用只能指部件或组件，而定义编号从 `0` 起、与部件/组件编号
   必然重叠，所以必须先判部件/组件。实测炮塔环一条载荷里 `mantletBlueprint.trunnions_vuid = 300`
   的 `300` 同时是 `blueprints[]` 里某条定义的 id：先问蓝图集合会把它改到定义号段上，
   `Mantlet.LoadDataInternal`（RVA `0x1A5A820`）用它按号取 `RotationRangeArea` 组件，
   取不到时该函数落到抛 `NullReferenceException` 的尾部（`trunnionRotationArea` / `trunnionModel`
   任一为空就走到 `0x180415D90`），整次粘贴在实例化中途中断。
   网格与蓝图编号从 `0` 开始，`0` 是**合法编号**（只有 `-1` 表示"没有"）。

   **引用列表**：引用也会以整数数组的形式出现，键名不带 `Vuid` 后缀——`barrelVuids`
   （`CannonInstanceBlueprint.BarrelSegmentVUIDs`，炮管段组件号）与 `operatedBehaviours`
   （`CrewSeatBlueprint.OperatedBehaviourIDs`，`VUID[]`）。漏改的后果是复制件"认领"原车的组件：
   `CannonBarrelSegment.SetSegmentCount`（RVA `0x19C6D40`）拿数组元素在载具里按号解析出**既有**的
   炮管段，再把它挂到本炮名下（`segment.parentCannon`），而 `barrelVuids` 是定义数据本身，
   写档案后重读照样复现——表现为原车的炮管外观错乱、且保存重读不恢复。
   这两个键的号段**只由键名定**（都是组件号，与部件共用号段），不查编号集合：
   元素值会与蓝图/网格编号撞车（实测 `operatedBehaviours` 里有一个指向子树之外、
   恰好等于某条定义编号的组件号）。只认这些键名——网格拓扑数组（`edges` / `faces.v` /
   `vertices`）与 `paintJobIDs`（颜料序号）里的数值与各类编号区间大量重叠，按值改会写坏网格。
3. **定义注册**：用**改指后**的那一份定义注册——网格引用（`bodyMeshVuid` 等）写在蓝图定义里，
   用未改指的那份会让定义指向旧网格编号，而网格已注册到新编号。
   - 蓝图：`IBlueprintFactory.Register(Blueprint, int)`
   - 网格：`MeshTypes.TryGetType(TypeID, out type)` → `IVehicleMeshRegister.New(id, type, Mesh)`
   - **只被蓝图内部整数引用的定义**（`shellID` 的目标，如 `shellSlot`）额外调一次
     `RegisteredBlueprint.AddUser()`：`VehicleBlueprintSerializer.ToBlueprint` 只写
     `userCount > 0` 的定义，而 `userCount` 只由 `BlueprintSlot.set_RegisterLink` 维护，
     `Register` 不动它——不加这一次使用，这类定义会在写档案时被静默丢弃。
4. **实例化**：`InstantiateStructured(objects, parent, StateContext{FullSave(1)}, None)`。
   它只做 `ApplyBaseObjectState` → `InitiateBehaviour` → `ApplyComponentStates`（不跑组件构建趟）。
5. **补齐与收敛**：
   - `PostPasteSetup.ApplyBaseStates`：对象级基础状态；
   - `PostPasteSetup.RequestRebuild` + `BuildPump`：组件 `Build` 只由
     `Vehicle.Build` → `VehicleObjectRegister.Build` → `VehicleComponent.BuildInternal` 调用，
     且 `Vehicle.Build` 的闸门是 `dirtyFlags & (PartAdded|PartConfiguration)`、
     `register.Build` 只挑 `RequiresRebuild` 置位的组件；`Vehicle.Build` 一次不收敛
     （脏位从 `+0x13C` 挪到 `+0x13E`），所以先立脏位、再跨帧构建到返回 `None`。
6. **补炮座耳轴**（`RepairTrunnions`）：`LayingDrive.Trunnions` 全游戏只有一个写点
   （`Mantlet.Build()` → `LayingDrive.set_Trunnions`）；为 null 时编辑器画 gizmo 会抛
   `NullReferenceException`，插件随即被单独禁用。
7. **摆放**：`Reattach`（先拆卸再放置）；只有它没接住时才退回 `BeginPlacement`。

该路径不进撤销栈（原生 `Instantiate` 不产生 operation）。

## 命令

```
dotnet build .\SprocketPartClipboard.slnx --configuration Release
dotnet run --configuration Release --project .\tests\SprocketPartClipboard.ContractTests\SprocketPartClipboard.ContractTests.csproj
dotnet build .\src\SprocketPartClipboard\SprocketPartClipboard.csproj -c Release -p:DeployMod=true
powershell -File .\tools\verify-package.ps1
```
