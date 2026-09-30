using System;
using Il2CppSprocket.Vehicles;
using MelonLoader;

namespace SprocketPartClipboard.Designer
{
    // 跨帧构建泵。
    //
    // `Vehicle.Build` 一次不保证收敛：它把脏位从 +0x13C 挪到 +0x13E，处理期间新抬起的位留到下一轮。
    // 游戏自己装载载具时走的是 `VehicleUtilities.FullyBuild`——一个最多 17 轮的跨帧循环。
    // 粘贴是一次同步动作，所以这里在随后的若干帧里反复调 `Build()` 直到它返回 None，
    // 让每个组件的 `Build` 都跑到（组件 Build 里会建立 BlueprintSlot 链接，进而决定某些定义
    // 在写档案时是否被算作"被使用"）。
    internal static class BuildPump
    {
        private const int MaxRounds = 20;

        private static IVehicleEditGateway? gateway;
        private static int remaining;
        private static int rounds;
        private static VehicleDirtyFlags last;

        public static void Arm(IVehicleEditGateway? target)
        {
            gateway = target != null && target.Pointer != IntPtr.Zero ? target : null;
            remaining = gateway == null ? 0 : MaxRounds;
            rounds = 0;
            last = VehicleDirtyFlags.None;

            if (remaining == 0)
                return;

            MelonLogger.Msg("[PartClipboard] 构建泵已开启（跨帧收敛）");
        }

        public static void Tick()
        {
            if (remaining <= 0 || gateway == null)
                return;

            remaining--;
            rounds++;

            try
            {
                last = gateway.Build();
            }
            catch (Exception exception)
            {
                MelonLogger.Warning($"[PartClipboard] 构建泵失败：{exception.Message}");
                remaining = 0;
                return;
            }

            if (last == VehicleDirtyFlags.None || remaining == 0)
            {
                MelonLogger.Msg($"[PartClipboard] 构建泵结束：{rounds} 轮，末次脏标志 {last}");
                remaining = 0;
            }
        }
    }
}
