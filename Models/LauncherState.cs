using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskFolder.Models
{
    public sealed class LegacyAmbiguity
    {
        public JsonElement OriginalRecord { get; set; }
        public Guid[] CandidateIds { get; set; } = Array.Empty<Guid>();
        public string ResolutionState { get; set; } = "Unresolved";
    }

    public sealed class LauncherState
    {
        public const int CurrentSchemaVersion = 2;
        [JsonRequired] public int SchemaVersion { get; set; } = CurrentSchemaVersion;
        [JsonRequired] public long Revision { get; set; }
        [JsonRequired] public List<ProfileState> Profiles { get; set; } = new List<ProfileState>();
        [JsonRequired] public List<FolderState> Folders { get; set; } = new List<FolderState>();
        [JsonRequired] public List<ShortcutEntry> Shortcuts { get; set; } = new List<ShortcutEntry>();
        [JsonRequired] public AppSettings Settings { get; set; } = new AppSettings();
        [JsonRequired] public List<LegacyAmbiguity> LegacyAmbiguities { get; set; } = new List<LegacyAmbiguity>();
        public JsonElement? LegacySettings { get; set; }
        public List<Guid> CommittedOperationIds { get; set; } = new List<Guid>();
    }
}
