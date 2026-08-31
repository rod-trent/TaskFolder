using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using TaskFolder.Services;
using TaskFolder.Utilities;

namespace TaskFolder.Views
{
    /// <summary>
    /// Settings dialog for TaskFolder.
    /// </summary>
    public partial class SettingsForm : Form
    {
        private readonly ShortcutManager _shortcutManager;
        private readonly SettingsService _settingsService;
        private readonly HotkeyService _hotkeyService;
        private Font _uiFont;

        // Controls
        private CheckBox _chkAutoStart;
        private CheckBox _chkShowNotifications;

        private CheckBox _chkHotkeyEnabled;
        private ComboBox _cboModifiers;
        private ComboBox _cboKey;

        private NumericUpDown _numMenuItemHeight;
        private NumericUpDown _numMenuFontSize;
        private PictureBox _picMenuLogo;
        private Label _lblMenuLogoFile;
        private Button _btnChooseMenuLogo;
        private Button _btnRemoveMenuLogo;
        private string _selectedMenuLogoPath = string.Empty;

        private Button _btnOpenFolder;
        private Button _btnClearShortcuts;

        private Button _btnOK;
        private Button _btnCancel;

        public SettingsForm(ShortcutManager shortcutManager,
                            SettingsService settingsService,
                            HotkeyService hotkeyService)
        {
            _shortcutManager = shortcutManager;
            _settingsService = settingsService;
            _hotkeyService = hotkeyService;
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            Text = "TaskFolder Settings";
            Size = new Size(480, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            _uiFont = Windows11MenuStyle.CreateFont(9.5f);
            Font = _uiFont;
            BackColor = Color.FromArgb(249, 249, 249);

            int y = 12;
            const int W = 440;
            const int X = 15;

            // ── Startup ──────────────────────────────────────────────────────────
            var grpStartup = new GroupBox { Text = "Startup", Location = new Point(X, y), Size = new Size(W, 60) };

            _chkAutoStart = new CheckBox
            {
                Text = "Start TaskFolder when Windows starts",
                Location = new Point(12, 20),
                Size = new Size(W - 24, 20)
            };
            grpStartup.Controls.Add(_chkAutoStart);
            Controls.Add(grpStartup);
            y += grpStartup.Height + 8;

            // ── Notifications ────────────────────────────────────────────────────
            var grpNotif = new GroupBox { Text = "Notifications", Location = new Point(X, y), Size = new Size(W, 60) };

            _chkShowNotifications = new CheckBox
            {
                Text = "Show a balloon tip when an application is launched",
                Location = new Point(12, 20),
                Size = new Size(W - 24, 20)
            };
            grpNotif.Controls.Add(_chkShowNotifications);
            Controls.Add(grpNotif);
            y += grpNotif.Height + 8;

            // ── Global Hotkey ────────────────────────────────────────────────────
            var grpHotkey = new GroupBox { Text = "Global Hotkey (pops the tray menu)", Location = new Point(X, y), Size = new Size(W, 90) };

            _chkHotkeyEnabled = new CheckBox
            {
                Text = "Enable global hotkey",
                Location = new Point(12, 20),
                Size = new Size(200, 20)
            };
            grpHotkey.Controls.Add(_chkHotkeyEnabled);

            var lblMod = new Label { Text = "Modifiers:", Location = new Point(12, 48), AutoSize = true };
            grpHotkey.Controls.Add(lblMod);

            _cboModifiers = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(80, 45),
                Width = 130
            };
            _cboModifiers.Items.AddRange(new object[]
            {
                "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift", "Win"
            });
            grpHotkey.Controls.Add(_cboModifiers);

            var lblKey = new Label { Text = "Key:", Location = new Point(224, 48), AutoSize = true };
            grpHotkey.Controls.Add(lblKey);

            _cboKey = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(255, 45),
                Width = 80
            };
            // Populate with A-Z and F1-F12
            for (char c = 'A'; c <= 'Z'; c++) _cboKey.Items.Add(c.ToString());
            for (int i = 1; i <= 12; i++) _cboKey.Items.Add($"F{i}");
            grpHotkey.Controls.Add(_cboKey);

            Controls.Add(grpHotkey);
            y += grpHotkey.Height + 8;

            // ── Menu appearance ──────────────────────────────────────────────────
            var grpAppearance = new GroupBox { Text = "Menu Appearance", Location = new Point(X, y), Size = new Size(W, 148) };

            grpAppearance.Controls.Add(new Label
            {
                Text = "Header logo:",
                Location = new Point(12, 28),
                AutoSize = true
            });

            _picMenuLogo = new PictureBox
            {
                Location = new Point(112, 18),
                Size = new Size(40, 40),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.CenterImage
            };
            grpAppearance.Controls.Add(_picMenuLogo);

            _lblMenuLogoFile = new Label
            {
                Location = new Point(160, 27),
                Size = new Size(100, 22),
                AutoEllipsis = true,
                Text = "No logo selected"
            };
            grpAppearance.Controls.Add(_lblMenuLogoFile);

            _btnChooseMenuLogo = new Button
            {
                Text = "Choose...",
                Location = new Point(260, 24),
                Size = new Size(70, 28)
            };
            _btnChooseMenuLogo.Click += BtnChooseMenuLogo_Click;
            grpAppearance.Controls.Add(_btnChooseMenuLogo);

            grpAppearance.Controls.Add(new Label
            {
                Text = "Item height:",
                Location = new Point(12, 77),
                AutoSize = true
            });

            _numMenuItemHeight = new NumericUpDown
            {
                Location = new Point(112, 73),
                Size = new Size(64, 23),
                Minimum = 32,
                Maximum = 72,
                Increment = 2
            };
            grpAppearance.Controls.Add(_numMenuItemHeight);
            grpAppearance.Controls.Add(new Label
            {
                Text = "px (larger values are easier to click)",
                Location = new Point(182, 77),
                AutoSize = true
            });

            grpAppearance.Controls.Add(new Label
            {
                Text = "Text size:",
                Location = new Point(12, 113),
                AutoSize = true
            });

            _numMenuFontSize = new NumericUpDown
            {
                Location = new Point(112, 109),
                Size = new Size(64, 23),
                Minimum = 9,
                Maximum = 18,
                DecimalPlaces = 1,
                Increment = 0.5m
            };
            grpAppearance.Controls.Add(_numMenuFontSize);
            grpAppearance.Controls.Add(new Label
            {
                Text = "pt (applies to menu, search, and submenus)",
                Location = new Point(182, 113),
                AutoSize = true
            });

            _btnRemoveMenuLogo = new Button
            {
                Text = "Remove",
                Location = new Point(338, 24),
                Size = new Size(70, 28)
            };
            _btnRemoveMenuLogo.Click += BtnRemoveMenuLogo_Click;
            grpAppearance.Controls.Add(_btnRemoveMenuLogo);

            Controls.Add(grpAppearance);
            y += grpAppearance.Height + 8;

            // ── Shortcuts Management ─────────────────────────────────────────────
            var grpShortcuts = new GroupBox { Text = "Shortcuts Management", Location = new Point(X, y), Size = new Size(W, 100) };

            _btnOpenFolder = new Button
            {
                Text = "Open Shortcuts Folder",
                Location = new Point(12, 25),
                Size = new Size(180, 28)
            };
            _btnOpenFolder.Click += BtnOpenFolder_Click;
            grpShortcuts.Controls.Add(_btnOpenFolder);

            _btnClearShortcuts = new Button
            {
                Text = "Remove All Shortcuts",
                Location = new Point(210, 25),
                Size = new Size(180, 28)
            };
            _btnClearShortcuts.Click += BtnClearShortcuts_Click;
            grpShortcuts.Controls.Add(_btnClearShortcuts);

            var lblFolder = new Label
            {
                Text = $"Folder: {_shortcutManager.ShortcutsFolder}",
                Location = new Point(12, 65),
                Size = new Size(W - 24, 22),
                AutoEllipsis = true
            };
            grpShortcuts.Controls.Add(lblFolder);
            Controls.Add(grpShortcuts);
            y += grpShortcuts.Height + 8;

            // ── About ────────────────────────────────────────────────────────────
            var grpAbout = new GroupBox { Text = "About", Location = new Point(X, y), Size = new Size(W, 65) };
            grpAbout.Controls.Add(new Label
            {
                Text = "TaskFolder v1.2.0 — Windows 11 Application Launcher — MIT License",
                Location = new Point(12, 22),
                Size = new Size(W - 24, 32),
                TextAlign = ContentAlignment.MiddleCenter
            });
            Controls.Add(grpAbout);
            y += grpAbout.Height + 12;

            // ── Buttons ──────────────────────────────────────────────────────────
            _btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Location = new Point(W - 165, y),
                Size = new Size(80, 28)
            };
            _btnOK.Click += BtnOK_Click;

            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(W - 75, y),
                Size = new Size(80, 28)
            };

            Controls.Add(_btnOK);
            Controls.Add(_btnCancel);

            AcceptButton = _btnOK;
            CancelButton = _btnCancel;

            // Resize form to fit content
            ClientSize = new Size(W + 30, y + _btnOK.Height + 16);
        }

        private void LoadSettings()
        {
            _chkAutoStart.Checked = IsAutoStartEnabled();
            _chkShowNotifications.Checked = _settingsService.Settings.ShowNotifications;
            _chkHotkeyEnabled.Checked = _settingsService.Settings.HotkeyEnabled;
            _numMenuItemHeight.Value = Math.Clamp(_settingsService.Settings.MenuItemHeight, 32, 72);
            _numMenuFontSize.Value = (decimal)Math.Clamp(_settingsService.Settings.MenuFontSize, 9f, 18f);
            _selectedMenuLogoPath = _settingsService.Settings.MenuHeaderLogoPath ?? string.Empty;
            UpdateMenuLogoPreview();

            // Modifiers
            string mod = _settingsService.Settings.HotkeyModifiers ?? "Ctrl+Alt";
            int modIdx = _cboModifiers.Items.IndexOf(mod);
            _cboModifiers.SelectedIndex = modIdx >= 0 ? modIdx : 0;

            // Key
            string key = _settingsService.Settings.HotkeyKey ?? "T";
            int keyIdx = _cboKey.Items.IndexOf(key);
            _cboKey.SelectedIndex = keyIdx >= 0 ? keyIdx : _cboKey.Items.IndexOf("T");
            if (_cboKey.SelectedIndex < 0) _cboKey.SelectedIndex = 0;
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("TaskFolder") != null;
            }
            catch { return false; }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                if (enable)
                {
                    string exe = Environment.ProcessPath
                        ?? System.IO.Path.Combine(AppContext.BaseDirectory, "TaskFolder.exe");
                    key?.SetValue("TaskFolder", $"\"{exe}\"");
                }
                else
                {
                    key?.DeleteValue("TaskFolder", false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update startup settings:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _shortcutManager.ShortcutsFolder,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open folder:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnChooseMenuLogo_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select Menu Header Logo",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            _selectedMenuLogoPath = dialog.FileName;
            UpdateMenuLogoPreview();
        }

        private void BtnRemoveMenuLogo_Click(object sender, EventArgs e)
        {
            _selectedMenuLogoPath = string.Empty;
            UpdateMenuLogoPreview();
        }

        private void UpdateMenuLogoPreview()
        {
            var oldImage = _picMenuLogo.Image;
            _picMenuLogo.Image = MenuLogoLoader.LoadScaledBitmap(_selectedMenuLogoPath, 32);
            oldImage?.Dispose();

            bool hasLogo = _picMenuLogo.Image != null;
            _lblMenuLogoFile.Text = hasLogo
                ? Path.GetFileName(_selectedMenuLogoPath)
                : string.IsNullOrWhiteSpace(_selectedMenuLogoPath)
                    ? "No logo selected"
                    : "Logo file is unavailable";
            _lblMenuLogoFile.Tag = _selectedMenuLogoPath;
            _btnRemoveMenuLogo.Enabled = !string.IsNullOrWhiteSpace(_selectedMenuLogoPath);
        }

        private void BtnClearShortcuts_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "Remove all shortcuts in the current profile? This cannot be undone.",
                "Confirm Clear Shortcuts",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            try
            {
                foreach (var sc in _shortcutManager.GetAllShortcuts())
                    _shortcutManager.RemoveShortcut(sc);

                MessageBox.Show("All shortcuts have been removed.",
                    "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to clear shortcuts:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            // Persist auto-start
            SetAutoStart(_chkAutoStart.Checked);
            _settingsService.Settings.StartWithWindows = _chkAutoStart.Checked;

            // Persist notifications
            _settingsService.Settings.ShowNotifications = _chkShowNotifications.Checked;

            // Persist hotkey settings
            bool hotkeyEnabled = _chkHotkeyEnabled.Checked;
            string mods = _cboModifiers.SelectedItem?.ToString() ?? "Ctrl+Alt";
            string key = _cboKey.SelectedItem?.ToString() ?? "T";

            _settingsService.Settings.HotkeyEnabled = hotkeyEnabled;
            _settingsService.Settings.HotkeyModifiers = mods;
            _settingsService.Settings.HotkeyKey = key;
            _settingsService.Settings.MenuHeaderLogoPath = _selectedMenuLogoPath;
            _settingsService.Settings.MenuItemHeight = (int)_numMenuItemHeight.Value;
            _settingsService.Settings.MenuFontSize = (float)_numMenuFontSize.Value;
            _settingsService.Save();

            // Apply hotkey change
            _hotkeyService.Unregister();
            if (hotkeyEnabled)
                _hotkeyService.Register(mods, key);

            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _picMenuLogo?.Image?.Dispose();
            base.Dispose(disposing);
            if (disposing)
                _uiFont?.Dispose();
        }
    }
}
