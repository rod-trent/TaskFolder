using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    public sealed class StateLoadResult
    {
        public LauncherState State { get; set; }
        public OperationResult Result { get; set; }
        public string Fingerprint { get; set; }
        public bool RecoveredFromBackup { get; set; }
        public bool ReadOnly { get; set; }
    }

    internal enum StateWritePhase { BeforeWrite, BeforeReplace, AfterReplace, BeforeVerify }

    /// <summary>One revisioned snapshot; never writes legacy JSON or repairs corrupt files implicitly.</summary>
    public sealed class LauncherStateStore
    {
        internal static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private readonly string _root;
        private readonly Action<StateWritePhase> _fault;
        public string StatePath => Path.Combine(_root, "state.v2.json");
        public string BackupPath => StatePath + ".bak";

        public LauncherStateStore(string dataRoot) : this(dataRoot, null) { }
        internal LauncherStateStore(string dataRoot, Action<StateWritePhase> fault)
        {
            if (string.IsNullOrWhiteSpace(dataRoot) || !Path.IsPathFullyQualified(dataRoot))
                throw new ArgumentException("An absolute data root is required.", nameof(dataRoot));
            _root = Path.GetFullPath(dataRoot);
            _fault = fault;
        }

        public StateLoadResult Load()
        {
            var primary = Read(StatePath);
            if (primary.Result.Status == OperationStatus.UnsupportedSchema || primary.Result.Success && primary.State != null)
                return primary;
            // Access errors are not corruption: do not pretend that a denied/locked file is missing.
            if (primary.Result.Status == OperationStatus.AccessDenied || primary.Result.Status == OperationStatus.IoFailure)
                return primary;
            var backup = Read(BackupPath);
            if (backup.State != null && backup.Result.Success)
            {
                backup.RecoveredFromBackup = true;
                backup.ReadOnly = true;
                backup.Result = OperationResult.Fail(OperationStatus.RecoveryRequired, "state.backup-recovery-required");
                return backup;
            }
            if (!backup.Result.Success)
                return backup;
            return primary;
        }

        private StateLoadResult Read(string path)
        {
            try
            {
                string name = Path.GetFileName(path);
                SafePath.Resolve(_root, name);
                byte[] bytes = File.ReadAllBytes(path);
                using var doc = JsonDocument.Parse(bytes);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("SchemaVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schema))
                    throw new InvalidDataException("State schema is missing.");
                if (schema != LauncherState.CurrentSchemaVersion)
                    return new StateLoadResult { ReadOnly = true,
                        Result = OperationResult.Fail(OperationStatus.UnsupportedSchema, "state.unsupported-schema") };
                var state = JsonSerializer.Deserialize<LauncherState>(bytes, JsonOptions);
                Validate(state);
                return new StateLoadResult { State = state, Fingerprint = Convert.ToHexString(SHA256.HashData(bytes)),
                    Result = new OperationResult { NewRevision = state.Revision } };
            }
            catch (FileNotFoundException) { return new StateLoadResult { Result = new OperationResult() }; }
            catch (DirectoryNotFoundException) { return new StateLoadResult { Result = new OperationResult() }; }
            catch (UnauthorizedAccessException) { return Failure(OperationStatus.AccessDenied, "state.access-denied"); }
            catch (Exception ex) when (ex is JsonException || ex is InvalidDataException || ex is ArgumentException || ex is OverflowException)
            { return Failure(OperationStatus.ValidationError, "state.invalid"); }
            catch (IOException) { return Failure(OperationStatus.IoFailure, "state.read-failed"); }
        }

        private static StateLoadResult Failure(OperationStatus status, string code) =>
            new StateLoadResult { ReadOnly = true, Result = OperationResult.Fail(status, code) };

        internal FileStream AcquireWriterLease()
        {
            string path = SafePath.Resolve(_root, "state.writer.lock");
            Directory.CreateDirectory(_root);
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        public OperationResult Commit(LauncherState candidate, long expectedRevision, string expectedFingerprint = null)
            => CommitCore(candidate, expectedRevision, expectedFingerprint, null);

        internal OperationResult CommitWithLease(LauncherState candidate, long expectedRevision,
            string expectedFingerprint, FileStream writerLease)
        {
            if (writerLease == null || !writerLease.CanWrite || !string.Equals(writerLease.Name,
                Path.Combine(_root, "state.writer.lock"), StringComparison.OrdinalIgnoreCase))
                return OperationResult.Fail(OperationStatus.ValidationError, "state.invalid-writer-lease");
            return CommitCore(candidate, expectedRevision, expectedFingerprint, writerLease);
        }

        private OperationResult CommitCore(LauncherState candidate, long expectedRevision, string expectedFingerprint, FileStream writerLease)
        {
            long? replacedRevision = null;
            try
            {
                Validate(candidate);
                // An exclusive handle coordinates independent store instances/processes on this data root.
                using var writer = writerLease == null ? AcquireWriterLease() : null;
                var current = Load();
                if (!current.Result.Success || current.ReadOnly) return current.Result;
                if ((current.State?.Revision ?? 0) != expectedRevision || candidate.Revision != expectedRevision ||
                    expectedFingerprint != null && current.Fingerprint != expectedFingerprint)
                    return OperationResult.Fail(OperationStatus.Conflict, "state.stale-revision");
                // Serialize a detached copy so a failed write never advances the caller's revision.
                var pending = Clone(candidate);
                pending.Revision = checked(expectedRevision + 1);
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(pending, JsonOptions);
                string temporary = StatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                _fault?.Invoke(StateWritePhase.BeforeWrite);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }
                _fault?.Invoke(StateWritePhase.BeforeReplace);
                var recheck = Load();
                if (!recheck.Result.Success || recheck.ReadOnly) return recheck.Result;
                if (current.Fingerprint != recheck.Fingerprint)
                    return OperationResult.Fail(OperationStatus.Conflict, "state.changed-during-write");
                if (current.State == null) File.Move(temporary, StatePath);
                else File.Replace(temporary, StatePath, BackupPath);
                replacedRevision = pending.Revision;
                _fault?.Invoke(StateWritePhase.AfterReplace);
                _fault?.Invoke(StateWritePhase.BeforeVerify);
                var verified = Read(StatePath);
                if (!verified.Result.Success || verified.Fingerprint != Convert.ToHexString(SHA256.HashData(bytes)))
                    return new OperationResult { Status = OperationStatus.IoFailure, Code = "state.verify-failed", NewRevision = replacedRevision };
                return new OperationResult { NewRevision = pending.Revision };
            }
            catch (UnauthorizedAccessException) { return OperationResult.Fail(OperationStatus.AccessDenied, "state.access-denied"); }
            catch (Exception ex) when (ex is JsonException || ex is InvalidDataException || ex is ArgumentException || ex is OverflowException)
            { return OperationResult.Fail(OperationStatus.ValidationError, "state.invalid"); }
            catch (IOException)
            {
                return new OperationResult { Status = OperationStatus.IoFailure,
                    Code = replacedRevision.HasValue ? "state.commit-outcome-reload-required" : "state.write-failed",
                    NewRevision = replacedRevision };
            }
        }

        internal static LauncherState Clone(LauncherState state) =>
            JsonSerializer.Deserialize<LauncherState>(JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions), JsonOptions);

        internal void Validate(LauncherState state)
        {
            if (state == null || state.SchemaVersion != 2 || state.Revision < 0 || state.Settings == null ||
                state.Profiles == null || state.Folders == null || state.Shortcuts == null || state.LegacyAmbiguities == null ||
                state.CommittedOperationIds == null || state.CommittedOperationIds.Any(id => id == Guid.Empty) ||
                state.CommittedOperationIds.Distinct().Count() != state.CommittedOperationIds.Count)
                throw new InvalidDataException("Incomplete state.");
            var ids = new HashSet<Guid>();
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var profiles = new Dictionary<Guid, string>();
            foreach (var profile in state.Profiles)
            {
                if (profile == null || profile.Id == Guid.Empty || !ids.Add(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
                    throw new InvalidDataException("Invalid profile identity.");
                string required = profile.Id == ProfileState.DefaultId ? "Shortcuts" : Path.Combine("Profiles", profile.Name);
                if (!string.Equals(profile.RelativeRoot, required, StringComparison.OrdinalIgnoreCase) ||
                    (profile.Id == ProfileState.DefaultId) != string.Equals(profile.Name, "Default", StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(profile.Name) != profile.Name ||
                    !roots.Add(profile.RelativeRoot)) throw new InvalidDataException("Invalid profile binding.");
                profiles.Add(profile.Id, SafePath.Resolve(_root, profile.RelativeRoot));
            }
            if (!profiles.ContainsKey(ProfileState.DefaultId)) throw new InvalidDataException("Default profile is missing.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in state.Folders)
            {
                if (folder == null || folder.Id == Guid.Empty || !ids.Add(folder.Id) || !profiles.ContainsKey(folder.ProfileId) ||
                    !paths.Add(SafePath.Resolve(profiles[folder.ProfileId], folder.RelativePath))) throw new InvalidDataException("Invalid folder identity.");
            }
            foreach (var entry in state.Shortcuts)
            {
                if (entry == null || entry.Id == Guid.Empty || !ids.Add(entry.Id) || !profiles.ContainsKey(entry.ProfileId) ||
                    entry.LaunchCount < 0 || entry.Aliases == null || entry.Aliases.Any(a => a == null) ||
                    !paths.Add(SafePath.Resolve(profiles[entry.ProfileId], entry.RelativePath))) throw new InvalidDataException("Invalid shortcut identity.");
                string extension = Path.GetExtension(entry.RelativePath).ToLowerInvariant();
                if (!Enum.IsDefined(entry.Kind) || extension != (entry.Kind == ShortcutKind.ShellLink ? ".lnk" :
                    entry.Kind == ShortcutKind.Url ? ".url" : ".exe")) throw new InvalidDataException("Invalid shortcut kind.");
                if (entry.CustomIcon != null)
                {
                    if (!Enum.IsDefined(entry.CustomIcon.Source) || string.IsNullOrWhiteSpace(entry.CustomIcon.Path))
                        throw new InvalidDataException("Invalid icon reference.");
                    if (entry.CustomIcon.Source == IconSource.ManagedAsset)
                    {
                        if (!entry.CustomIcon.Path.StartsWith("Assets" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Invalid managed icon root.");
                        SafePath.Resolve(_root, entry.CustomIcon.Path);
                    }
                    else if (!Path.IsPathFullyQualified(entry.CustomIcon.Path)) throw new InvalidDataException("External icon must be absolute.");
                }
            }
            foreach (var ambiguity in state.LegacyAmbiguities)
                if (ambiguity == null || ambiguity.OriginalRecord.ValueKind == JsonValueKind.Undefined || ambiguity.CandidateIds == null ||
                    ambiguity.CandidateIds.Any(id => !state.Shortcuts.Any(s => s.Id == id)))
                    throw new InvalidDataException("Invalid legacy ambiguity.");
            if (state.Profiles.Any(p => p.Id == ProfileState.DefaultId && p.Suppressed) ||
                !state.Profiles.Any(p => !p.Suppressed && p.Name.Equals(state.Settings.ActiveProfileName, StringComparison.OrdinalIgnoreCase)) ||
                state.Settings.MenuItemHeight < 32 || state.Settings.MenuItemHeight > 72 ||
                !float.IsFinite(state.Settings.MenuFontSize) || state.Settings.MenuFontSize < 9 || state.Settings.MenuFontSize > 18 ||
                state.Settings.WidgetHoverDelayMs < 100 || state.Settings.WidgetHoverDelayMs > 1500 ||
                state.Settings.WidgetSizePercent < 50 || state.Settings.WidgetSizePercent > 200 ||
                state.Settings.WidgetMenuHideDelayMs < 200 || state.Settings.WidgetMenuHideDelayMs > 2000 ||
                !new[] { "System", "English", "Русский" }.Contains(state.Settings.Language) ||
                !new[] { "System", "Light", "Dark" }.Contains(state.Settings.Theme))
                throw new InvalidDataException("Invalid presentation preferences.");
        }
    }
}
