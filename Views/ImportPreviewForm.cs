using L = TaskFolder.Services.LocalizationService;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;

namespace TaskFolder.Views
{
    internal sealed class ImportPreviewForm : ThemedForm
    {
        private readonly SettingsService _settings;
        private readonly string _path;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly CheckedListBox _profiles = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, DisplayMember = "Name" };
        private readonly DataGridView _items = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        private readonly TextBox _prefix = new TextBox { Width = 180, Text = "Imported " };
        private readonly CheckBox _merge = new CheckBox { AutoSize = true }, _presentation = new CheckBox { AutoSize = true }, _copy = new CheckBox { AutoSize = true }, _reveal = new CheckBox { AutoSize = true };
        private readonly TextBox _report = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly Button _apply = new Button { AutoSize = true, Enabled = false }, _previewButton = new Button { AutoSize = true };
        private ImportPreview _preview;
        private bool _busy;
        private readonly Guid _copyToken = Guid.NewGuid();
        private string T(string key) => LocalizationService.Get("Backup_" + key, _settings.Settings.Language);
        internal ImportPreviewForm(SettingsService settings, string path)
        {
            _settings = settings; _path = path; Text = T("Import"); Size = new System.Drawing.Size(960, 800); MinimumSize = new System.Drawing.Size(800, 680); StartPosition = FormStartPosition.CenterParent;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
            foreach (int height in new[] { 90, 90, 240, 240, 45 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var settingsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            _merge.Text = T("Merge"); _presentation.Text = T("Presentation"); _copy.Text = T("Copy"); _reveal.Text = T("Reveal");
            settingsPanel.Controls.AddRange(new Control[] { new Label { Text = T("Prefix"), AutoSize = true }, _prefix, _merge, _presentation, _copy, _reveal }); layout.Controls.Add(settingsPanel, 0, 0);
            layout.Controls.Add(_profiles, 0, 1); layout.Controls.Add(_items, 0, 2); layout.Controls.Add(_report, 0, 3);
            _items.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = T("Include"), Name = "Include" });
            _items.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Item"), Name = "Item", ReadOnly = true });
            _items.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = T("Conflict"), Name = "Conflict", ValueMember = "Key", DisplayMember = "Label",
                DataSource = new[] { new { Key = "Skip", Label = L.Text("Skip") }, new { Key = "Rename", Label = L.Text("Rename") }, new { Key = "Replace", Label = L.Text("Replace") } } });
            _items.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Path"), Name = "Path" });
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill }; _previewButton.Text = T("Preview"); _apply.Text = T("Apply"); var cancel = new Button { Text = T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { _previewButton, _apply, cancel }); layout.Controls.Add(buttons, 0, 4); Controls.Add(layout); CancelButton = cancel;
            void InvalidatePreview() { _apply.Enabled = false; }
            _prefix.TextChanged += (_, _) => InvalidatePreview(); _merge.CheckedChanged += (_, _) => InvalidatePreview(); _presentation.CheckedChanged += (_, _) => InvalidatePreview(); _copy.CheckedChanged += (_, _) => InvalidatePreview();
            _profiles.ItemCheck += (_, _) => InvalidatePreview(); _items.CellValueChanged += (_, _) => InvalidatePreview(); _items.CurrentCellDirtyStateChanged += (_, _) => { if (_items.IsCurrentCellDirty) _items.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _reveal.CheckedChanged += (_, _) => ShowReport(); _previewButton.Click += async (_, _) => await Preview(false);
            _apply.Click += (_, _) => { if (_preview?.Result.Success != true || !_apply.Enabled) return;
                if (MessageBox.Show(this, T("Confirm"), Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                var result = new BackupService(settings).Apply(_preview); if (!result.Success) { _report.Text = result.Code; _apply.Enabled = false; return; } DialogResult = DialogResult.OK; Close(); };
            Shown += async (_, _) => await Preview(true);
        }
        private async System.Threading.Tasks.Task Preview(bool initial)
        {
            if (_busy) return; _busy = true; _apply.Enabled = false; _previewButton.Enabled = false; _items.EndEdit();
            var options = new ImportOptions { ProfilePrefix = _prefix.Text, MergeExisting = _merge.Checked, ApplyPresentationSettings = _presentation.Checked, CreateCopy = _copy.Checked, CopyToken = _copyToken };
            if (!initial) { options.SelectedProfiles = _profiles.CheckedItems.Cast<ProfileState>().Select(p => p.Id).ToHashSet(); options.SelectedItems = _items.Rows.Cast<DataGridViewRow>().Where(row => row.Cells[0].Value is true).Select(row => (Guid)row.Tag).ToHashSet();
                foreach (DataGridViewRow row in _items.Rows) { options.Conflicts[(Guid)row.Tag] = Enum.Parse<ImportConflict>((string)row.Cells[2].Value); options.RenamedPaths[(Guid)row.Tag] = (string)row.Cells[3].Value; } }
            // Freeze controls while the background validation runs, so the displayed preview is the committed request.
            _prefix.Enabled = _merge.Enabled = _presentation.Enabled = _copy.Enabled = _profiles.Enabled = _items.Enabled = false;
            try { _preview = await System.Threading.Tasks.Task.Run(() => new BackupService(new SettingsService(_settings.DataRoot)).Preview(_path, options, _cancellation.Token));
                if (IsDisposed) return;
                if (initial && _preview.Manifest?.State != null) { foreach (var p in _preview.Manifest.State.Profiles.Where(p => !p.Suppressed)) _profiles.Items.Add(p, true);
                    foreach (var item in _preview.Manifest.State.Shortcuts.Where(e => !e.Suppressed)) { int row = _items.Rows.Add(true, item.RelativePath, "Skip", item.RelativePath); _items.Rows[row].Tag = item.Id; } }
                ShowReport(); _apply.Enabled = !initial && _preview.Result.Success; }
            catch (Exception ex) { if (!IsDisposed) _report.Text = ex.Message; }
            finally { _busy = false; if (!IsDisposed) { _previewButton.Enabled = true; _prefix.Enabled = _merge.Enabled = _presentation.Enabled = _copy.Enabled = _profiles.Enabled = _items.Enabled = true; } }
        }
        private void ShowReport()
        { if (_preview == null) return; _report.Text = string.Join(Environment.NewLine, _preview.Summary.Concat(_preview.Warnings)) + "\r\n" + (_preview.Result.Success ? T("Ready") + $" {_preview.Imported} / {_preview.Skipped}; {_preview.Bytes} bytes" : _preview.Result.Code + "\r\n" + string.Join("\r\n", _preview.Result.ItemErrors));
            if (_reveal.Checked && _preview.Manifest != null) _report.AppendText("\r\n" + string.Join("\r\n", _preview.Manifest.Payloads.Where(p => p.EntryId != Guid.Empty).Select(p => p.Target + "\r\n" + p.Arguments + "\r\n" + p.WorkingDirectory))); }
        protected override void Dispose(bool disposing) { if (disposing) _cancellation.Cancel(); base.Dispose(disposing); }
    }
}
