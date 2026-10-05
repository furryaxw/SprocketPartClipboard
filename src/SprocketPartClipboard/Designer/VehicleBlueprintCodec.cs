using System;
using Sprocket.Vehicles;
using Sprocket.Vehicles.Serialization;

namespace SprocketPartClipboard.Designer
{
    // 载荷就是游戏自己的载具蓝图 JSON：一棵被复制下来的子树写成"objects 只含子树"的退化蓝图。
    // 读写都走游戏自带的 VehicleBlueprintSerializer，键名、数值格式与版本迁移都由游戏负责。
    internal static class VehicleBlueprintCodec
    {
        private static readonly VehicleBlueprintSerializer Serializer = new VehicleBlueprintSerializer();

        public static string? Encode(VehicleBlueprint? blueprint)
        {
            if (blueprint == null || blueprint.Pointer == IntPtr.Zero)
                return null;

            string json = Serializer.SerializeToJSON(blueprint.Cast<IVehicleBlueprint>(), true);
            return string.IsNullOrEmpty(json) ? null : json;
        }

        public static VehicleBlueprint? Decode(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            IVehicleBlueprint blueprint = Serializer.DeserializeJSON(json);
            if (blueprint == null || blueprint.Pointer == IntPtr.Zero)
                return null;

            return blueprint.TryCast<VehicleBlueprint>();
        }
    }
}
