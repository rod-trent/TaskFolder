using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TaskFolder.Views
{
    /// <summary>
    /// Dialog for adding a new note. Lets the user pick a title and an optional
    /// Notes subfolder (existing ones shown in a drop-down; new names typed in).
    /// </summary>
    public sealed class AddNoteDialog : Form
    {
        private readonly TextBox _txtName;
        private readonly ComboBox _cboFolder;

        /// <summary>The title entered by the user.</summary>
        public string NoteName => _txtName.Text.Trim();

        /// <summary>
        /// The subfolder name, or null / empty if the note belongs in the Notes root.
        /// </summary>
        public string NoteSubfolder
        {
            get
            {
                string v = _cboFolder.Text.Trim();
                return string.IsNullOrEmpty(v) ? null : v;
            }
        }

        public AddNoteDialog(IEnumerable<string> existingSubfolders)
        {
            Text            = "Add Note";
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Size            = new Size(340, 215);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            ShowInTaskbar   = false;

            int y = 16;

            Controls.Add(new Label { Text = "Note title:", Location = new Point(14, y + 2), AutoSize = true });
            _txtName = new TextBox { Location = new Point(90, y), Width = 220 };
            Controls.Add(_txtName);
            y += 32;

            Controls.Add(new Label { Text = "Folder:", Location = new Point(14, y + 2), AutoSize = true });
            _cboFolder = new ComboBox
            {
                Location      = new Point(90, y),
                Width         = 220,
                DropDownStyle = ComboBoxStyle.DropDown   // allows typing a new folder name
            };
            _cboFolder.Items.Add(string.Empty);          // "(none)" — notes root
            foreach (string s in existingSubfolders)
                _cboFolder.Items.Add(s);
            _cboFolder.SelectedIndex = 0;
            Controls.Add(_cboFolder);
            y += 14;

            Controls.Add(new Label
            {
                Text      = "Leave blank to save in the Notes root, or type a folder name.",
                Location  = new Point(90, y + 20),
                Size      = new Size(220, 30),
                ForeColor = Color.Gray,
                Font      = new Font(Font, FontStyle.Italic)
            });
            y += 54;

            var btnOK = new Button
            {
                Text         = "OK",
                DialogResult = DialogResult.OK,
                Location     = new Point(150, y),
                Size         = new Size(75, 26)
            };
            btnOK.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(_txtName.Text))
                {
                    MessageBox.Show("Please enter a note title.", "Add Note",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };
            Controls.Add(btnOK);

            var btnCancel = new Button
            {
                Text         = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location     = new Point(235, y),
                Size         = new Size(75, 26)
            };
            Controls.Add(btnCancel);

            AcceptButton = btnOK;
            CancelButton = btnCancel;

            ActiveControl = _txtName;
        }
    }
}
