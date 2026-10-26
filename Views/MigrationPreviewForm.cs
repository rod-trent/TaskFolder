using System;
using System.Linq;
using System.Windows.Forms;
using TaskFolder.Services;

namespace TaskFolder.Views
{
    internal sealed class MigrationPreviewForm : ThemedForm
    {
        private readonly TextBox _details = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical };
        private readonly Button _apply = new Button { AutoSize = true, Enabled = false };
        private readonly SettingsService _settings;
        private MigrationPreview _preview;
        private bool _busy;
        internal MigrationPreviewForm(SettingsService settings)
        {
            _settings = settings; Text = LocalizationService.Text("Preview catalog migration"); Size = new System.Drawing.Size(760, 580); StartPosition = FormStartPosition.CenterParent;
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            var preview = new Button { Text = LocalizationService.Text("Refresh preview"), AutoSize = true };
            _apply.Text = LocalizationService.Text("Create snapshot and migrate"); var cancel = new Button { Text = LocalizationService.Text("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { preview, _apply, cancel }); Controls.Add(_details); Controls.Add(buttons); CancelButton = cancel;
            preview.Click += async (_, _) => await RefreshPreview(); Shown += async (_, _) => await RefreshPreview();
            _apply.Click += async (_, _) => {
                if (_busy || _preview?.Result.Success != true || _preview.AlreadyMigrated) return;
                if (MessageBox.Show(this, LocalizationService.Text("Create the shown migration snapshot and state.v2.json in this data root? Original legacy files and raw EXE files remain unchanged."), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                _busy = true; var accepted = _preview;
                _apply.Enabled = false; var result = await System.Threading.Tasks.Task.Run(() => new StateMigrationService(settings.DataRoot).Commit(accepted)); _busy = false;
                if (IsDisposed) return; if (!result.Success) { _details.AppendText("\r\n" + result.Code); return; } settings.Load(); DialogResult = DialogResult.OK; Close();
            };
        }
        private async System.Threading.Tasks.Task RefreshPreview()
        {
            if (_busy) return; _busy = true;
            _apply.Enabled = false;
            _preview = await System.Threading.Tasks.Task.Run(() => new StateMigrationService(_settings.DataRoot).Preview());
            _busy = false;
            if (IsDisposed) return;
            _details.Text = _settings.DataRoot + "\r\n\r\n" + (_preview.Result.Success ? LocalizationService.Text("Profiles: {0}; folders: {1}; shortcuts: {2}; unresolved records: {3}", _preview.State.Profiles.Count, _preview.State.Folders.Count, _preview.State.Shortcuts.Count, _preview.State.LegacyAmbiguities.Count) : _preview.Result.Code) +
                "\r\n" + LocalizationService.Text("Snapshot: {0}; bytes: {1:N0}", _preview.SnapshotPath, _preview.SnapshotBytes) + "\r\n\r\n" + LocalizationService.Text(_preview.RecoveryRoute);
            _apply.Enabled = _preview.Result.Success && !_preview.AlreadyMigrated;
        }
    }
}
