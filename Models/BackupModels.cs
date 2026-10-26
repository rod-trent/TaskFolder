using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TaskFolder.Models
{
    internal sealed class BackupManifest
    {
        [JsonRequired] public int SchemaVersion { get; set; } = 1;
        [JsonRequired] public LauncherState State { get; set; }
        [JsonRequired] public List<BackupPayload> Payloads { get; set; } = new List<BackupPayload>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
    internal sealed class BackupPayload
    {
        public string ArchivePath { get; set; }
        public Guid EntryId { get; set; }
        public string AssetPath { get; set; }
        public string Sha256 { get; set; }
        public string Target { get; set; }
        public string Arguments { get; set; }
        public string WorkingDirectory { get; set; }
    }
    internal enum ImportConflict { Skip, Rename, Replace }
    internal sealed class ImportOptions
    {
        internal HashSet<Guid> SelectedProfiles;
        internal HashSet<Guid> SelectedItems;
        internal string ProfilePrefix = "Imported ";
        internal bool MergeExisting, ApplyPresentationSettings;
        internal bool CreateCopy;
        internal Guid CopyToken = Guid.NewGuid();
        internal Dictionary<Guid, ImportConflict> Conflicts = new Dictionary<Guid, ImportConflict>();
        internal Dictionary<Guid, string> RenamedPaths = new Dictionary<Guid, string>();
    }
    internal sealed class ImportPreview
    {
        internal BackupManifest Manifest;
        internal OperationRequest Request;
        internal OperationResult Result;
        internal string ArchiveHash;
        internal int Imported, Skipped;
        internal long Bytes;
        internal List<string> Warnings = new List<string>();
        internal List<string> Summary = new List<string>();
    }
}
