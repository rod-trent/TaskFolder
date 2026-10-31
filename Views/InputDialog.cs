using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskFolder.Views
{
    /// <summary>
    /// Generic single-line text input dialog, reused for note creation,
    /// pre-launch script entry, and similar prompts.
    /// </summary>
    public class InputDialog : Form
    {
        private readonly TextBox _txt;
        private readonly Button _btnOK;
        private readonly Button _btnCancel;

        /// <summary>The value the user typed (trimmed).</summary>
        public string Value => _txt.Text.Trim();

        /// <param name="title">Form title bar text.</param>
        /// <param name="prompt">Label shown above the input.</param>
        /// <param name="initialValue">Pre-filled value.</param>
        /// <param name="allowEmpty">When false, OK is rejected if the field is blank.</param>
        public InputDialog(string title, string prompt, string initialValue = "", bool allowEmpty = false)
        {
            Text = title;
            Size = new Size(400, 145);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var lbl = new Label
            {
                Text = prompt,
                Location = new Point(12, 14),
                AutoSize = true
            };

            _txt = new TextBox
            {
                Text = initialValue ?? string.Empty,
                Location = new Point(12, 34),
                Width = 360,
                SelectionStart = 0,
                SelectionLength = (initialValue ?? string.Empty).Length
            };

            _btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(216, 72),
                Width = 75
            };
            _btnOK.Click += (s, e) =>
            {
                if (!allowEmpty && string.IsNullOrWhiteSpace(Value))
                {
                    MessageBox.Show("Value cannot be empty.", title,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(297, 72),
                Width = 75
            };

            Controls.AddRange(new Control[] { lbl, _txt, _btnOK, _btnCancel });
            AcceptButton = _btnOK;
            CancelButton = _btnCancel;
        }
    }
}
