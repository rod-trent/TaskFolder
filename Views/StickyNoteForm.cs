using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TaskFolder.Views
{
    /// <summary>
    /// A lightweight always-on-top sticky-note editor with basic rich-text formatting.
    /// Content is saved as RTF; plain-text files from older versions are loaded gracefully.
    /// Auto-saves on close.
    /// </summary>
    public class StickyNoteForm : Form
    {
        private readonly string _filePath;
        private readonly string _title;
        private readonly RichTextBox _rtfNote;
        private readonly ToolStrip _toolbar;
        private bool _dirty;

        public StickyNoteForm(string filePath, string title, string content)
        {
            _filePath = filePath;
            _title    = title;

            Text            = title;
            Size            = new Size(340, 300);
            MinimumSize     = new Size(220, 180);
            BackColor       = Color.FromArgb(255, 255, 204); // classic yellow
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = SystemIcons.Information; }
            StartPosition   = FormStartPosition.CenterScreen;

            // ── Toolbar ───────────────────────────────────────────────────────────
            _toolbar = new ToolStrip
            {
                GripStyle   = ToolStripGripStyle.Hidden,
                BackColor   = Color.FromArgb(240, 240, 160),
                RenderMode  = ToolStripRenderMode.System,
                Padding     = new Padding(2, 0, 0, 0)
            };

            var btnPin = new ToolStripButton("Always on Top")
            {
                CheckOnClick = true,
                Checked      = true,
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            btnPin.CheckedChanged += (s, e) => TopMost = btnPin.Checked;

            var btnBold = new ToolStripButton("B")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font         = new Font("Segoe UI", 9f, FontStyle.Bold),
                ToolTipText  = "Bold (Ctrl+B)"
            };
            btnBold.Click += (s, e) => ToggleFormat(FontStyle.Bold);

            var btnItalic = new ToolStripButton("I")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                Font         = new Font("Segoe UI", 9f, FontStyle.Italic),
                ToolTipText  = "Italic (Ctrl+I)"
            };
            btnItalic.Click += (s, e) => ToggleFormat(FontStyle.Italic);

            var btnBullet = new ToolStripButton("•")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText  = "Toggle bullet list"
            };
            btnBullet.Click += (s, e) => ToggleBullet();

            var btnSave = new ToolStripButton("Save")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text
            };
            btnSave.Click += (s, e) => SaveNote();

            _toolbar.Items.Add(btnPin);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(btnBold);
            _toolbar.Items.Add(btnItalic);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(btnBullet);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(btnSave);

            // ── Rich-text area ────────────────────────────────────────────────────
            _rtfNote = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(255, 255, 204),
                BorderStyle = BorderStyle.None,
                Font        = new Font("Segoe UI", 10f),
                AcceptsTab  = true,
                WordWrap    = true,
                ScrollBars  = RichTextBoxScrollBars.Vertical
            };

            // Load existing content — handle both RTF and plain text
            if (!string.IsNullOrEmpty(content))
            {
                try
                {
                    if (content.TrimStart().StartsWith("{\\rtf", StringComparison.Ordinal))
                        _rtfNote.Rtf = content;
                    else
                        _rtfNote.Text = content;
                }
                catch
                {
                    _rtfNote.Text = content;
                }
            }

            _rtfNote.TextChanged += (s, e) => { _dirty = true; Text = _title + " *"; };

            Controls.Add(_rtfNote);
            Controls.Add(_toolbar);

            TopMost      = true;
            FormClosing += OnFormClosing;
        }

        private void ToggleFormat(FontStyle style)
        {
            if (_rtfNote.SelectionLength == 0) return;
            var current = _rtfNote.SelectionFont ?? _rtfNote.Font;
            bool hasStyle = (current.Style & style) != 0;
            _rtfNote.SelectionFont = new Font(current, hasStyle ? current.Style & ~style : current.Style | style);
            _rtfNote.Focus();
        }

        private void ToggleBullet()
        {
            _rtfNote.SelectionBullet = !_rtfNote.SelectionBullet;
            _rtfNote.Focus();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_dirty) return;

            var result = MessageBox.Show("Save changes to this note?", _title,
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
                File.WriteAllText(_filePath, _rtfNote.Rtf);
                _dirty = false;
                Text   = _title;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save note:\n{ex.Message}", "TaskFolder",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
