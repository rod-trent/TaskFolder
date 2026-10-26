using System;
using System.Diagnostics;
using System.IO;
using TaskFolder.Models;

namespace TaskFolder.Services
{
    public sealed class ShortcutLaunchResult
    {
        public bool Dispatched { get; set; }
        public Exception DispatchError { get; set; }
        public OperationResult StatisticsResult { get; set; }
    }

    public sealed class ShortcutLaunchService
    {
        private readonly Action<ShortcutItem> _recordLaunch;
        private readonly Action<ProcessStartInfo> _dispatch;
        private readonly Func<string, bool> _fileExists;

        public ShortcutLaunchService(Action<ShortcutItem> recordLaunch)
            : this(recordLaunch, info => { Process.Start(info); }, File.Exists) { }

        internal ShortcutLaunchService(Action<ShortcutItem> recordLaunch,
            Action<ProcessStartInfo> dispatch, Func<string, bool> fileExists)
        {
            _recordLaunch = recordLaunch ?? throw new ArgumentNullException(nameof(recordLaunch));
            _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
            _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        }

        public ShortcutLaunchResult Launch(ShortcutItem shortcut)
        {
            var result = new ShortcutLaunchResult();
            try
            {
                if (shortcut == null) throw new ArgumentNullException(nameof(shortcut));
                string extension = Path.GetExtension(shortcut.ShortcutFilePath ?? string.Empty);
                var start = new ProcessStartInfo { UseShellExecute = true };
                if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase)) start.FileName = shortcut.TargetPath;
                else if (!string.IsNullOrEmpty(shortcut.ShortcutFilePath) && _fileExists(shortcut.ShortcutFilePath))
                    start.FileName = shortcut.ShortcutFilePath;
                else
                {
                    start.FileName = shortcut.TargetPath;
                    if (!string.IsNullOrEmpty(shortcut.Arguments)) start.Arguments = shortcut.Arguments;
                    if (!string.IsNullOrEmpty(shortcut.WorkingDirectory)) start.WorkingDirectory = shortcut.WorkingDirectory;
                }
                _dispatch(start);
                result.Dispatched = true;
            }
            catch (Exception ex) { result.DispatchError = ex; return result; }
            try { _recordLaunch(shortcut); result.StatisticsResult = new OperationResult(); }
            catch (StatePersistenceException ex) { result.StatisticsResult = ex.Result; }
            catch (Exception) { result.StatisticsResult = OperationResult.Fail(OperationStatus.IoFailure, "launch.statistics-save-failed"); }
            return result;
        }
    }
}
