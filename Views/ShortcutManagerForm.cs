using L = TaskFolder.Services.LocalizationService;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

namespace TaskFolder.Views
{
    internal sealed class ShortcutManagerForm : ThemedForm
    {
        private sealed class TreeLocation
        {
            internal Guid Profile;
            internal Guid Folder;
            internal string Path = string.Empty;
        }
        private readonly SettingsService _settings;
        private readonly ShortcutManager _manager;
        private readonly OrganizerSession _session;
        private readonly Font _font = Windows11MenuStyle.CreateFont(10);
        private readonly TreeView _tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, AllowDrop = true };
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
            MultiSelect = true, HideSelection = false, AllowDrop = true };
        private readonly TextBox _filter = new TextBox { Width = 250 };
        private readonly CheckBox _attention = new CheckBox { AutoSize = true };
        private readonly TextBox _aliases = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
        private readonly Label _aliasTitle = new Label { AutoSize = true };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly Label _location = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        private readonly Button _saveAliases = new Button { AutoSize = true };
        private readonly ToolStrip _toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        private readonly List<ToolStripItem> _mutators = new List<ToolStripItem>();
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private readonly Timer _refreshTimer = new Timer { Interval = 500 };
        private readonly object _watchLock = new object();
        private readonly Dictionary<string, string> _renames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private bool _diskChanged, _treeSelection, _watchNeedsRestart;
        private string _watchSignature;
        private bool _rendering, _working;
        private string T(string key) => LocalizationService.Get("Manager_" + key, _settings.Settings.Language);

        internal ShortcutManagerForm(SettingsService settings, ShortcutManager manager)
        {
            _settings = settings; _manager = manager; _session = new OrganizerSession(settings, manager);
            Text = T("Title"); Font = _font; Size = new Size(1080, 740); MinimumSize = new Size(820, 580);
            StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
            _tree.AccessibleName = T("Locations"); _list.AccessibleName = T("Items"); _filter.AccessibleName = T("Search");
            _aliases.AccessibleName = T("Aliases"); _attention.Text = T("Attention");
            _list.Columns.Add(T("Name"), 240); _list.Columns.Add(T("Type"), 100);
            _list.Columns.Add(T("Status"), 140); _list.Columns.Add(T("Folder"), 240);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1000, 400), SplitterDistance = 260 };
            split.Panel1.Controls.Add(_tree); split.Panel2.Controls.Add(_list);
            var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            filters.Controls.Add(new Label { Text = T("Search"), AutoSize = true }); filters.Controls.Add(_filter); filters.Controls.Add(_attention);
            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(0, 8, 0, 0) };
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize)); editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editor.Controls.Add(_aliasTitle, 0, 0); editor.Controls.Add(_aliases, 0, 1);
            var aliasCommands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            _saveAliases.Text = T("SaveAliases"); var discard = new Button { Text = T("DiscardAliases"), AutoSize = true };
            aliasCommands.Controls.Add(_saveAliases); aliasCommands.Controls.Add(discard);
            aliasCommands.Controls.Add(new Label { Text = T("AliasHint"), AutoSize = true }); editor.Controls.Add(aliasCommands, 0, 2);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.Controls.Add(filters, 0, 0); layout.Controls.Add(_location, 0, 1); layout.Controls.Add(split, 0, 2);
            layout.Controls.Add(editor, 0, 3); layout.Controls.Add(_status, 0, 4);
            Controls.Add(layout); Controls.Add(_toolbar);
            var actions = new (string Key, Action Action)[] { ("Add", ExistingLinks), ("NewFolder", NewFolder), ("Rename", Rename), ("Move", () => Transfer(false)),
                ("Copy", () => Transfer(true)), ("Up", () => Reorder(-1)), ("Down", () => Reorder(1)), ("Pin", Pin), ("Remove", Remove), ("Repair", Repair) };
            foreach (var action in actions)
            { var button = new ToolStripButton(T(action.Key)); button.Click += (_, _) => Guard(action.Action); _toolbar.Items.Add(button); _mutators.Add(button); }
            var refresh = new ToolStripButton(T("Refresh")); refresh.Click += (_, _) => Run(() => { _manager.ReloadShortcuts(); return ReconcileDisk(); }); _toolbar.Items.Add(refresh);
            var activate = new ToolStripButton(T("UseProfile")); activate.Click += (_, _) => Guard(() =>
                new ProfileService(_settings, _manager).SwitchProfile(_session.Snapshot.Profiles.Single(profile => profile.Id == _session.ProfileId).Name));
            _toolbar.Items.Add(activate); _mutators.Add(activate);
            var export = new ToolStripButton(LocalizationService.Get("Backup_Export", settings.Settings.Language)); export.Click += async (_, _) => {
                using var file = new SaveFileDialog { Filter = "TaskFolder archive (*.zip)|*.zip", FileName = "TaskFolder-config.zip" };
                if (file.ShowDialog(this) != DialogResult.OK) return;
                _working = true; try { await StaWorker.Run(() => new BackupService(new SettingsService(settings.DataRoot)).Export(file.FileName), System.Threading.CancellationToken.None);
                    MessageBox.Show(this, LocalizationService.Get("Backup_Exported", settings.Settings.Language), Text); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); } finally { _working = false; RefreshView(); } };
            var import = new ToolStripButton(LocalizationService.Get("Backup_Import", settings.Settings.Language)); import.Click += (_, _) => {
                using var file = new OpenFileDialog { Filter = "TaskFolder archive (*.zip)|*.zip" }; if (file.ShowDialog(this) != DialogResult.OK) return;
                using var form = new ImportPreviewForm(settings, file.FileName); form.ShowDialog(this); RefreshView(); };
            _toolbar.Items.Add(export); _toolbar.Items.Add(import); _mutators.Add(import); _mutators.Add(export);
            var context = new ContextMenuStrip();
            foreach (var action in actions) { var item = new ToolStripMenuItem(T(action.Key)); item.Click += (_, _) => Guard(action.Action); context.Items.Add(item); }
            context.Opening += (_, _) => { foreach (ToolStripItem item in context.Items) item.Enabled = !_session.ReadOnly; };
            _list.ContextMenuStrip = context; _tree.ContextMenuStrip = context;
            _tree.AfterSelect += (_, args) => { if (!_rendering && args.Node.Tag is TreeLocation location) { _treeSelection = true; _session.Navigate(location.Profile, location.Path); RefreshView(); } };
            _tree.Enter += (_, _) => _treeSelection = true; _list.Enter += (_, _) => _treeSelection = false;
            _tree.NodeMouseClick += (_, args) => { _treeSelection = true; _tree.SelectedNode = args.Node; };
            _list.MouseDown += (_, _) => _treeSelection = false;
            _list.ItemActivate += (_, _) => { var rows = SelectedRows(); if (rows.Length == 1 && rows[0].IsFolder) { _session.Navigate(rows[0].ProfileId, rows[0].RelativePath); RefreshView(); } };
            _list.SelectedIndexChanged += (_, _) => { if (!_rendering) UpdateAliasEditor(); };
            _filter.TextChanged += (_, _) => { _session.Query = _filter.Text; RenderRows(); };
            _attention.CheckedChanged += (_, _) => { _session.AttentionOnly = _attention.Checked; RenderRows(); };
            _aliases.TextChanged += (_, _) => { if (!_rendering) { _session.SetAliasText(_aliases.Text); UpdateAliasButtons(); } };
            _saveAliases.Click += (_, _) => Run(() => _session.SaveAliases());
            discard.Click += (_, _) => { _session.DiscardAliases(); UpdateAliasEditor(); };
            _list.ItemDrag += (_, _) => BeginDrag(SelectedIds());
            _tree.ItemDrag += (_, args) => { if (args.Item is TreeNode node && node.Tag is TreeLocation location && location.Folder != Guid.Empty) { _tree.SelectedNode = node; BeginDrag(new[] { location.Folder }); } };
            _tree.DragEnter += OnItemDragOver; _tree.DragOver += OnItemDragOver; _list.DragEnter += OnItemDragOver; _list.DragOver += OnItemDragOver;
            _tree.DragDrop += Drop; _list.DragDrop += Drop;
            KeyDown += OnKeyDown;
            FormClosing += (_, args) => { if (_session.Draft?.Dirty == true && MessageBox.Show(this, T("DiscardOnClose"), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) args.Cancel = true; };
            _settings.Operations.CatalogChanged += CatalogChanged;
            _manager.ShortcutsChanged += ShortcutsChanged;
            _refreshTimer.Tick += (_, _) => { if (_working || _rendering) return; lock (_watchLock) { if (!_diskChanged) return; } Run(ReconcileDisk); };
            _refreshTimer.Start();
            Shown += (_, _) => RefreshView(); RefreshView();
        }

        private void CatalogChanged(object sender, CatalogChangedEventArgs args)
        {
            if (IsDisposed || _working || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke(new Action(RefreshView)); else RefreshView();
        }
        private void ShortcutsChanged(object sender, EventArgs args) => CatalogChanged(sender, null);
        private OperationResult ReconcileDisk()
        {
            Dictionary<string, string> renames;
            lock (_watchLock) { renames = new Dictionary<string, string>(_renames, StringComparer.OrdinalIgnoreCase); _renames.Clear(); _diskChanged = false;
                if (_watchNeedsRestart) { _watchSignature = null; _watchNeedsRestart = false; } }
            var result = _session.Reconcile(renames);
            // Retain known rename bindings if a concurrent operation prevents reconciliation.
            if (!result.Success) lock (_watchLock) { foreach (var pair in renames) if (!_renames.ContainsKey(pair.Key)) _renames[pair.Key] = pair.Value; }
            return result;
        }
        private void WatchOtherProfiles()
        {
            var profiles = _session.Snapshot.Profiles.Where(p => !p.Suppressed && p.Name != _session.Snapshot.Settings.ActiveProfileName).ToArray();
            string signature = string.Join("|", profiles.Select(p => p.Id + ":" + p.RelativeRoot + ":" + Directory.Exists(Path.Combine(_settings.DataRoot, p.RelativeRoot))));
            if (signature == _watchSignature) return;
            foreach (var watcher in _watchers) watcher.Dispose(); _watchers.Clear(); _watchSignature = null;
            foreach (var profile in profiles)
            {
                string root = SafePath.Resolve(_settings.DataRoot, profile.RelativeRoot); if (!Directory.Exists(root)) continue;
                var watcher = new FileSystemWatcher(root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
                FileSystemEventHandler changed = (_, _) => { lock (_watchLock) _diskChanged = true; };
                watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
                watcher.Renamed += (_, args) => { lock (_watchLock) { _diskChanged = true;
                    string from = Path.GetRelativePath(_settings.DataRoot, args.OldFullPath), to = Path.GetRelativePath(_settings.DataRoot, args.FullPath);
                    foreach (string key in _renames.Where(pair => Same(pair.Value, from)).Select(pair => pair.Key).ToArray()) _renames[key] = to;
                    _renames[from] = to; } };
                watcher.Error += (_, _) => { lock (_watchLock) { _diskChanged = true; _watchNeedsRestart = true; } };
                _watchers.Add(watcher); watcher.EnableRaisingEvents = true;
            }
            _watchSignature = signature;
        }
        private void RefreshView()
        {
            if (_rendering) return;
            try
            {
                var result = _session.Refresh();
                if (_session.Snapshot == null) { _status.Text = T("MigrationRequired"); foreach (var item in _mutators) item.Enabled = false; return; }
                _rendering = true;
                var expanded = new HashSet<string>(); var known = new HashSet<string>();
                void ReadExpanded(TreeNodeCollection nodes) { foreach (TreeNode node in nodes) { known.Add(node.Name); if (node.IsExpanded) expanded.Add(node.Name); ReadExpanded(node.Nodes); } }
                ReadExpanded(_tree.Nodes); _tree.BeginUpdate(); _tree.Nodes.Clear();
                BuildLocations(_tree, _session.Snapshot, T("Root"));
                void Restore(TreeNodeCollection nodes) { foreach (TreeNode node in nodes) { if (expanded.Contains(node.Name)) node.Expand(); else if (known.Contains(node.Name)) node.Collapse();
                    if (node.Tag is TreeLocation location && location.Profile == _session.ProfileId && Same(location.Path, _session.FolderPath)) _tree.SelectedNode = node; Restore(node.Nodes); } }
                Restore(_tree.Nodes);
                _rendering = false; RenderRows(); foreach (var item in _mutators) item.Enabled = !_session.ReadOnly;
                WatchOtherProfiles();
                if (!result.Success) _status.Text = new StatePersistenceException(result).Message;
            }
            catch (Exception ex) { _status.Text = ex.Message; }
            finally { _tree.EndUpdate(); _rendering = false; }
        }
        private static void BuildLocations(TreeView tree, LauncherState state, string rootLabel)
        {
            foreach (var profile in state.Profiles.Where(profile => !profile.Suppressed))
            {
                var root = new TreeNode(profile.Name + " / " + rootLabel) { Name = profile.Id.ToString(), Tag = new TreeLocation { Profile = profile.Id } };
                tree.Nodes.Add(root);
                var nodes = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase) { [string.Empty] = root };
                foreach (var folder in state.Folders.Where(folder => folder.ProfileId == profile.Id && !folder.Suppressed).OrderBy(folder => folder.RelativePath.Length).ThenBy(folder => folder.RelativePath, StringComparer.OrdinalIgnoreCase))
                {
                    if (!nodes.TryGetValue(Path.GetDirectoryName(folder.RelativePath) ?? string.Empty, out var parent)) continue;
                    var node = new TreeNode(Path.GetFileName(folder.RelativePath)) { Name = folder.Id.ToString(),
                        Tag = new TreeLocation { Profile = profile.Id, Folder = folder.Id, Path = folder.RelativePath } };
                    parent.Nodes.Add(node); nodes[folder.RelativePath] = node;
                }
                root.Expand();
            }
        }
        private void RenderRows()
        {
            if (_rendering || _session.Snapshot == null) return;
            var selected = SelectedRows().Select(row => row.Id).ToHashSet();
            Guid? focused = (_list.FocusedItem?.Tag as OrganizerRow)?.Id;
            Guid? top = (_list.TopItem?.Tag as OrganizerRow)?.Id;
            try
            {
                _rendering = true; _list.BeginUpdate(); _list.Items.Clear();
                foreach (var row in _session.Rows())
                {
                    var item = new ListViewItem(new[] { (row.IsPinned ? "★ " : string.Empty) + row.Name,
                        row.IsFolder ? T("Folder") : L.Relocalize(row.Kind.ToString()), row.IsFolder ? string.Empty : T("State" + row.Status), Path.GetDirectoryName(row.RelativePath) ?? string.Empty }) { Tag = row };
                    _list.Items.Add(item); item.Selected = selected.Contains(row.Id); if (focused == row.Id) item.Focused = true;
                }
                if (top.HasValue) { var old = _list.Items.Cast<ListViewItem>().FirstOrDefault(item => ((OrganizerRow)item.Tag).Id == top.Value); if (old != null) _list.TopItem = old; }
                string profile = _session.Snapshot.Profiles.Single(profile => profile.Id == _session.ProfileId).Name;
                _location.Text = string.Format(T("Location"), profile, _session.FolderPath, _session.Snapshot.Settings.ActiveProfileName);
                _status.Text = _list.Items.Count == 0 ? T("Empty") : T("KeyboardHint");
            }
            finally { _list.EndUpdate(); _rendering = false; }
            UpdateAliasEditor();
        }
        private OrganizerRow[] SelectedRows() => _list.SelectedItems.Cast<ListViewItem>().Select(item => (OrganizerRow)item.Tag).ToArray();
        private Guid[] SelectedIds()
        {
            var ids = SelectedRows().Select(row => row.Id).ToArray();
            if (_treeSelection) return _tree.SelectedNode?.Tag is TreeLocation location && location.Folder != Guid.Empty ? new[] { location.Folder } : Array.Empty<Guid>();
            return ids;
        }
        private void UpdateAliasEditor()
        {
            if (_session.Draft?.Dirty != true)
            {
                var rows = SelectedRows();
                if (rows.Length == 1 && !rows[0].IsFolder) _session.EditAliases(rows[0].Id); else _session.DiscardAliases();
            }
            _rendering = true;
            _aliases.Enabled = _session.Draft != null;
            if (_session.Draft != null)
            {
                var entry = _session.Snapshot.Shortcuts.FirstOrDefault(entry => entry.Id == _session.Draft.Id);
                _aliasTitle.Text = T("Aliases") + " — " + (entry?.RelativePath ?? _session.Draft.Id.ToString());
                if (_aliases.Text != _session.Draft.Text) _aliases.Text = _session.Draft.Text;
            }
            else { _aliasTitle.Text = T("Aliases"); _aliases.Text = string.Empty; }
            _rendering = false; UpdateAliasButtons();
        }
        private void UpdateAliasButtons() => _saveAliases.Enabled = !_session.ReadOnly && _session.Draft?.Dirty == true;
        private void Guard(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); } }
        private void Run(Func<OperationResult> action)
        {
            _working = true;
            try { var result = action(); if (!result.Success) MessageBox.Show(this, new StatePersistenceException(result).Message + "\n" + result.Code, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { _working = false; RefreshView(); }
        }
        private void Confirm(OrganizerPreview preview)
        {
            if (!preview.Result.Success) { MessageBox.Show(this, T("PreviewFailed") + "\n" + preview.Result.Code, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string action = preview.Request.Kind switch { ShortcutOperationKind.CreateFolder => T("NewFolder"), ShortcutOperationKind.Reorder => T("Up") + " / " + T("Down"),
                ShortcutOperationKind.Unpin => T("Pin"), _ => T(preview.Request.Kind.ToString()) };
            string destination = preview.Request.DestinationProfileId == Guid.Empty ? _session.Snapshot.Profiles.Single(p => p.Id == preview.Request.ProfileId).Name
                : _session.Snapshot.Profiles.Single(p => p.Id == preview.Request.DestinationProfileId).Name;
            string detail = action + "\n" + destination + " / " + preview.Request.DestinationFolder + "\n" + preview.Request.Name + "\n" + string.Join(Environment.NewLine, preview.Items);
            if (preview.Request.Kind == ShortcutOperationKind.Remove) detail += "\n\n" + T("SafeRemove");
            if (MessageBox.Show(this, T("Confirm") + "\n\n" + detail, Text, MessageBoxButtons.OKCancel,
                MessageBoxIcon.Question) == DialogResult.OK) Run(() => _session.Commit(preview));
        }
        private void NewFolder() { using var name = new RenameDialog(string.Empty) { Text = T("NewFolder") }; if (name.ShowDialog(this) == DialogResult.OK)
            Confirm(_session.Prepare(ShortcutOperationKind.CreateFolder, Array.Empty<Guid>(), destinationFolder: _session.FolderPath, name: name.NewName)); }
        private void ExistingLinks()
        {
            using var add = new AddItemDialog(_settings, _session.ProfileId, _session.FolderPath); add.ShowDialog(this); RefreshView();
        }
        private void Rename() { var ids = SelectedIds(); if (ids.Length != 1) return; var row = _session.Rows().FirstOrDefault(row => row.Id == ids[0]);
            string current = row?.Name ?? Path.GetFileName(_session.FolderPath); using var name = new RenameDialog(current) { Text = T("Rename") };
            if (name.ShowDialog(this) == DialogResult.OK) Confirm(_session.Prepare(ShortcutOperationKind.Rename, ids, name: name.NewName)); }
        private void Remove() { var ids = SelectedIds(); if (ids.Length != 0) Confirm(_session.Prepare(ShortcutOperationKind.Remove, ids)); }
        private void Pin() { var ids = SelectedIds(); if (ids.Length == 0 || ids.Any(id => !_session.Snapshot.Shortcuts.Any(entry => entry.Id == id))) return;
            bool unpin = ids.All(id => _session.Snapshot.Shortcuts.Single(entry => entry.Id == id).IsPinned); Confirm(_session.Prepare(unpin ? ShortcutOperationKind.Unpin : ShortcutOperationKind.Pin, ids)); }
        private void Reorder(int direction) { var ids = SelectedIds(); if (ids.Length != 0) Confirm(_session.Reorder(ids, direction: direction)); }
        private void Transfer(bool copy)
        {
            var ids = SelectedIds(); if (ids.Length == 0) return;
            using var destination = new DestinationDialog(_session.Snapshot, T("Destination"), T("Root"));
            if (destination.ShowDialog(this) == DialogResult.OK && destination.Selected != null)
                Confirm(_session.Prepare(copy ? ShortcutOperationKind.Copy : ShortcutOperationKind.Move, ids, destination.Selected.Profile, destination.Selected.Path));
        }
        private void Repair()
        {
            var ids = SelectedIds(); if (ids.Length != 1 || !_session.Snapshot.Shortcuts.Any(entry => entry.Id == ids[0])) return;
            var item = _manager.ReadOrganizerShortcut(ids[0], _session.Snapshot);
            if (Path.GetExtension(item.ShortcutFilePath).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return;
            string fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item.ShortcutFilePath)));
            using var dialog = new RepairTargetDialog(item);
            if (dialog.ShowDialog(this) == DialogResult.OK) Run(() => { _manager.RepairTarget(item, dialog.Target, fingerprint); return new OperationResult(); });
        }
        private void BeginDrag(Guid[] ids)
        {
            if (_session.ReadOnly || ids.Length == 0) return;
            var payload = _session.StartDrag(ids); var data = new DataObject(); data.SetData(OrganizerSession.DragFormat, false, "1"); data.SetText(payload.Token, TextDataFormat.UnicodeText);
            try { DoDragDrop(data, DragDropEffects.Move | DragDropEffects.Copy); } finally { _session.EndDrag(payload.Token); }
        }
        private OrganizerDragSelection InternalDrag(System.Windows.Forms.IDataObject data) => data?.GetDataPresent(OrganizerSession.DragFormat, false) == true
            ? _session.FindDrag(data.GetData(DataFormats.UnicodeText, false) as string) : null;
        private void OnItemDragOver(object sender, DragEventArgs args)
        {
            args.Effect = DragDropEffects.None; if (_session.ReadOnly || args.Data == null) return;
            if (args.Data.GetDataPresent(OrganizerSession.DragFormat, false))
            { if (InternalDrag(args.Data) != null) args.Effect = (args.KeyState & 8) != 0 ? DragDropEffects.Copy : DragDropEffects.Move; }
            else if (args.Data.GetDataPresent(DataFormats.FileDrop, false)) args.Effect = DragDropEffects.Copy;
        }
        private void Drop(object sender, DragEventArgs args)
        {
            if (_session.ReadOnly || args.Data == null) return;
            Guard(() =>
            {
                var destination = sender == _tree ? _tree.GetNodeAt(_tree.PointToClient(new Point(args.X, args.Y)))?.Tag as TreeLocation
                    : new TreeLocation { Profile = _session.ProfileId, Path = _session.FolderPath };
                if (destination == null) return;
                var targetRow = sender == _list ? _list.GetItemAt(_list.PointToClient(new Point(args.X, args.Y)).X, _list.PointToClient(new Point(args.X, args.Y)).Y)?.Tag as OrganizerRow : null;
                if (targetRow?.IsFolder == true) destination = new TreeLocation { Profile = targetRow.ProfileId, Path = targetRow.RelativePath };
                if (args.Data.GetDataPresent(OrganizerSession.DragFormat, false))
                {
                    var payload = InternalDrag(args.Data); if (payload == null) { _status.Text = T("StaleDrag"); return; }
                    bool copy = (args.KeyState & 8) != 0;
                    if (!copy && sender == _list && targetRow?.IsFolder != true && payload.ProfileId == _session.ProfileId &&
                        payload.Ids.All(id => _session.Snapshot.Shortcuts.Any(entry => entry.Id == id && Same(Path.GetDirectoryName(entry.RelativePath), _session.FolderPath))))
                        Confirm(_session.Reorder(payload.Ids, targetRow?.Id));
                    else Confirm(_session.Prepare(copy ? ShortcutOperationKind.Copy : ShortcutOperationKind.Move, payload.Ids,
                        destination.Profile, destination.Path, sourceProfile: payload.ProfileId));
                }
                else if (args.Data.GetData(DataFormats.FileDrop, false) is string[] files)
                {
                    using var add = new AddItemDialog(_settings, destination.Profile, destination.Path, files.Select(path => new AddInput {
                        Kind = new[] { ".lnk", ".url" }.Contains(Path.GetExtension(path).ToLowerInvariant()) ? AddInputKind.ExistingShortcut : AddInputKind.Path, Source = path }));
                    add.ShowDialog(this); RefreshView();
                }
            });
        }
        private void OnKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Control && args.KeyCode == Keys.F) { _filter.Focus(); args.SuppressKeyPress = true; return; }
            if (_aliases.ContainsFocus) { if (args.Control && args.KeyCode == Keys.Enter) { Run(() => _session.SaveAliases()); args.SuppressKeyPress = true; } return; }
            if (!_list.ContainsFocus && !_tree.ContainsFocus) return;
            Action command = args.KeyData switch { Keys.F2 => Rename, Keys.Delete => Remove, Keys.Control | Keys.O => ExistingLinks, Keys.Control | Keys.M => () => Transfer(false),
                Keys.Control | Keys.Shift | Keys.C => () => Transfer(true), Keys.Control | Keys.N => NewFolder, Keys.Control | Keys.P => Pin,
                Keys.Alt | Keys.Up => () => Reorder(-1), Keys.Alt | Keys.Down => () => Reorder(1), _ => null };
            if (command != null) { args.SuppressKeyPress = true; if (!_session.ReadOnly) Guard(command); }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { _refreshTimer.Dispose(); foreach (var watcher in _watchers) watcher.Dispose(); _settings.Operations.CatalogChanged -= CatalogChanged; _manager.ShortcutsChanged -= ShortcutsChanged; _list.ContextMenuStrip?.Dispose(); _font.Dispose(); } base.Dispose(disposing); }
        private static bool Same(string left, string right) => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        private sealed class DestinationDialog : ThemedForm
        {
            private readonly TreeView _locations = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            internal TreeLocation Selected => _locations.SelectedNode?.Tag as TreeLocation;
            internal DestinationDialog(LauncherState state, string title, string rootLabel)
            {
                Text = title; Size = new Size(460, 500); StartPosition = FormStartPosition.CenterParent;
                BuildLocations(_locations, state, rootLabel); var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
                var ok = new Button { Text = L.Text("OK"), DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = L.Text("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
                buttons.Controls.AddRange(new Control[] { ok, cancel }); Controls.Add(_locations); Controls.Add(buttons); AcceptButton = ok; CancelButton = cancel;
            }
        }
    }
}
