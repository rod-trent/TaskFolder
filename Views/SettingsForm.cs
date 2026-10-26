using L = TaskFolder.Services.LocalizationService;
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using TaskFolder.Services;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Views
{
    /// <summary>
    /// Settings dialog for TaskFolder.
    /// </summary>
    public partial class SettingsForm : ThemedForm
    {
        private readonly ShortcutManager _shortcutManager;
        private readonly SettingsService _settingsService;
        private readonly HotkeyService _hotkeyService;
        private readonly WindowsIntegrationService _windowsIntegration;
        private bool _startupWasRegistered;
        private bool _startupReadFailed;
        private Label _lblWindowsRegistration;
        private Font _uiFont;
        private SettingsDraft _settingsDraft;

        // Controls
        private CheckBox _chkAutoStart;
        private CheckBox _chkShowNotifications;
        private CheckBox _chkTaskbarButton;
        private CheckBox _chkTaskbarHover;
        private CheckBox _chkShowRecent;
        private CheckBox _chkShowMostUsed;
        private ComboBox _language, _theme;
        private CheckBox _widgetLocked;
        private NumericUpDown _hoverDelay;
        private NumericUpDown _widgetSize, _menuHideDelay;

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
            _windowsIntegration = new WindowsIntegrationService(settingsService.IsPortable);
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            Text = L.Text("TaskFolder Settings");
            ClientSize = new Size(620, 720);
            MinimumSize = new Size(560, 600);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
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
                Text = L.Text("Start TaskFolder when I sign in to Windows"),
                AutoSize = true,
                Margin = Padding.Empty
            };
            var integrationLayout = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false
            };
            _lblWindowsRegistration = new Label
            {
                AutoSize = true, MaximumSize = new Size(460, 0), Margin = new Padding(0, 8, 0, 8)
            };
            var repairButton = new Button
            {
                Text = L.Text("Repair Windows integration"), AutoSize = true, MinimumSize = new Size(190, 34)
            };
            repairButton.Click += (_, _) => RepairWindowsIntegration();
            var startupSettings = new LinkLabel
            {
                Text = L.Text("Windows startup settings"), AutoSize = true, Margin = new Padding(0, 8, 0, 4)
            };
            startupSettings.LinkClicked += (_, _) => OpenWindowsSettings("ms-settings:startupapps");
            var traySettings = new LinkLabel
            {
                Text = L.Text("Windows taskbar and system tray settings"), AutoSize = true
            };
            traySettings.LinkClicked += (_, _) => OpenWindowsSettings("ms-settings:taskbar");
            integrationLayout.Controls.Add(_chkAutoStart);
            integrationLayout.Controls.Add(_lblWindowsRegistration);
            integrationLayout.Controls.Add(repairButton);
            integrationLayout.Controls.Add(startupSettings);
            integrationLayout.Controls.Add(traySettings);
            content.Controls.Add(CreateSection(L.Text("Windows Integration"), integrationLayout));

            _chkTaskbarButton = new CheckBox
            {
                Text = L.Text("Show desktop TaskFolder widget"), AutoSize = true
            };
            _chkTaskbarHover = new CheckBox
            {
                Text = L.Text("Open menu on hover"), AutoSize = true
            };
            _chkTaskbarButton.CheckedChanged += (_, _) => UpdateWidgetControls();
            _chkTaskbarHover.CheckedChanged += (_, _) => UpdateWidgetControls();
            var taskbarLayout = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false
            };
            taskbarLayout.Controls.Add(_chkTaskbarButton);
            taskbarLayout.Controls.Add(_chkTaskbarHover);
            _widgetLocked = new CheckBox { Text = L.Text("Lock widget position"), AutoSize = true };
            _hoverDelay = new NumericUpDown { Minimum = 100, Maximum = 1500, Increment = 50, Width = 100 };
            _widgetSize = new NumericUpDown { Minimum = 50, Maximum = 200, Increment = 10, Width = 100,
                AccessibleName = L.Text("Widget size:") };
            _menuHideDelay = new NumericUpDown { Minimum = 200, Maximum = 2000, Increment = 100, Width = 100,
                AccessibleName = L.Text("Menu hide delay:") };
            taskbarLayout.Controls.Add(_widgetLocked);
            taskbarLayout.Controls.Add(CreateSettingRow(L.Text("Widget size:"), _widgetSize, "% (50–200)"));
            taskbarLayout.Controls.Add(CreateSettingRow(L.Text("Hover delay:"), _hoverDelay, L.Text("ms (100–1500)")));
            taskbarLayout.Controls.Add(CreateSettingRow(L.Text("Menu hide delay:"), _menuHideDelay, L.Text("ms (200–2000)")));
            taskbarLayout.Controls.Add(new Label { Text = L.Text("Hover menus hide after the pointer leaves the widget and menu. Returning cancels the countdown; keyboard input keeps the menu open."),
                AutoSize = true, MaximumSize = new Size(450, 0), Margin = new Padding(0, 6, 0, 0) });
            taskbarLayout.Controls.Add(new Label
            {
                Text = L.Text("Drag anywhere on the desktop; position is saved. The widget stays below applications. Use the tray icon while working in an app. Right-click for Settings."),
                AutoSize = true, MaximumSize = new Size(450, 0), Margin = new Padding(0, 6, 0, 0)
            });
            content.Controls.Add(CreateSection(L.Text("Desktop widget"), taskbarLayout));

            // ── Notifications ────────────────────────────────────────────────────
            _chkShowNotifications = new CheckBox
            {
                Text = L.Text("Show a balloon tip when an application is launched"),
                AutoSize = true,
                Margin = Padding.Empty
            };
            content.Controls.Add(CreateSection(L.Text("Notifications"), _chkShowNotifications));
            _chkShowRecent = new CheckBox { Text = LocalizationService.Get("Settings_ShowRecent", _settingsService.Settings.Language), AutoSize = true };
            _chkShowMostUsed = new CheckBox { Text = LocalizationService.Get("Settings_ShowMostUsed", _settingsService.Settings.Language), AutoSize = true };
            var history = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown, WrapContents = false };
            history.Controls.Add(_chkShowRecent);
            history.Controls.Add(_chkShowMostUsed);
            content.Controls.Add(CreateSection(LocalizationService.Get("Settings_History", _settingsService.Settings.Language), history));
            _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            _theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
            _language.Items.AddRange(new object[] { "System", "English", "Русский" }); _theme.Items.AddRange(new object[] { "System", "Light", "Dark" });
            _language.Format += (_, args) => args.Value = L.Text(args.ListItem.ToString()); _theme.Format += (_, args) => args.Value = L.Text(args.ListItem.ToString());
            _language.FormattingEnabled = _theme.FormattingEnabled = true;
            var presentation = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            presentation.Controls.AddRange(new Control[] { CreateInlineLabel(L.Text("Language")), _language, CreateInlineLabel(L.Text("Theme")), _theme });
            content.Controls.Add(CreateSection(L.Text("Language and theme"), presentation));

            // ── Global Hotkey ────────────────────────────────────────────────────
            _chkHotkeyEnabled = new CheckBox
            {
                Text = L.Text("Enable global hotkey"),
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
            hotkeyFields.Controls.Add(CreateInlineLabel(L.Text("Modifiers:")));
            hotkeyFields.Controls.Add(_cboModifiers);
            hotkeyFields.Controls.Add(CreateInlineLabel(L.Text("Key:")));
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
            content.Controls.Add(CreateSection(L.Text("Global Hotkey (pops the tray menu)"), hotkeyLayout));

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
                Text = L.Text("No logo selected"),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 0, 8, 6)
            };
            _btnChooseMenuLogo = new Button
            {
                Text = L.Text("Choose..."),
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
                Text = L.Text("Remove"),
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
            logoLayout.Controls.Add(CreateInlineLabel(L.Text("Header logo:")), 0, 0);
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
                L.Text("Item height:"), _numMenuItemHeight, L.Text("px — larger rows are easier to click")), 0, 1);
            appearanceLayout.Controls.Add(CreateSettingRow(
                L.Text("Text size:"), _numMenuFontSize, L.Text("pt — menu, search, and submenus")), 0, 2);
            content.Controls.Add(CreateSection(L.Text("Menu Appearance"), appearanceLayout));

            // ── Shortcuts Management ─────────────────────────────────────────────
            _btnOpenFolder = new Button
            {
                Text = L.Text("Open Shortcuts Folder"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(190, 34),
                Margin = new Padding(0, 0, 8, 8)
            };
            _btnOpenFolder.Click += BtnOpenFolder_Click;
            _btnClearShortcuts = new Button
            {
                Text = L.Text("Remove All Shortcuts"),
                AutoSize = true,
                Dock = DockStyle.Fill,
                MinimumSize = new Size(190, 34),
                Margin = new Padding(8, 0, 0, 8)
            };
            _btnClearShortcuts.Click += BtnClearShortcuts_Click;
            var lblFolder = new Label
            {
                Text = L.Text("Folder: {0}", _shortcutManager.ShortcutsFolder),
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
            content.Controls.Add(CreateSection(L.Text("Shortcuts Management"), shortcutsLayout));

            // ── About ────────────────────────────────────────────────────────────
            var aboutLabel = new Label
            {
                Text = L.Text("TaskFolder {0} — Windows application launcher — MIT License", typeof(SettingsForm).Assembly.GetName().Version?.ToString(3)),
                Dock = DockStyle.Top,
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty
            };
            content.Controls.Add(CreateSection(L.Text("About"), aboutLabel));

            // ── Buttons ──────────────────────────────────────────────────────────
            _btnOK = new Button
            {
                Text = L.Text("OK"),
                AutoSize = true,
                MinimumSize = new Size(96, 34),
                Margin = new Padding(8, 0, 0, 0)
            };
            _btnOK.Click += BtnOK_Click;
            _btnCancel = new Button
            {
                Text = L.Text("Cancel"),
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
            var reload = new Button { Text = L.Text("Reload saved settings"), AutoSize = true, MinimumSize = new Size(96, 34) };
            reload.Click += (_, _) =>
            {
                if (MessageBox.Show(this, L.Text("Reload saved settings and replace the input in this window?"), L.Text("Reload settings"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { _settingsService.Load(); LoadSettings(); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, L.Text("Settings could not be reloaded"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            footer.Controls.Add(reload);
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

        private void UpdateWidgetControls()
        {
            if (_widgetSize == null || _menuHideDelay == null || _hoverDelay == null || _widgetLocked == null) return;
            bool enabled = _chkTaskbarButton.Checked;
            _chkTaskbarHover.Enabled = enabled;
            _widgetSize.Enabled = enabled;
            _widgetLocked.Enabled = enabled;
            _hoverDelay.Enabled = _menuHideDelay.Enabled = enabled && _chkTaskbarHover.Checked;
        }

        private void LoadSettings()
        {
            RefreshWindowsRegistration();
            _chkShowNotifications.Checked = _settingsService.Settings.ShowNotifications;
            _chkShowRecent.Checked = _settingsService.Settings.ShowRecent;
            _chkShowMostUsed.Checked = _settingsService.Settings.ShowMostUsed;
            _language.SelectedItem = _settingsService.Settings.Language; _theme.SelectedItem = _settingsService.Settings.Theme;
            _chkTaskbarButton.Checked = _settingsService.Settings.TaskbarButtonEnabled;
            _chkTaskbarHover.Checked = _settingsService.Settings.TaskbarHoverEnabled;
            _widgetLocked.Checked = _settingsService.Settings.WidgetPositionLocked;
            _hoverDelay.Value = Math.Clamp(_settingsService.Settings.WidgetHoverDelayMs, 100, 1500);
            _widgetSize.Value = Math.Clamp(_settingsService.Settings.WidgetSizePercent, 50, 200);
            _menuHideDelay.Value = Math.Clamp(_settingsService.Settings.WidgetMenuHideDelayMs, 200, 2000);
            UpdateWidgetControls();
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
            _settingsDraft = new SettingsDraft(_settingsService.Settings);
        }

        private void RefreshWindowsRegistration()
        {
            try
            {
                _startupWasRegistered = _windowsIntegration.IsStartupRegistered;
                _startupReadFailed = false;
                _chkAutoStart.Enabled = true;
                _chkAutoStart.Checked = _startupWasRegistered;
                string startup = !_startupWasRegistered ? L.Text("Startup is not registered.")
                    : _windowsIntegration.StartupTargetsCurrentCopy ? L.Text("Startup is registered for this copy.")
                    : L.Text("Startup points to another path or uses old launch options. Use Repair to update it.");
                string menu = File.Exists(_windowsIntegration.StartMenuShortcutPath)
                    ? L.Text("A Start menu shortcut is registered.") : L.Text("Use Repair to add TaskFolder to the Start menu.");
                _lblWindowsRegistration.Text = startup + " " + menu +
                    L.Text(" Windows can disable startup separately; check Windows startup settings.") +
                    (_settingsService.IsPortable ? L.Text(" Keep this portable copy in a permanent folder.") : string.Empty);
            }
            catch (Exception ex)
            {
                _startupReadFailed = true;
                _chkAutoStart.Enabled = false;
                _lblWindowsRegistration.Text = L.Text("Windows registration could not be read: {0}", ex.Message);
            }
        }

        private void RepairWindowsIntegration()
        {
            try
            {
                if (!CheckSettingsDraft()) return;
                if (_startupReadFailed)
                    throw new InvalidOperationException(L.Text("Startup registration could not be read. Check your account permissions first."));
                _windowsIntegration.RegisterStartMenuShortcut();
                _windowsIntegration.SetStartupEnabled(_chkAutoStart.Checked);
                _settingsService.Settings.StartWithWindows = _chkAutoStart.Checked;
                _settingsService.Save();
                _settingsDraft = new SettingsDraft(_settingsService.Settings);
                RefreshWindowsRegistration();
            }
            catch (Exception ex)
            {
                RefreshWindowsRegistration();
                MessageBox.Show(this, L.Text("Windows integration could not be fully updated:\n{0}", ex.Message),
                    "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenWindowsSettings(string uri)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.Text("Could not open Windows settings:\n{0}", ex.Message),
                    "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show(L.Text("Failed to open folder:\n{0}", ex.Message),
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnChooseMenuLogo_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = L.Text("Select Menu Header Logo"),
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
                    ? L.Text("No logo selected")
                    : L.Text("Logo file is unavailable");
            _lblMenuLogoFile.Tag = _selectedMenuLogoPath;
            _btnRemoveMenuLogo.Enabled = !string.IsNullOrWhiteSpace(_selectedMenuLogoPath);
        }

        private void BtnClearShortcuts_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                L.Text("Remove all shortcuts in the current profile? Managed links can be restored through Recovery; targets and raw EXE files remain."),
                L.Text("Confirm Clear Shortcuts"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes) return;

            try
            {
                _shortcutManager.RemoveShortcuts(_shortcutManager.GetAllShortcuts());

                MessageBox.Show(L.Text("All shortcuts have been removed."),
                    "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.Text("Failed to clear shortcuts:\n{0}", ex.Message),
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            if (!CheckSettingsDraft()) return;
            // Do not rewrite registration when unrelated preferences are saved, or hide a failed write.
            if (!_startupReadFailed && _chkAutoStart.Checked != _startupWasRegistered)
            {
                try { _windowsIntegration.SetStartupEnabled(_chkAutoStart.Checked); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, L.Text("Startup could not be updated:\n{0}", ex.Message),
                        "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }
            if (!_startupReadFailed)
                _settingsService.Settings.StartWithWindows = _chkAutoStart.Checked;

            // Persist notifications
            _settingsService.Settings.ShowNotifications = _chkShowNotifications.Checked;
            _settingsService.Settings.ShowRecent = _chkShowRecent.Checked;
            _settingsService.Settings.ShowMostUsed = _chkShowMostUsed.Checked;
            _settingsService.Settings.Language = _language.SelectedItem as string ?? "System"; _settingsService.Settings.Theme = _theme.SelectedItem as string ?? "System";
            _settingsService.Settings.TaskbarButtonEnabled = _chkTaskbarButton.Checked;
            _settingsService.Settings.TaskbarHoverEnabled = _chkTaskbarHover.Checked;
            _settingsService.Settings.WidgetPositionLocked = _widgetLocked.Checked;
            _settingsService.Settings.WidgetHoverDelayMs = (int)_hoverDelay.Value;
            _settingsService.Settings.WidgetSizePercent = (int)_widgetSize.Value;
            _settingsService.Settings.WidgetMenuHideDelayMs = (int)_menuHideDelay.Value;

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
            var saveResult = _settingsService.TrySave();
            if (!saveResult.Success)
            {
                MessageBox.Show(this, new StatePersistenceException(saveResult).Message + L.Text("\nYour input is still here."),
                    "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Apply hotkey change
            _hotkeyService.Unregister();
            if (hotkeyEnabled)
                _hotkeyService.Register(mods, key);

            DialogResult = DialogResult.OK;
            Close();
        }

        private bool CheckSettingsDraft()
        {
            try { _settingsService.Load(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L.Text("Saved settings could not be checked"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_settingsService.StateLoad.ReadOnly)
            {
                MessageBox.Show(this, new StatePersistenceException(_settingsService.StateLoad.Result).Message,
                    L.Text("Saved settings need recovery"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (_settingsDraft == null || _settingsDraft.IsCurrent(_settingsService.Settings)) return true;
            MessageBox.Show(this, L.Text("Saved preferences changed while this window was open. Your input has been kept.\n") +
                L.Text("Use Reload saved settings to review the current values before applying changes."),
                L.Text("Settings changed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
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
