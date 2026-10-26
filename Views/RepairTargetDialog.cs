using L = TaskFolder.Services.LocalizationService;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Views
{
    internal sealed class RepairTargetDialog : ThemedForm
    {
        private readonly TextBox _target = new TextBox { Dock = DockStyle.Fill, AccessibleName = L.Text("Replacement target") };
        private readonly bool _url;
        internal string Target => _target.Text.Trim();

        internal RepairTargetDialog(ShortcutItem item)
        {
            _url = Path.GetExtension(item.ShortcutFilePath).Equals(".url", StringComparison.OrdinalIgnoreCase);
            Text = L.Text("Repair target — ") + item.GetDisplayName();
            Font = Windows11MenuStyle.CreateFont(10);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(540, 330);
            Size = new Size(660, 370);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { AutoSize = true, Text = _url ? L.Text("New HTTP/HTTPS URL:") : L.Text("New file or folder target:") }, 0, 0);
            _target.Text = item.TargetPath ?? string.Empty;
            layout.Controls.Add(_target, 0, 1);
            var saved = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = L.Text("Saved arguments (retained):\r\n") + (item.Arguments ?? string.Empty) +
                    L.Text("\r\n\r\nSaved working directory (retained):\r\n") + (item.WorkingDirectory ?? string.Empty),
                AccessibleName = L.Text("Saved launch fields") };
            layout.Controls.Add(saved, 0, 2);
            var browse = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
            var file = new Button { Text = L.Text("Choose file..."), AutoSize = true, Visible = !_url };
            var folder = new Button { Text = L.Text("Choose folder..."), AutoSize = true, Visible = !_url };
            file.Click += (_, _) => { using var dialog = new OpenFileDialog { Title = L.Text("Choose replacement target"), CheckFileExists = true };
                if (dialog.ShowDialog(this) == DialogResult.OK) _target.Text = dialog.FileName; };
            folder.Click += (_, _) => { using var dialog = new FolderBrowserDialog { Description = L.Text("Choose replacement target folder") };
                if (dialog.ShowDialog(this) == DialogResult.OK) _target.Text = dialog.SelectedPath; };
            browse.Controls.AddRange(new Control[] { file, folder });
            layout.Controls.Add(browse, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = L.Text("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
            var apply = new Button { Text = L.Text("Repair"), AutoSize = true };
            apply.Click += (_, _) =>
            {
                bool valid = _url ? Uri.TryCreate(Target, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http")
                    : Path.IsPathFullyQualified(Target) && (File.Exists(Target) || Directory.Exists(Target));
                if (!valid) { MessageBox.Show(this, L.Text("Choose an existing file/folder or a valid HTTP/HTTPS URL."), L.Text("Invalid target")); return; }
                DialogResult = DialogResult.OK;
                Close();
            };
            buttons.Controls.AddRange(new Control[] { cancel, apply });
            layout.Controls.Add(buttons, 0, 4);
            Controls.Add(layout);
            CancelButton = cancel;
            AcceptButton = apply;
        }
    }
}
