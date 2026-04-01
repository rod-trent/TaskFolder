using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskFolder.Views
{
    /// <summary>
    /// A simple modal dialog for renaming a shortcut.
    /// </summary>
    public class RenameDialog : Form
    {
        private TextBox _txtName;
        private Button _btnOK;
        private Button _btnCancel;

        /// <summary>The new name entered by the user (without extension).</summary>
        public string NewName => _txtName.Text.Trim();

        public RenameDialog(string currentName)
        {
            Text = "Rename Shortcut";
            Size = new Size(340, 130);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var lbl = new Label
            {
                Text = "New name:",
                Location = new Point(12, 14),
                AutoSize = true
            };

            _txtName = new TextBox
            {
                Text = currentName,
                Location = new Point(12, 32),
                Width = 298,
                SelectionStart = 0,
                SelectionLength = currentName.Length
            };

            _btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(154, 62),
                Width = 75
            };
            _btnOK.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(NewName))
                {
                    MessageBox.Show("Name cannot be empty.", "Rename", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(235, 62),
                Width = 75
            };

            Controls.AddRange(new Control[] { lbl, _txtName, _btnOK, _btnCancel });
            AcceptButton = _btnOK;
            CancelButton = _btnCancel;
        }
    }
}
