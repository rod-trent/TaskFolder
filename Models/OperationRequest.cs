using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TaskFolder.Models
{
    public enum ShortcutOperationKind
    {
        Rename, Move, Copy, Reorder, CreateFolder, Remove, Restore, Repair,
        SaveSnapshot, CreateProfile, DeleteProfile, Add, Reconcile,
        Pin, Unpin, ReorderPins, ApplyImport
    }

    public sealed class OperationAddItem
    {
        public string RelativePath { get; set; }
        public ShortcutKind Kind { get; set; }
        public byte[] Bytes { get; set; }
    }

    public sealed class OperationRequest
    {
        public Guid OperationId { get; set; } = Guid.NewGuid();
        public long ExpectedRevision { get; set; }
        public string ExpectedStateFingerprint { get; set; }
        public Guid ProfileId { get; set; }
        public Guid[] SelectedIds { get; set; } = Array.Empty<Guid>();
        public ShortcutOperationKind Kind { get; set; }
        public Guid DestinationProfileId { get; set; }
        public string DestinationFolder { get; set; } = string.Empty;
        public string Name { get; set; }
        public Guid RestoreOperationId { get; set; }
        public Dictionary<Guid, int> Orders { get; set; } = new Dictionary<Guid, int>();
        public Dictionary<Guid, string> ReplacementNames { get; set; } = new Dictionary<Guid, string>();
        public Dictionary<Guid, string> ExpectedFileFingerprints { get; set; } = new Dictionary<Guid, string>();
        public byte[] ReplacementBytes { get; set; }
        public LauncherState Snapshot { get; set; }
        // Data-root-relative watcher mappings. No target-based inference on offline changes.
        public Dictionary<string, string> RenamedBindings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<OperationAddItem> AddItems { get; set; } = new List<OperationAddItem>();
        public bool Cancelled { get; set; }
        // Omit when absent to preserve fingerprints of pre-S4 requests.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string> ExpectedDirectoryFingerprints { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ImportFile> ImportFiles { get; set; }
    }

    public sealed class ImportFile
    {
        public string Path { get; set; }
        public byte[] Bytes { get; set; }
        public bool Replace { get; set; }
    }

    public sealed class CatalogChangedEventArgs : EventArgs
    {
        public long Revision { get; set; }
        public Guid ProfileId { get; set; }
        public Guid[] ChangedIds { get; set; } = Array.Empty<Guid>();
        public ShortcutOperationKind ChangeKind { get; set; }
    }

    public sealed class RecoveryOperationInfo
    {
        public Guid OperationId { get; set; }
        public DateTimeOffset CreatedUtc { get; set; }
        public string Kind { get; set; }
        public string Phase { get; set; }
        public string[] ItemPaths { get; set; } = Array.Empty<string>();
        public Dictionary<Guid, string> OriginalItemPaths { get; set; } = new Dictionary<Guid, string>();
        public long PayloadBytes { get; set; }
        public long MetadataBytes { get; set; }
        public bool CanRestore { get; set; }
        public bool Pending { get; set; }
        public string Error { get; set; }
    }
}
