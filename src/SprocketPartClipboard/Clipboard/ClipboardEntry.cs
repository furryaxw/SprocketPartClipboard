using System;
using System.Text.Json.Serialization;

using System.Collections.Generic;

namespace SprocketPartClipboard.Clipboard
{
    // 一次复制的结果。Payload 对库是不可解释的黑盒：它由设计器侧抓取时生成，粘贴时原样交回设计器侧。
    public sealed class ClipboardEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string CapturedUtc { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string SourceVehicle { get; set; } = "";
        public string RootPartId { get; set; } = "";
        public int PartCount { get; set; }

        // 复制时记下的这棵子树里全部组件的 VUID。
        //
        // 粘贴要按这个精确集合偏移组件编号：组件编号在载荷里以组件标识为键存放（`turretRing`、
        // `gunnerSight`…），而它并不总是紧挨着所属部件的编号，靠"部件编号附近的数值区间"猜会漏掉
        // 远离的那种，漏掉的编号会让新部件与目标载具里既有部件撞号
        // （`VehicleDatabase.Register` 抛 "already contains a reference ... using VUID"）。
        public List<int> ComponentVuids { get; set; } = new List<int>();

        public string Payload { get; set; } = "";

        [JsonIgnore]
        public bool HasPayload => !string.IsNullOrEmpty(Payload);
    }
}
