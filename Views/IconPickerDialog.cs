using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TaskFolder.Utilities;

namespace TaskFolder.Views
{
    /// <summary>
    /// Lets the user pick a custom icon (file + index) for a shortcut.
    /// </summary>
    public class IconPickerDialog : Form
    {
        private TextBox _txtPath;
        private NumericUpDown _numIndex;
        private PictureBox _preview;
        private Button _btnBrowse;
        private Button _btnOK;
        private Button _btnCancel;

        public string SelectedIconPath => _txtPath.Text.Trim();
        public int SelectedIconIndex => (int)_numIndex.Value;

        public IconPickerDialog(string currentPath = "", int currentIndex = 0)
        {
            Text = "Choose Icon";
            Size = new Size(420, 220);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            // Path row
            var lblPath = new Label { Text = "Icon file (.ico, .exe, .dll):", Location = new Point(12, 14), AutoSize = true };
            _txtPath = new TextBox { Location = new Point(12, 32), Width = 280, Text = currentPath };
            _txtPath.TextChanged += (s, e) => UpdatePreview();

            _btnBrowse = new Button { Text = "Browse...", Location = new Point(298, 30), Width = 98 };
            _btnBrowse.Click += OnBrowse;

            // Index row
            var lblIndex = new Label { Text = "Icon index:", Location = new Point(12, 68), AutoSize = true };
            _numIndex = new NumericUpDown
            {
                Location = new Point(12, 86),
                Width = 70,
                Minimum = 0,
                Maximum = 999,
                Value = Math.Max(0, currentIndex)
            };
            _numIndex.ValueChanged += (s, e) => UpdatePreview();

            // Preview
            var lblPreview = new Label { Text = "Preview:", Location = new Point(200, 68), AutoSize = true };
            _preview = new PictureBox
            {
                Location = new Point(200, 86),
                Size = new Size(32, 32),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            // Buttons
            _btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(220, 150),
                Width = 80
            };
            _btnOK.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(SelectedIconPath) && !File.Exists(SelectedIconPath))
                {
                    MessageBox.Show("File not found.", "Choose Icon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(310, 150),
                Width = 80
            };

            Controls.AddRange(new Control[]
            {
                lblPath, _txtPath, _btnBrowse,
                lblIndex, _numIndex,
                lblPreview, _preview,
                _btnOK, _btnCancel
            });

            AcceptButton = _btnOK;
            CancelButton = _btnCancel;

            if (!string.IsNullOrEmpty(currentPath))
                UpdatePreview();
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Icon files (*.ico;*.exe;*.dll)|*.ico;*.exe;*.dll|All Files (*.*)|*.*",
                Title = "Select Icon File"
            };
            if (!string.IsNullOrWhiteSpace(_txtPath.Text) && File.Exists(_txtPath.Text))
                dlg.InitialDirectory = Path.GetDirectoryName(_txtPath.Text);

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _txtPath.Text = dlg.FileName;
                _numIndex.Value = 0;
                UpdatePreview();
            }
        }

        private void UpdatePreview()
        {
            try
            {
                string path = _txtPath.Text.Trim();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var icon = IconExtractor.ExtractIcon(path, (int)_numIndex.Value);
                    _preview.Image = icon?.ToBitmap();
                }
                else
                {
                    _preview.Image = null;
                }
            }
            catch
            {
                _preview.Image = null;
            }
        }
    }
}
