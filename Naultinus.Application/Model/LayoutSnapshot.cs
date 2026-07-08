using System;
using System.Collections.Generic;

namespace Naultinus.Model
{
    public class LayoutSnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int ScreenWidth { get; set; }
        public int ScreenHeight { get; set; }
        public int ScreenCount { get; set; }
        public int SchemaVersion { get; set; } = 1;
        public List<SnapshotEntry> Entries { get; set; } = new List<SnapshotEntry>();
    }

    public class SnapshotEntry
    {
        public string NaultinusIdentifier { get; set; } = "";
        public string? GroupId { get; set; }
        public int TabOrder { get; set; }
        public string StateXmlContent { get; set; } = "";
    }
}
