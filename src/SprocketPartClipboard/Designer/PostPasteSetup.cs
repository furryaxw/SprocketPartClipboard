using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppSprocket;
using Il2CppSprocket.Vehicles;
using Il2CppSprocket.Vehicles.Serialization;

namespace SprocketPartClipboard.Designer
{
    // 实例化之后把"游戏装配载具时会做、而 InstantiateStructured 不做"的两步补上。
    internal static class PostPasteSetup
    {
        // 对象级基础状态（标志位等）。InstantiateStructured 只应用组件状态与编号，
        // 这一步走游戏装载载具时用的同一个函数。
        public static int ApplyBaseStates(
            Il2CppReferenceArray<VehicleObjectBlueprint> objects,
            Il2CppReferenceArray<VehicleObject> created,
            StateContext context)
        {
            if (objects == null || created == null || created.Length == 0)
                return 0;

            var byVuid = new System.Collections.Generic.Dictionary<int, VehicleObject>();
            for (int index = 0; index < created.Length; index++)
            {
                VehicleObject item = created[index];
                if (item != null && item.Pointer != IntPtr.Zero)
                    byVuid[(int)item.VUID] = item;
            }

            int applied = 0;
            for (int index = 0; index < objects.Length; index++)
            {
                VehicleObjectBlueprint blueprint = objects[index];
                if (blueprint == null || blueprint.State == null)
                    continue;

                if (!byVuid.TryGetValue(blueprint.ID, out VehicleObject? target))
                    continue;

                VehicleObjectSerialization.ApplyBaseObjectState(target, blueprint.State, context, blueprint.Flags);
                applied++;
            }

            return applied;
        }

        // 组件构建趟。组件的 `Build` 只由
        // `Vehicle.Build` → `VehicleObjectRegister.Build` → `VehicleComponent.BuildInternal` 调用，
        // 而 `Vehicle.Build` 的闸门是 `dirtyFlags & (PartAdded|PartConfiguration)`、
        // `register.Build` 只挑 `RequiresRebuild` 置位的组件。`InstantiateStructured` 只置了
        // 伤害模型脏位，所以这里先给每个组件立脏位，再让车辆构建一次（后续帧由 BuildPump 收敛）。
        //
        // 组件 `Build` 里会按蓝图算出尺寸一类的派生数据，也会建立 BlueprintSlot 链接——
        // 后者决定"只被蓝图内部整数引用"的定义在写档案时是否算作被使用。
        public static int RequestRebuild(Il2CppReferenceArray<VehicleObject> created)
        {
            if (created == null || created.Length == 0)
                return 0;

            int flagged = 0;
            for (int index = 0; index < created.Length; index++)
            {
                VehicleObject item = created[index];
                if (item == null || item.Pointer == IntPtr.Zero)
                    continue;

                Il2CppReferenceArray<VehicleComponent> behaviours = item.behaviours;
                if (behaviours == null)
                    continue;

                for (int componentIndex = 0; componentIndex < behaviours.Length; componentIndex++)
                {
                    VehicleComponent component = behaviours[componentIndex];
                    if (component == null || component.Pointer == IntPtr.Zero)
                        continue;

                    component.RequestRebuild();
                    flagged++;
                }
            }

            return flagged;
        }
    }
}
