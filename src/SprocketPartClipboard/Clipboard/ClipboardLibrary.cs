using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SprocketPartClipboard.Clipboard
{
    // 剪贴板库是跨载具、跨启动的：每个条目是一棵被复制过的子树，条目按复制时间从新到旧排列。
    public sealed class ClipboardLibrary
    {
        public const int CurrentSchemaVersion = 1;

        // 上限存在的意义是让文件保持可读写规模；超出时丢弃最旧的条目。
        public const int MaxEntries = 64;

        public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string ActiveId { get; set; } = "";
        public List<ClipboardEntry> Entries { get; set; } = new List<ClipboardEntry>();

        // 未知键保留：更新过的模组或手改过的文件都不应在本模组写入后被静默截断。
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Unknown { get; set; } = new Dictionary<string, JsonElement>();

        [JsonIgnore]
        public ClipboardEntry? Active
        {
            get
            {
                ClipboardEntry? match = Find(ActiveId);
                if (match != null)
                    return match;
                return Entries.Count > 0 ? Entries[0] : null;
            }
        }

        public ClipboardEntry? Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            foreach (ClipboardEntry entry in Entries)
            {
                if (string.Equals(entry.Id, id, StringComparison.Ordinal))
                    return entry;
            }

            return null;
        }

        public void Add(ClipboardEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            Entries.Insert(0, entry);
            ActiveId = entry.Id;
            Normalize();
        }

        public bool Select(string id)
        {
            if (Find(id) == null)
                return false;

            ActiveId = id;
            return true;
        }

        public bool Remove(string id)
        {
            ClipboardEntry? entry = Find(id);
            if (entry == null)
                return false;

            Entries.Remove(entry);
            if (string.Equals(ActiveId, id, StringComparison.Ordinal))
                ActiveId = Entries.Count > 0 ? Entries[0].Id : "";
            return true;
        }

        public void Normalize()
        {
            if (Entries == null)
                Entries = new List<ClipboardEntry>();
            if (Unknown == null)
                Unknown = new Dictionary<string, JsonElement>();

            Entries.RemoveAll(entry => entry == null);

            if (Entries.Count > MaxEntries)
                Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);

            // 内存中的库始终是当前布局，落盘时写出的也是当前 SchemaVersion。
            SchemaVersion = CurrentSchemaVersion;

            if (Find(ActiveId) == null)
                ActiveId = Entries.Count > 0 ? Entries[0].Id : "";
        }
    }
}
