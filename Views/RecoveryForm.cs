using L = TaskFolder.Services.LocalizationService;
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

namespace TaskFolder.Views
{
    public sealed class RecoveryForm : ThemedForm
    {
        private readonly SettingsService _settings;
        private readonly ShortcutManager _manager;
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details,
            FullRowSelect = true, HideSelection = false, MultiSelect = true, AccessibleName = L.Text("Recovery operations") };
        private readonly TextBox _details = new TextBox { Dock = DockStyle.Fill, Multiline = true,
            ReadOnly = true, ScrollBars = ScrollBars.Vertical, AccessibleName = L.Text("Operation details") };
        private readonly Label _status = new Label { Dock = DockStyle.Fill, AutoSize = false, AccessibleName = L.Text("Recovery status") };
        private readonly Button _restore = new Button { Text = L.Text("Restore selected"), AutoSize = true };
        private readonly Button _recover = new Button { Text = L.Text("Recover interrupted operations"), AutoSize = true };
        private readonly Button _clear = new Button { Text = L.Text("Clear selected recovery data..."), AutoSize = true };
        private bool _working;

        public RecoveryForm(SettingsService settings, ShortcutManager manager)
        {
            _settings = settings;
            _manager = manager;
            Text = L.Text("TaskFolder — Recovery");
            Font = Windows11MenuStyle.CreateFont(10);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(700, 480);
            Size = new Size(880, 600);
            _list.Columns.Add(L.Text("Operation"), 130);
            _list.Columns.Add(L.Text("Date"), 160);
            _list.Columns.Add(L.Text("State"), 130);
            _list.Columns.Add(L.Text("Recovery bytes"), 120);
            _list.Columns.Add(L.Text("Record bytes"), 110);
            _list.Columns.Add(L.Text("Items"), 240);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            var refresh = new Button { Text = L.Text("Refresh"), AutoSize = true };
            var open = new Button { Text = L.Text("Open recovery folder"), AutoSize = true };
            var close = new Button { Text = L.Text("Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
            commands.Controls.AddRange(new Control[] { _restore, _recover, _clear, refresh, open, close });
            layout.Controls.Add(_list, 0, 0);
            layout.Controls.Add(_details, 0, 1);
            layout.Controls.Add(_status, 0, 2);
            layout.Controls.Add(commands, 0, 3);
            Controls.Add(layout);
            CancelButton = close;
            _list.SelectedIndexChanged += (_, _) => UpdateSelection();
            _restore.Click += (_, _) => RestoreSelected();
            _recover.Click += (_, _) => RecoverPending();
            _clear.Click += (_, _) => ClearSelected();
            refresh.Click += (_, _) => RefreshOperations();
            open.Click += (_, _) =>
            {
                try
                {
                    string path = SafePath.Resolve(_settings.DataRoot, "Recovery");
                    if (!Directory.Exists(path)) { MessageBox.Show(this, L.Text("There is no recovery folder yet."), L.Text("Recovery")); return; }
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, L.Text("Recovery folder could not be opened")); }
            };
            _settings.Operations.CatalogChanged += OnCatalogChanged;
            RefreshOperations();
        }

        private void OnCatalogChanged(object sender, CatalogChangedEventArgs args)
        {
            if (IsDisposed || _working) return;
            if (InvokeRequired) { BeginInvoke(new Action(RefreshOperations)); return; }
            RefreshOperations();
        }

        private void RefreshOperations()
        {
            var selected = _list.SelectedItems.Cast<ListViewItem>().Select(item => ((RecoveryOperationInfo)item.Tag).OperationId).ToHashSet();
            var operations = _settings.Operations.GetRecoveryOperations();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var operation in operations.OrderByDescending(item => item.CreatedUtc))
            {
                var row = new ListViewItem(new[] { L.Relocalize(operation.Kind ?? L.Text("Unreadable")), operation.CreatedUtc.LocalDateTime.ToString("g"),
                    L.Relocalize(operation.Error ?? operation.Phase), operation.PayloadBytes.ToString("N0"), operation.MetadataBytes.ToString("N0"), string.Join(", ", operation.ItemPaths) }) { Tag = operation };
                _list.Items.Add(row);
                row.Selected = selected.Contains(operation.OperationId);
            }
            _list.EndUpdate();
            var pending = _settings.Operations.InspectPending();
            _status.Text = !pending.Success ? L.Text("Changes are blocked until interrupted or unreadable recovery data is resolved. ") + pending.Code
                : operations.Count == 0 ? L.Text("No saved recovery operations. Recovery data is never cleared automatically.")
                : L.Plural("OperationCount_", operations.Count, _settings.Settings.Language) + "; " + L.Text("{0:N0} payload bytes, {1:N0} record bytes. Nothing is removed automatically.", operations.Sum(item => item.PayloadBytes), operations.Sum(item => item.MetadataBytes));
            _recover.Enabled = operations.Any(item => item.Pending) || !pending.Success;
            UpdateSelection();
        }

        private RecoveryOperationInfo[] Selection() => _list.SelectedItems.Cast<ListViewItem>().Select(item => (RecoveryOperationInfo)item.Tag).ToArray();

        private void UpdateSelection()
        {
            var selected = Selection();
            _restore.Enabled = !_working && !_settings.StateLoad.ReadOnly && selected.Length == 1 && selected[0].CanRestore;
            _clear.Enabled = !_working && selected.Length != 0 && selected.All(item => !item.Pending && item.Error == null);
            _details.Text = string.Join(Environment.NewLine + Environment.NewLine, selected.Select(item =>
                $"{item.OperationId}\r\n{item.Error ?? item.Phase}\r\n" + string.Join(Environment.NewLine, item.ItemPaths)));
        }

        private void RestoreSelected()
        {
            var operation = Selection().SingleOrDefault();
            if (operation == null || !operation.CanRestore) return;
            var request = _settings.NewOperation(ShortcutOperationKind.Restore);
            request.RestoreOperationId = operation.OperationId;
            foreach (var pair in operation.OriginalItemPaths)
            {
                string path = SafePath.Resolve(_settings.DataRoot, pair.Value);
                if (!File.Exists(path)) continue;
                using var rename = new RenameDialog(Path.GetFileNameWithoutExtension(path) + " restored");
                rename.Text = L.Text("Original path is occupied — choose another name, or Cancel to skip this restore");
                if (rename.ShowDialog(this) != DialogResult.OK) return;
                request.ReplacementNames[pair.Key] = rename.NewName + Path.GetExtension(path);
            }
            if (MessageBox.Show(this, L.Text("Restore this operation's files and metadata?\n") + string.Join(Environment.NewLine, operation.ItemPaths),
                L.Text("Restore recovery operation"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Run(() => _settings.ExecuteOperation(request));
        }

        private void RecoverPending()
        {
            var pending = _settings.Operations.GetRecoveryOperations().Where(item => item.Pending || item.Error != null).ToArray();
            if (MessageBox.Show(this, L.Text("Recover the interrupted operations below? Uncommitted file changes will be rolled back; committed changes will be completed.\n\n") +
                string.Join(Environment.NewLine, pending.Select(item => item.OperationId + " — " + string.Join(", ", item.ItemPaths))),
                L.Text("Recover interrupted operations"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Run(() => _settings.Operations.RecoverPending());
        }

        private void ClearSelected()
        {
            var selected = Selection();
            if (selected.Length == 0 || selected.Any(item => item.Pending || item.Error != null)) return;
            if (MessageBox.Show(this, L.Text("Permanently clear recovery data for {0} ({1:N0} payload bytes and {2:N0} record bytes)?\n", L.Plural("OperationCount_", selected.Length, _settings.Settings.Language), selected.Sum(item => item.PayloadBytes), selected.Sum(item => item.MetadataBytes)) +
                L.Text("These operations will no longer be restorable. Small operation receipts will remain to prevent accidental replay.\n\n") +
                string.Join(Environment.NewLine, selected.Select(item => item.OperationId + " — " + string.Join(", ", item.ItemPaths))),
                L.Text("Permanently clear selected recovery payloads"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Run(() => _settings.Operations.PurgeRecovery(selected.Select(item => item.OperationId).ToArray()));
        }

        private void Run(Func<OperationResult> action)
        {
            _working = true;
            try
            {
                var result = action();
                if (!result.Success)
                    MessageBox.Show(this, new StatePersistenceException(result).Message + "\n" + result.Code,
                        L.Text("Recovery could not be completed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                {
                    _settings.Load();
                    _manager.ReloadShortcuts();
                    _status.Text = L.Text("Operation completed.");
                }
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, L.Text("Recovery"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { _working = false; RefreshOperations(); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _settings.Operations.CatalogChanged -= OnCatalogChanged;
            base.Dispose(disposing);
        }
    }
}
