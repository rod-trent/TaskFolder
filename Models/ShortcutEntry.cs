using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskFolder.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ShortcutKind { ShellLink, Url, Executable }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum IconSource { ManagedAsset, ExternalReference }
    public enum ShortcutStatus { Ready, MissingTarget, Unavailable, Invalid, NeedsReview }

    public sealed class CustomIconReference
    {
        public IconSource Source { get; set; }
        public string Path { get; set; }
        public int Index { get; set; }
    }

    public sealed class ShortcutEntry
    {
        [JsonRequired] public Guid Id { get; set; }
        [JsonRequired] public Guid ProfileId { get; set; }
        [JsonRequired] public string RelativePath { get; set; }
        [JsonRequired] public ShortcutKind Kind { get; set; }
        public string[] Aliases { get; set; } = Array.Empty<string>();
        public bool IsPinned { get; set; }
        public int PinnedOrder { get; set; } = int.MaxValue;
        public int SortOrder { get; set; } = int.MaxValue;
        public long LaunchCount { get; set; }
        public DateTimeOffset? LastUsedUtc { get; set; }
        public CustomIconReference CustomIcon { get; set; }
        public bool Suppressed { get; set; }
        // Preserve the original timezone representation and unknown legacy fields.
        public JsonElement? LegacyRecord { get; set; }
    }
}
