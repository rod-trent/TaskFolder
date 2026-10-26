using L = TaskFolder.Services.LocalizationService;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Services
{
    internal sealed class AddItemService
    {
        private readonly SettingsService _settings;
        private readonly Func<string, string, string, bool, byte[]> _create;
        private readonly Func<string, (string Target, string Arguments, string WorkingDirectory)> _read;
        internal AddItemService(SettingsService settings, Func<string, string, string, bool, byte[]> create = null,
            Func<string, (string, string, string)> read = null)
        {
            _settings = settings; _read = read ?? WindowsShellLink.ReadDefinition;
            _create = create ?? ((target, args, cwd, shell) => {
                string staging = SafePath.Resolve(settings.DataRoot, Path.Combine("Staging", Guid.NewGuid().ToString("N") + ".lnk"));
                Directory.CreateDirectory(Path.GetDirectoryName(staging)); WindowsShellLink.SaveDefinition(staging, target, args, cwd, shell); return File.ReadAllBytes(staging); });
        }
        internal AddPreview Prepare(Guid profile, string folder, IEnumerable<AddInput> inputs, CancellationToken cancellation = default)
        {
            var preview = new AddPreview();
            try
            {
                _settings.Load(); var state = _settings.StateSnapshot;
                if (state == null || _settings.StateLoad.ReadOnly) throw new InvalidDataException(L.Text("Writable v2 catalog required."));
                var request = new OperationRequest { Kind = ShortcutOperationKind.Add, ProfileId = profile, DestinationFolder = folder,
                    ExpectedRevision = state.Revision, ExpectedStateFingerprint = _settings.StateLoad.Fingerprint };
                preview.Request = request; var signatures = new HashSet<string>(StringComparer.Ordinal);
                string Signature(string target, string args, string cwd) => target.ToUpperInvariant() + "\0" + (args ?? "") + "\0" + (cwd ?? "").ToUpperInvariant();
                foreach (var entry in state.Shortcuts.Where(e => !e.Suppressed && e.ProfileId == profile && (Path.GetDirectoryName(e.RelativePath) ?? "").Equals(folder ?? "", StringComparison.OrdinalIgnoreCase)))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string path = _settings.GetMetadata(entry.Id).FileName;
                    try
                    {
                        if (entry.Kind == ShortcutKind.ShellLink && File.Exists(path)) { var d = _read(path); signatures.Add(Signature(d.Target, d.Arguments, d.WorkingDirectory)); }
                        else if (entry.Kind == ShortcutKind.Url && File.Exists(path)) signatures.Add("url:" + ReadUrl(File.ReadAllBytes(path)));
                        else if (entry.Kind == ShortcutKind.Executable) signatures.Add(Signature(path,"",Path.GetDirectoryName(path)));
                    }
                    catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.COMException)
                    { preview.Summary.Add(L.Text("Existing definition could not be compared: {0}",entry.RelativePath)); }
                }
                foreach (var input in inputs)
                {
                    cancellation.ThrowIfCancellationRequested(); byte[] bytes; string extension, signature, detail;
                    string source = input.Catalog?.LinkPath ?? input.Source;
                    if (input.Kind == AddInputKind.ExistingShortcut || input.Kind == AddInputKind.Catalog && input.Catalog.LinkPath != null)
                    {
                        extension = Path.GetExtension(source).ToLowerInvariant(); if (extension != ".lnk" && extension != ".url") throw new InvalidDataException(L.Text("Choose .lnk or .url."));
                        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException(L.Text("Reparse sources are not supported."));
                        bytes = File.ReadAllBytes(source);
                        if (extension == ".url") { detail = ReadUrl(bytes); signature = "url:" + detail; }
                        else { var d = _read(source); signature = Signature(d.Target, d.Arguments, d.WorkingDirectory); detail = d.Target + " " + d.Arguments; }
                    }
                    else if (input.Kind == AddInputKind.Url)
                    { detail = ValidateUrl(input.Source); bytes = Encoding.UTF8.GetBytes("[InternetShortcut]\r\nURL=" + detail + "\r\n"); extension = ".url"; signature = "url:" + detail; }
                    else
                    {
                        bool shell = input.Kind == AddInputKind.Catalog; string target = shell ? input.Catalog.ShellName : input.Source;
                        if (!shell && (!Path.IsPathFullyQualified(target) || (!File.Exists(target) && !Directory.Exists(target)))) throw new InvalidDataException("Choose an existing absolute file/folder path.");
                        if (input.Kind == AddInputKind.Command && !File.Exists(target)) throw new InvalidDataException("Choose the executable/interpreter explicitly.");
                        string cwd = input.WorkingDirectory ?? (shell ? "" : Path.GetDirectoryName(target));
                        if (!string.IsNullOrEmpty(cwd) && (!Path.IsPathFullyQualified(cwd) || !Directory.Exists(cwd))) throw new InvalidDataException(L.Text("Working directory does not exist."));
                        bytes = _create(target, input.Arguments, cwd, shell); extension = ".lnk";
                        signature = Signature(target, input.Arguments, cwd); detail = target + " " + input.Arguments;
                    }
                    string name = string.IsNullOrWhiteSpace(input.Name) ? input.Catalog?.Name ?? Path.GetFileNameWithoutExtension(source) : input.Name.Trim();
                    if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException(L.Text("Enter a valid display name."));
                    if (!signatures.Add(signature)) preview.Duplicates.Add(name);
                    request.AddItems.Add(new OperationAddItem { RelativePath = name + extension, Kind = extension == ".url" ? ShortcutKind.Url : ShortcutKind.ShellLink, Bytes = bytes });
                    preview.Summary.Add(name + " → " + detail);
                }
                cancellation.ThrowIfCancellationRequested(); if (request.AddItems.Count == 0) throw new InvalidDataException(L.Text("Nothing selected."));
                var root = state.Profiles.Single(p => p.Id == profile).RelativeRoot;
                request.ExpectedDirectoryFingerprints = new Dictionary<string, string> { [root] = _settings.Operations.DirectoryFingerprint(root) };
                preview.Result = _settings.Operations.Preview(request);
            }
            catch (OperationCanceledException) { preview.Result = OperationResult.Fail(OperationStatus.Cancelled, "adding.cancelled"); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException || ex is UnauthorizedAccessException)
            { preview.Result = OperationResult.Fail(OperationStatus.ValidationError, "adding.invalid-input"); preview.Result.ItemErrors.Add(ex.Message); }
            return preview;
        }
        internal OperationResult Commit(AddPreview preview) => preview?.Result.Success == true ? _settings.ExecuteOperation(preview.Request) : preview?.Result ?? OperationResult.Fail(OperationStatus.ValidationError, "adding.preview-required");
        internal static string ReadUrl(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true); string text = reader.ReadToEnd();
            var fields = text.Replace("\r", "").Split('\n').Where(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (!text.Contains("[InternetShortcut]", StringComparison.OrdinalIgnoreCase) || fields.Length != 1) throw new InvalidDataException(L.Text("Invalid InternetShortcut."));
            return ValidateUrl(fields[0].Substring(4));
        }
        private static string ValidateUrl(string value)
        { if (value == null || value.IndexOfAny(new[] {'\r','\n','\0'}) >= 0 || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "http" && uri.Scheme != "https") throw new InvalidDataException("Enter an HTTP/HTTPS URL; use explicit command for other protocols."); return uri.AbsoluteUri; }
    }
}
