using System;
using System.Text.Json.Serialization;

namespace TaskFolder.Models
{
    /// <summary>
    /// Compatibility projection: legacy metadata uses a basename; v2 mutations require Id.
    /// V2 persistence stores this data in ShortcutEntry, without rewriting legacy JSON.
    /// </summary>
    public class ShortcutMetadata
    {
        [JsonIgnore] public Guid Id { get; set; }
        [JsonIgnore] public Guid ProfileId { get; set; }
        [JsonIgnore] public bool IsPinned { get; set; }
        [JsonIgnore] public int PinnedOrder { get; set; } = int.MaxValue;
        /// <summary>Filename key, e.g. "Notepad.lnk" or "GitHub.url".</summary>
        public string FileName { get; set; } = string.Empty;

        public long LaunchCount { get; set; } = 0;
        public DateTime? LastUsed { get; set; } = null;

        /// <summary>Custom sort order. int.MaxValue means unset (alphabetical fallback).</summary>
        public int SortOrder { get; set; } = int.MaxValue;

        /// <summary>Overrides the auto-detected icon. Null means use default detection.</summary>
        public string CustomIconPath { get; set; } = null;
        public int CustomIconIndex { get; set; } = 0;
    }
}
