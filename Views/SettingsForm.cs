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
            SuspendLayout();
            Text = "TaskFolder Settings";
            ClientSize = new Size(620, 720);
            MinimumSize = new Size(560, 600);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScroll = false;
            _uiFont = Windows11MenuStyle.CreateFont(9.5f);
            Font = _uiFont;
            BackColor = Color.FromArgb(249, 249, 249);

            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = BackColor
            };
            var content = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 0,
                GrowStyle = TableLayoutPanelGrowStyle.AddRows,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(16, 12, 16, 16),
                BackColor = BackColor
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            scrollHost.Controls.Add(content);
            Controls.Add(scrollHost);

            // ── Startup ──────────────────────────────────────────────────────────
            _chkAutoStart = new CheckBox
            {
                Text = "Start TaskFolder when Windows starts",
                AutoSize = true,
                Margin = Padding.Empty
            };
            content.Controls.Add(CreateSection("Startup", _chkAutoStart));

            // ── Notifications ────────────────────────────────────────────────────
            _chkShowNotifications = new CheckBox
            {
                Text = "Show a balloon tip when an application is launched",
                AutoSize = true,
                Margin = Padding.Empty
            };
            content.Controls.Add(CreateSection("Notifications", _chkShowNotifications));

            // ── Global Hotkey ────────────────────────────────────────────────────
            _chkHotkeyEnabled = new CheckBox
            {
                Text = "Enable global hotkey",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };

            _cboModifiers = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 160,
                Margin = new Padding(6, 0, 20, 0)
            };
            _cboModifiers.Items.AddRange(new object[]
            {
                "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift", "Win"
            });
            _cboKey = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 96,
                Margin = new Padding(6, 0, 0, 0)
            };
            // Populate with A-Z and F1-F12
            for (char c = 'A'; c <= 'Z'; c++) _cboKey.Items.Add(c.ToString());
            for (int i = 1; i <= 12; i++) _cboKey.Items.Add($"F{i}");
            var hotkeyFields = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = Padding.Empty
            };
            hotkeyFields.Controls.Add(CreateInlineLabel("Modifiers:"));
            hotkeyFields.Controls.Add(_cboModifiers);
            hotkeyFields.Controls.Add(CreateInlineLabel("Key:"));
            hotkeyFields.Controls.Add(_cboKey);
            var hotkeyLayout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty
            };
            hotkeyLayout.Controls.Add(_chkHotkeyEnabled, 0, 0);
            hotkeyLayout.Controls.Add(hotkeyFields, 0, 1);
            content.Controls.Add(CreateSection("Global Hotkey (pops the tray menu)", hotkeyLayout));

            // ── Menu appearance ──────────────────────────────────────────────────
            _picMenuLogo = new PictureBox
            {
                Size = new Size(40, 40),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.CenterImage,
                Margin = new Padding(8, 0, 8, 6)
            };
            _lblMenuLogoFile = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                Text = "No logo selected",
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 8, 6)
            };
            _btnChooseMenuLogo = new Button
            {
                Text = "Choose...",
                AutoSize = true,
                MinimumSize = new Size(88, 32),
                Margin = new Padding(0, 0, 6, 6)
            };
            _btnChooseMenuLogo.Click += BtnChooseMenuLogo_Click;
            _numMenuItemHeight = new NumericUpDown
            {
                Width = 80,
                Minimum = 32,
                Maximum = 72,
                Increment = 2,
                Margin = new Padding(8, 3, 8, 3)
            };
            _numMenuFontSize = new NumericUpDown
            {
                Width = 80,
                Minimum = 9,
                Maximum = 18,
                DecimalPlaces = 1,
                Increment = 0.5m,
                Margin = new Padding(8, 3, 8, 3)
            };
            _btnRemoveMenuLogo = new Button
            {
                Text = "Remove",
                AutoSize = true,
                MinimumSize = new Size(88, 32),
                Margin = new Padding(0, 0, 0, 6)
            };
            _btnRemoveMenuLogo.Click += BtnRemoveMenuLogo_Click;
            var logoActions = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = Padding.Empty
            };
            logoActions.Controls.Add(_btnChooseMenuLogo);
            logoActions.Controls.Add(_btnRemoveMenuLogo);
            var logoLayout = new TableLayoutPanel
            {
                ColumnCount = 3,
                RowCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 0, 6)
            };
            logoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            logoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            logoLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            logoLayout.Controls.Add(CreateInlineLabel("Header logo:"), 0, 0);
            logoLayout.Controls.Add(_picMenuLogo, 1, 0);
            logoLayout.Controls.Add(_lblMenuLogoFile, 2, 0);
            logoLayout.Controls.Add(logoActions, 0, 1);
            logoLayout.SetColumnSpan(logoActions, 3);
            var appearanceLayout = new TableLayoutPanel
            {
                ColumnCount = 1,
                RowCount = 3,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty
            };
            appearanceLayout.Controls.Add(logoLayout, 0, 0);
            appearanceLayout.Controls.Add(CreateSettingRow(
                "Item height:", _numMenuItemHeight, "px — larger rows are easier to click"), 0, 1);
            appearanceLayout.Controls.Add(CreateSettingRow(
                "Text size:", _numMenuFontSize, "pt — menu, search, and submenus"), 0, 2);
            content.Controls.Add(CreateSection("Menu Appearance", appearanceLayout));

            // ── Shortcuts Management ─────────────────────────────────────────────
            _btnOpenFolder = new Button
            {
                Text = "Open Shortcuts Folder",
                AutoSize = true,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(190, 34),
                Margin = new Padding(0, 0, 8, 8)
            };
            _btnOpenFolder.Click += BtnOpenFolder_Click;
            _btnClearShortcuts = new Button
            {
                Text = "Remove All Shortcuts",
                AutoSize = true,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(190, 34),
                Margin = new Padding(8, 0, 0, 8)
            };
            _btnClearShortcuts.Click += BtnClearShortcuts_Click;
            var lblFolder = new Label
            {
                Text = $"Folder: {_shortcutManager.ShortcutsFolder}",
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                AutoSize = false,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
            var shortcutsLayout = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 2,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = Padding.Empty
            };
            shortcutsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            shortcutsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            shortcutsLayout.Controls.Add(_btnOpenFolder, 0, 0);
            shortcutsLayout.Controls.Add(_btnClearShortcuts, 1, 0);
            shortcutsLayout.Controls.Add(lblFolder, 0, 1);
            shortcutsLayout.SetColumnSpan(lblFolder, 2);
            content.Controls.Add(CreateSection("Shortcuts Management", shortcutsLayout));

            // ── About ────────────────────────────────────────────────────────────
            var aboutLabel = new Label
            {
                Text = "TaskFolder v1.2.1 — Windows 11 Application Launcher — MIT License",
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
            content.Controls.Add(CreateSection("About", aboutLabel));

            // ── Buttons ──────────────────────────────────────────────────────────
            _btnOK = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                AutoSize = true,
                MinimumSize = new Size(96, 34),
                Margin = new Padding(8, 0, 0, 0)
            };
            _btnOK.Click += BtnOK_Click;
            _btnCancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                MinimumSize = new Size(96, 34),
                Margin = new Padding(8, 0, 0, 0)
            };
            var footer = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Margin = new Padding(0, 4, 0, 0)
            };
            footer.Controls.Add(_btnCancel);
            footer.Controls.Add(_btnOK);
            content.Controls.Add(footer);

            AcceptButton = _btnOK;
            CancelButton = _btnCancel;
            ResumeLayout(true);
        }

        private static GroupBox CreateSection(string title, Control body)
        {
            var section = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(12, 24, 12, 12),
                Margin = new Padding(0, 0, 0, 8)
            };
            body.Dock = DockStyle.Top;
            section.Controls.Add(body);
            return section;
        }

        private static Label CreateInlineLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 0, 6)
            };
        }

        private static FlowLayoutPanel CreateSettingRow(
            string label,
            NumericUpDown input,
            string description)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 4)
            };
            var title = CreateInlineLabel(label);
            title.MinimumSize = new Size(96, 0);
            row.Controls.Add(title);
            row.Controls.Add(input);
            row.Controls.Add(CreateInlineLabel(description));
            return row;
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
