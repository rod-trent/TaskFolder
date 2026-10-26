using L = TaskFolder.Services.LocalizationService;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;

namespace TaskFolder.Views
{
    internal sealed class AddItemDialog : ThemedForm
    {
        private readonly SettingsService _settings;
        private readonly ComboBox _profile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
        private readonly ComboBox _folder = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly ComboBox _kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
        private readonly TextBox _name = new TextBox { Width = 300 }, _source = new TextBox { Width = 470 }, _args = new TextBox { Width = 470 }, _cwd = new TextBox { Width = 470 };
        private readonly ListBox _catalog = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 28 };
        private readonly ListBox _batch = new ListBox { Dock = DockStyle.Fill };
        private readonly TextBox _catalogFilter = new TextBox { Dock = DockStyle.Top };
        private readonly TextBox _details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
        private readonly Button _previewButton = new Button { AutoSize = true }, _commit = new Button { AutoSize = true, Enabled = false };
        private readonly List<AddInput> _inputs = new List<AddInput>();
        private readonly List<CatalogApp> _apps = new List<CatalogApp>();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly LauncherState _snapshot;
        private AddPreview _preview;
        private bool _busy;
        private string T(string key) => LocalizationService.Get("Add_" + key, _settings.Settings.Language);
        internal AddItemDialog(SettingsService settings, Guid profile = default, string folder = "", IEnumerable<AddInput> initial = null)
        {
            _settings = settings; _snapshot = settings.StateSnapshot;
            Text = T("Title"); Size = new Size(900, 850); MinimumSize = new Size(760, 700); StartPosition = FormStartPosition.CenterParent;
            _name.AccessibleName=T("Name"); _source.AccessibleName=T("Source"); _args.AccessibleName=T("Arguments"); _cwd.AccessibleName=T("WorkingDirectory"); _kind.AccessibleName=T("Kind");
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(12) };
            foreach (int height in new[] { 40, 195, 170, 40, 110, 130, 40 }) root.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            var destination = new FlowLayoutPanel { Dock = DockStyle.Fill }; destination.Controls.AddRange(new Control[] { Label(T("Destination")), _profile, _folder }); root.Controls.Add(destination, 0, 0);
            _profile.DisplayMember = "Name";
            if (_snapshot != null) foreach (var item in _snapshot.Profiles.Where(p => !p.Suppressed)) _profile.Items.Add(item);
            _profile.SelectedIndexChanged += (_, _) => { _folder.Items.Clear(); _folder.Items.Add(""); if (_profile.SelectedItem is ProfileState p)
                foreach (var item in _snapshot.Folders.Where(f => f.ProfileId == p.Id && !f.Suppressed).OrderBy(f => f.RelativePath)) _folder.Items.Add(item.RelativePath); _folder.SelectedIndex = 0; InvalidatePreview(); };
            _profile.SelectedItem = _snapshot?.Profiles.FirstOrDefault(p => p.Id == profile || profile == Guid.Empty && p.Name == settings.Settings.ActiveProfileName);
            if (_folder.Items.Contains(folder)) _folder.SelectedItem = folder;
            _folder.SelectedIndexChanged += (_, _) => InvalidatePreview();
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5 };
            string[] labels = { "Kind", "Name", "Source", "Arguments", "WorkingDirectory" }; Control[] controls = { _kind, _name, _source, _args, _cwd };
            for (int i = 0; i < labels.Length; i++) { fields.Controls.Add(Label(T(labels[i])), 0, i); fields.Controls.Add(controls[i], 1, i); }
            _kind.Items.AddRange(new object[] { T("Existing"), T("Path"), T("Url"), T("Command") }); _kind.SelectedIndex = 0;
            var browse = new Button { Text = T("Browse"), AutoSize = true }; var browseFolder = new Button { Text = T("Folder"), AutoSize = true };
            fields.Controls.Add(browse, 2, 2); fields.Controls.Add(browseFolder, 2, 4); root.Controls.Add(fields, 0, 1);
            browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Multiselect = _kind.SelectedIndex == 0, Filter = _kind.SelectedIndex == 0 ? "Shortcuts (*.lnk;*.url)|*.lnk;*.url" : "Files (*.*)|*.*" };
                if (dialog.ShowDialog(this) == DialogResult.OK) { if (_kind.SelectedIndex == 0 && dialog.FileNames.Length > 1) foreach (string path in dialog.FileNames) Queue(new AddInput { Kind = AddInputKind.ExistingShortcut, Source = path });
                    else _source.Text = dialog.FileName; } };
            browseFolder.Click += (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) { _kind.SelectedIndex = 1; _source.Text = dialog.SelectedPath; } };
            var catalogPanel = new Panel { Dock = DockStyle.Fill }; catalogPanel.Controls.Add(_catalog); catalogPanel.Controls.Add(_catalogFilter); root.Controls.Add(catalogPanel, 0, 2);
            if (!InstalledAppCatalogService.PackagedProviderVerified) catalogPanel.Controls.Add(new Label { Dock=DockStyle.Bottom,Height=35,Text=T("CatalogLimited"),AutoEllipsis=true });
            _catalogFilter.AccessibleName = T("CatalogSearch"); _catalogFilter.TextChanged += (_, _) => RenderCatalog();
            _catalog.DrawItem += (_, args) => { args.DrawBackground(); if (args.Index >= 0) { var app = (CatalogApp)_catalog.Items[args.Index];
                args.Graphics.DrawIcon(app.Icon ?? SystemIcons.Application, new Rectangle(args.Bounds.X + 3, args.Bounds.Y + 4, 20, 20));
                TextRenderer.DrawText(args.Graphics, app.ToString(), args.Font, new Rectangle(args.Bounds.X + 28, args.Bounds.Y, args.Bounds.Width - 30, args.Bounds.Height), args.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis); } args.DrawFocusRectangle(); };
            var queue = new FlowLayoutPanel { Dock = DockStyle.Fill }; var add = new Button { Text = T("Queue"), AutoSize = true }; var apps = new Button { Text = T("QueueApps"), AutoSize = true };
            var remove = new Button { Text = T("Remove"), AutoSize = true }; queue.Controls.AddRange(new Control[] { add, apps, remove }); root.Controls.Add(queue, 0, 3);
            add.Click += (_, _) => Queue(new AddInput { Kind = (AddInputKind)_kind.SelectedIndex, Name = _name.Text, Source = _source.Text, Arguments = _args.Text, WorkingDirectory = _cwd.Text.Length == 0 ? null : _cwd.Text });
            apps.Click += (_, _) => { foreach (CatalogApp app in _catalog.SelectedItems) Queue(new AddInput { Kind = AddInputKind.Catalog, Catalog = app }); };
            remove.Click += (_, _) => { if (_batch.SelectedIndex >= 0) { _inputs.RemoveAt(_batch.SelectedIndex); RenderBatch(); } };
            root.Controls.Add(_batch, 0, 4); root.Controls.Add(_details, 0, 5);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill }; _previewButton.Text = T("Preview"); _commit.Text = T("Apply"); var cancel = new Button { Text = T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.AddRange(new Control[] { _previewButton, _commit, cancel }); root.Controls.Add(buttons, 0, 6); Controls.Add(root); CancelButton = cancel;
            _previewButton.Click += async (_, _) => {
                if (_profile.SelectedItem is not ProfileState p || _inputs.Count == 0 || _busy) return;
                _busy = true; _previewButton.Enabled = false; _commit.Enabled = false; var input = _inputs.ToArray(); string destinationFolder = _folder.Text;
                try { var preview = await StaWorker.Run(() => new AddItemService(new SettingsService(settings.DataRoot)).Prepare(p.Id, destinationFolder, input, _cancel.Token), _cancel.Token);
                    if (IsDisposed || _cancel.IsCancellationRequested) return;
                    if (_inputs.SequenceEqual(input) && _profile.SelectedItem == p && _folder.Text == destinationFolder) { _preview = preview; _details.Text = string.Join(Environment.NewLine, preview.Summary) + "\r\n" +
                        (preview.Result.Success ? T("Ready") : preview.Result.Code + "\r\n" + string.Join("\r\n", preview.Result.ItemErrors)) + (preview.Duplicates.Count == 0 ? "" : "\r\n" + T("Duplicates") + ": " + string.Join(", ", preview.Duplicates)); _commit.Enabled = preview.Result.Success; } }
                catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed) _details.Text = ex.Message; }
                finally { _busy = false; if (!IsDisposed) _previewButton.Enabled = true; }
            };
            _commit.Click += (_, _) => { if (_preview?.Result.Success != true) return;
                if (_preview.Duplicates.Count > 0 && MessageBox.Show(this, T("DuplicateConfirm"), Text, MessageBoxButtons.OKCancel) != DialogResult.OK) return;
                var result = settings.ExecuteOperation(_preview.Request); if (!result.Success) { _details.Text = result.Code; InvalidatePreview(); return; } DialogResult = DialogResult.OK; Close(); };
            if (initial != null) foreach (var input in initial) Queue(input);
            Shown += async (_, _) => { try { await new InstalledAppCatalogService().LoadAsync(app => { if (IsDisposed || _cancel.IsCancellationRequested) return; BeginInvoke(new Action(() => { if (!IsDisposed && !_cancel.IsCancellationRequested) { if (!_apps.Any(a => a.Identity == app.Identity)) { _apps.Add(app); RenderCatalog(); } else _catalog.Invalidate(); } })); }, _cancel.Token); }
                catch (OperationCanceledException) { } catch (Exception ex) { if (!IsDisposed) _details.Text = T("CatalogPartial") + " " + ex.Message; } };
        }
        private static Label Label(string text) => new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 8, 3) };
        private void Queue(AddInput input) { if (_busy) return; _inputs.Add(input); RenderBatch(); }
        private void RenderBatch() { _batch.Items.Clear(); foreach (var input in _inputs) _batch.Items.Add(input.Name ?? input.Catalog?.Name ?? input.Source ?? "?"); InvalidatePreview(); }
        private void RenderCatalog() { var selected = _catalog.SelectedItems.Cast<CatalogApp>().Select(a => a.Identity).ToHashSet(); _catalog.BeginUpdate(); _catalog.Items.Clear();
            foreach (var app in _apps.Where(a => a.Name.Contains(_catalogFilter.Text, StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.Name)) { int index = _catalog.Items.Add(app); if (selected.Contains(app.Identity)) _catalog.SetSelected(index, true); } _catalog.EndUpdate(); }
        private void InvalidatePreview() { _preview = null; _commit.Enabled = false; }
        protected override void Dispose(bool disposing) { if (disposing) { _cancel.Cancel(); foreach (var app in _apps) app.Icon?.Dispose(); } base.Dispose(disposing); }
    }
}
