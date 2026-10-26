using System;
using System.Collections.Generic;

namespace TaskFolder.Models
{
    internal sealed class LauncherMenuSnapshot
    {
        internal const int DirectPinLimit = 8;
        public long Revision { get; set; }
        public Guid ProfileId { get; set; }
        public string ProfileName { get; set; }
        public bool IsV2 { get; set; }
        public bool ShowRecent { get; set; }
        public bool ShowMostUsed { get; set; }
        public IReadOnlyList<ShortcutItem> AllShortcuts { get; set; } = Array.Empty<ShortcutItem>();
        public IReadOnlyList<ShortcutItem> Pins { get; set; } = Array.Empty<ShortcutItem>();
        public IReadOnlyList<ShortcutItem> DirectPins { get; set; } = Array.Empty<ShortcutItem>();
        public IReadOnlyList<ShortcutItem> MostUsed { get; set; } = Array.Empty<ShortcutItem>();
        public IReadOnlyList<ShortcutItem> Recent { get; set; } = Array.Empty<ShortcutItem>();
        public IReadOnlyList<string> FolderPaths { get; set; } = Array.Empty<string>();
    }
}
