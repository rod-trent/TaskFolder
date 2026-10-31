using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskFolder.Views
{
    /// <summary>
    /// A lightweight always-on-top sticky-note editor.
    /// Auto-saves on close. Opened when a .note shortcut is launched.
    /// </summary>
    public class StickyNoteForm : Form
    {
        private readonly string _filePath;
        private readonly TextBox _txtNote;
        private readonly ToolStrip _toolbar;
        private bool _dirty;

        public StickyNoteForm(string filePath, string title, string content)
        {
            _filePath = filePath;

            Text = title;
            Size = new Size(340, 260);
            MinimumSize = new Size(220, 160);
            BackColor = Color.FromArgb(255, 255, 204); // classic yellow
            Icon = SystemIcons.Information;
            StartPosition = FormStartPosition.CenterScreen;

            // ── Toolbar ───────────────────────────────────────────────────────────
            _toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = Color.FromArgb(240, 240, 160),
                RenderMode = ToolStripRenderMode.System,
                Padding = new Padding(2, 0, 0, 0)
            };

            var btnPin = new ToolStripButton("Always on Top")
            {
                CheckOnClick = true,
                Checked = true,
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            btnPin.CheckedChanged += (s, e) => TopMost = btnPin.Checked;

            var btnSave = new ToolStripButton("Save")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            btnSave.Click += (s, e) => SaveNote();

            _toolbar.Items.Add(btnPin);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(btnSave);

            // ── Note text area ────────────────────────────────────────────────────
            _txtNote = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                Text = content ?? string.Empty,
                BackColor = Color.FromArgb(255, 255, 204),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10f),
                AcceptsReturn = true,
                AcceptsTab = true,
                WordWrap = true
            };
            _txtNote.TextChanged += (s, e) => { _dirty = true; Text = title + " *"; };

            Controls.Add(_txtNote);
            Controls.Add(_toolbar);

            TopMost = true;
            FormClosing += OnFormClosing;
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_dirty) return;

            var result = MessageBox.Show("Save changes to this note?", Text.TrimEnd('*', ' '),
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (result == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (result == DialogResult.Yes)
                SaveNote();
        }

        private void SaveNote()
        {
            try
            {
                System.IO.File.WriteAllText(_filePath, _txtNote.Text);
                _dirty = false;
                Text = System.IO.Path.GetFileNameWithoutExtension(_filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save note:\n{ex.Message}", "TaskFolder",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
