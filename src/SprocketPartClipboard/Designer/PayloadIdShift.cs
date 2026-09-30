using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SprocketPartClipboard.Designer
{
    // 载荷里的编号分三类，粘贴时必须一起换到目标载具的号段，否则引用会指空或指错：
    //
    //   部件编号    objects[].vuid / pvuid、*ID 形式的部件引用
    //   组件编号    objects[] 顶层以 ComponentID 为键的整数、以及 *Vuid 形式的组件引用
    //   蓝图编号    blueprints[].id，以及指向它的 *Vuid 字段
    //   网格编号    meshes[].vuid，以及指向它的 *Vuid 字段
    //
    // **只按键名与复制时记下的精确编号集合判定，绝不按数值大小猜**：
    //   - 早期按值猜的版本把蓝图里的坐标（y=375）也改了，BeltBlueprint 反序列化直接失败；
    //   - 早期按"部件编号附近的区间"认组件编号的版本会漏掉离得远的组件
    //     （实测 gunnerSight 的组件编号离所属部件很远），漏掉就让新部件与既有部件撞号。
    // 组件编号的键只出现在 objects[] 条目的顶层：
    //
    //   {"guid":"…","vuid":260,"pvuid":0,"flags":2,"turretRing":261,"basket":262,"structureID":268,…}
    public static class PayloadIdShift
    {
        // 只有条目没记录组件编号时才用到的兜底宽度：组件编号通常紧跟所属部件编号
        // （实测炮塔环 260→261..264），但**并非总是**如此，所以精确集合才是主路径。
        private const int ComponentBandWidth = 16;
        private static readonly string[] ObjectStructuralKeys = { "guid", "vuid", "pvuid", "flags", "transform" };

        public sealed class ShiftResult
        {
            public ShiftResult(string payload, int blueprintCount, int meshCount, IReadOnlyCollection<int> internalReferenceIds)
            {
                Payload = payload;
                BlueprintCount = blueprintCount;
                MeshCount = meshCount;
                InternalReferenceIds = internalReferenceIds;
            }

            public string Payload { get; }

            public int BlueprintCount { get; }

            public int MeshCount { get; }

            // 只被蓝图内部整数键（shellID）指向的定义编号（改指后的值）。
            // 这类定义没有任何部件的 BlueprintSlot 引用，注册后 userCount 为 0，
            // 写档案时会被 VehicleBlueprintSerializer 静默丢弃——必须由注册方自己计入使用。
            public IReadOnlyCollection<int> InternalReferenceIds { get; }
        }

        public static IReadOnlyList<int> DefinitionIds(string payload)
        {
            return IdsOf(payload, "blueprints");
        }

        public static IReadOnlyList<int> MeshIds(string payload)
        {
            return IdsOf(payload, "meshes", "vuid");
        }

        private static IReadOnlyList<int> IdsOf(string payload, string arrayName, string idName = "id")
        {
            List<int> ids = new List<int>();
            JsonNode? root = Parse(payload);
            if (root?[arrayName] is not JsonArray items)
                return ids;

            foreach (JsonNode? item in items)
            {
                if (item?[idName] is JsonValue value && value.TryGetValue(out int id))
                    ids.Add(id);
            }

            return ids;
        }

        public static ShiftResult Shift(
            string payload,
            IReadOnlyList<int> blueprintIds,
            IReadOnlyList<int> meshIds,
            IReadOnlyCollection<int> componentVuids,
            int blueprintOffset,
            int meshOffset,
            int objectOffset)
        {
            JsonNode? root = Parse(payload);
            if (root == null)
                return new ShiftResult(payload, 0, 0, Array.Empty<int>());

            HashSet<int> blueprints = new HashSet<int>(blueprintIds);
            HashSet<int> meshes = new HashSet<int>(meshIds);
            HashSet<int> components = new HashSet<int>(componentVuids);

            HashSet<int> objects = new HashSet<int>();
            if (root["objects"] is JsonArray objectItems)
            {
                foreach (JsonNode? item in objectItems)
                {
                    if (item?["vuid"] is JsonValue value && value.TryGetValue(out int id))
                        objects.Add(id);
                }
            }

            HashSet<int> internalReferences = new HashSet<int>();

            // 条目里没有记录组件编号（早于该字段的条目）时退回"部件编号之后的窄带"：
            // 这个判定只作用于 objects[] 条目的顶层，不会碰到蓝图数据里的数值；
            // 它可能漏掉离得远的组件，但比完全不偏移好。
            if (components.Count == 0)
            {
                foreach (int objectId in objects)
                {
                    for (int step = 1; step <= ComponentBandWidth; step++)
                        components.Add(objectId + step);
                }
            }

            // 定义数组里的 id 按各自号段处理；递归到这些条目时要跳过该键，
            // 否则 meshes[].vuid 会被"vuid 规则"再按部件号段偏一次。
            ShiftDefinitionIds(root, "blueprints", "id", blueprintOffset);
            ShiftDefinitionIds(root, "meshes", "vuid", meshOffset);

            if (root["objects"] is JsonArray objectsArray)
            {
                foreach (JsonNode? item in objectsArray)
                {
                    if (item is JsonObject entry)
                        ShiftObjectEntry(entry, objects, components, blueprints, meshes,
                            blueprintOffset, meshOffset, objectOffset);
                }
            }

            if (root["blueprints"] is JsonArray definitions)
            {
                foreach (JsonNode? item in definitions)
                {
                    if (item is JsonObject entry)
                        ShiftNested(entry, blueprints, meshes, blueprintOffset, meshOffset, objectOffset, "id");
                }
            }

            if (root["meshes"] is JsonArray meshItems)
            {
                foreach (JsonNode? item in meshItems)
                {
                    if (item is JsonObject entry)
                        ShiftNested(entry, blueprints, meshes, blueprintOffset, meshOffset, objectOffset, "vuid");
                }
            }

            // 改指之后再收集：这时 shellID 的值已经是新编号。
            CollectInternalReferences(root, internalReferences);

            return new ShiftResult(root.ToJsonString(), blueprints.Count, meshes.Count, internalReferences);
        }

        private static void ShiftDefinitionIds(JsonNode root, string arrayName, string idName, int offset)
        {
            if (root[arrayName] is not JsonArray items)
                return;

            foreach (JsonNode? item in items)
            {
                if (item is JsonObject map && map[idName] is JsonValue value && value.TryGetValue(out int id))
                    map[idName] = id + offset;
            }
        }

        // objects[] 条目：顶层按键名判定部件编号、*ID 部件引用、组件编号；再往下只认 *Vuid。
        private static void ShiftObjectEntry(
            JsonObject entry,
            HashSet<int> objects,
            HashSet<int> components,
            HashSet<int> blueprints,
            HashSet<int> meshes,
            int blueprintOffset,
            int meshOffset,
            int objectOffset)
        {
            List<KeyValuePair<string, int>>? changes = null;

            foreach (KeyValuePair<string, JsonNode?> pair in entry)
            {
                if (pair.Value == null)
                    continue;

                if (pair.Value is JsonValue value && value.TryGetValue(out int id) && id >= 0)
                {
                    int? shifted = null;

                    if (IsKey(pair.Key, "vuid") || IsKey(pair.Key, "pvuid"))
                    {
                        // 部件自身的编号是正数；pvuid 的 0 表示"没有父节点"，不能偏移。
                        if (id > 0)
                            shifted = id + objectOffset;
                    }
                    else if (EndsWith(pair.Key, "vuid"))
                    {
                        // 蓝图与网格编号从 0 开始，0 是合法编号（只有 -1 表示"没有"）。
                        shifted = ShiftVuid(id, pair.Key, blueprints, meshes, blueprintOffset, meshOffset, objectOffset);
                    }
                    else if (EndsWith(pair.Key, "id") && id > 0 && objects.Contains(id))
                    {
                        // 例如 structureID：指向另一个部件。
                        shifted = id + objectOffset;
                    }
                    else if (id > 0 && !IsStructuralKey(pair.Key) && components.Contains(id))
                    {
                        // 组件编号：以 ComponentID 为键，按复制时记下的精确集合判定。
                        shifted = id + objectOffset;
                    }

                    if (shifted.HasValue)
                    {
                        (changes ??= new List<KeyValuePair<string, int>>()).Add(
                            new KeyValuePair<string, int>(pair.Key, shifted.Value));
                        continue;
                    }
                }

                ShiftNested(pair.Value, blueprints, meshes, blueprintOffset, meshOffset, objectOffset, null);
            }

            Apply(entry, changes);
        }

        // 蓝图 / 网格定义以及嵌套结构：只认 *Vuid 键，别的一律不动。
        private static void ShiftNested(
            JsonNode node,
            HashSet<int> blueprints,
            HashSet<int> meshes,
            int blueprintOffset,
            int meshOffset,
            int objectOffset,
            string? skipKey)
        {
            if (node is JsonObject map)
            {
                List<KeyValuePair<string, int>>? changes = null;

                foreach (KeyValuePair<string, JsonNode?> pair in map)
                {
                    if (pair.Value == null)
                        continue;

                    if (!IsKey(pair.Key, skipKey)
                        && pair.Value is JsonValue value
                        && value.TryGetValue(out int id))
                    {
                        if (EndsWith(pair.Key, "vuid") && id >= 0)
                        {
                            (changes ??= new List<KeyValuePair<string, int>>()).Add(
                                new KeyValuePair<string, int>(
                                    pair.Key, ShiftVuid(id, pair.Key, blueprints, meshes, blueprintOffset, meshOffset, objectOffset)));
                            continue;
                        }

                        if (IsBlueprintReferenceKey(pair.Key) && blueprints.Contains(id))
                        {
                            (changes ??= new List<KeyValuePair<string, int>>()).Add(
                                new KeyValuePair<string, int>(pair.Key, id + blueprintOffset));
                            continue;
                        }
                    }

                    ShiftNested(pair.Value, blueprints, meshes, blueprintOffset, meshOffset, objectOffset, null);
                }

                Apply(map, changes);
                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    if (item != null)
                        ShiftNested(item, blueprints, meshes, blueprintOffset, meshOffset, objectOffset, null);
                }
            }
        }

        private static int ShiftVuid(
            int id,
            string key,
            HashSet<int> blueprints,
            HashSet<int> meshes,
            int blueprintOffset,
            int meshOffset,
            int objectOffset)
        {
            // 键名优先：网格与蓝图的编号段会重叠（都从 0、1 开始），只靠数值集合会认错空间。
            if (key.Contains("mesh", StringComparison.OrdinalIgnoreCase))
                return id + meshOffset;

            if (key.Contains("blueprint", StringComparison.OrdinalIgnoreCase))
                return id + blueprintOffset;

            if (blueprints.Contains(id))
                return id + blueprintOffset;

            if (meshes.Contains(id))
                return id + meshOffset;

            // 既不是蓝图也不是网格的 vuid 引用：组件编号，跟随部件号段。
            return id + objectOffset;
        }

        // 只被蓝图内部整数键（shellID）指向的定义编号（改指后的值）。
        private static void CollectInternalReferences(JsonNode node, HashSet<int> ids)
        {
            if (node is JsonObject map)
            {
                foreach (KeyValuePair<string, JsonNode?> pair in map)
                {
                    if (pair.Value == null)
                        continue;

                    if (pair.Value is JsonValue value && value.TryGetValue(out int id) && IsBlueprintReferenceKey(pair.Key))
                    {
                        ids.Add(id);
                        continue;
                    }

                    CollectInternalReferences(pair.Value, ids);
                }

                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? item in array)
                {
                    if (item != null)
                        CollectInternalReferences(item, ids);
                }
            }
        }

        // 蓝图内部用整数直接指向另一条蓝图（不经过 objects[] 的 *Vuid 键）。实测：
        // `AmmoRackBlueprint.ShellSlotBlueprintID` 与 `CannonBlueprint.ShellSlotBlueprintID` 的序列化键
        // 都是 `shellID`，指向 `blueprints[]` 里那条 `shellSlot`；而 shellSlot 蓝图在 objects[] 里
        // 没有任何引用，全靠这个整数。不改号的话 AmmoRack.Build 里那次 Load 查不到蓝图、
        // **静默**返回 false（不抛不日志），弹种与装填随之失效。
        private static bool IsBlueprintReferenceKey(string key)
        {
            return key.Equals("shellID", StringComparison.OrdinalIgnoreCase);
        }

        private static void Apply(JsonObject map, List<KeyValuePair<string, int>>? changes)
        {
            if (changes == null)
                return;

            foreach (KeyValuePair<string, int> change in changes)
                map[change.Key] = change.Value;
        }

        private static bool IsStructuralKey(string key)
        {
            foreach (string candidate in ObjectStructuralKeys)
            {
                if (IsKey(key, candidate))
                    return true;
            }

            return false;
        }

        private static bool IsKey(string key, string? expected)
        {
            return expected != null && key.Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EndsWith(string key, string suffix)
        {
            return key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        private static JsonNode? Parse(string payload)
        {
            try
            {
                return JsonNode.Parse(payload);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
