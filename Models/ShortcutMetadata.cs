using System;

namespace TaskFolder.Models
{
    /// <summary>
    /// Per-shortcut persisted data, keyed by filename (e.g. "Notepad.lnk").
    /// Stored as a dictionary in metadata.json.
    /// </summary>
    public class ShortcutMetadata
    {
        /// <summary>Filename key, e.g. "Notepad.lnk" or "GitHub.url".</summary>
        public string FileName { get; set; } = string.Empty;

        public int LaunchCount { get; set; } = 0;
        public DateTime? LastUsed { get; set; } = null;

        /// <summary>Custom sort order. int.MaxValue means unset (alphabetical fallback).</summary>
        public int SortOrder { get; set; } = int.MaxValue;

        /// <summary>Overrides the auto-detected icon. Null means use default detection.</summary>
        public string CustomIconPath { get; set; } = null;
        public int CustomIconIndex { get; set; } = 0;
    }
}
