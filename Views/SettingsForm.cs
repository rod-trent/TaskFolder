using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using TaskFolder.Services;

namespace TaskFolder.Views
{
    /// <summary>
    /// Settings dialog for TaskFolder. All groups live in a scrollable panel so
    /// the form fits on small or high-DPI displays without being cut off.
    /// </summary>
    public partial class SettingsForm : Form
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_settingsService?.Settings.DarkMode == true)
            {
                int val = 1;
                DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int));
                foreach (var p in _scrollPanels)
                    SetWindowTheme(p.Handle, "DarkMode_Explorer", null);
                ApplyDarkMode(this);
            }
            SelectSettingsTab(_selectedTabIndex);
        }

        private void SelectSettingsTab(int index)
        {
            _selectedTabIndex = index;
            bool dark = _settingsService?.Settings.DarkMode == true;

            var selBg   = dark ? Color.FromArgb(45, 45, 65)   : SystemColors.Window;
            var unselBg = dark ? Color.FromArgb(30, 30, 46)   : SystemColors.Control;
            var fg      = dark ? Color.FromArgb(205, 214, 244) : SystemColors.ControlText;
            var sepClr  = dark ? Color.FromArgb(60, 60, 80)   : SystemColors.ControlDark;

            if (_tabSeparator != null) _tabSeparator.BackColor = sepClr;

            for (int i = 0; i < _tabBtns.Length; i++)
            {
                bool sel = i == index;
                _tabBtns[i].BackColor = sel ? selBg : unselBg;
                _tabBtns[i].ForeColor = fg;
                _tabBtns[i].Font = new Font(Font, sel ? FontStyle.Bold : FontStyle.Regular);
                if (_tabPanels[i] != null) _tabPanels[i].Visible = sel;
            }
        }

        private readonly ShortcutManager _shortcutManager;
        private readonly SettingsService _settingsService;
        private readonly HotkeyService _hotkeyService;
        private readonly MouseHotkeyService _mouseHotkeyService;

        // Controls
        private CheckBox _chkAutoStart;
        private CheckBox _chkShowNotifications;
        private CheckBox _chkCheckForUpdates;

        private CheckBox _chkHotkeyEnabled;
        private ComboBox _cboModifiers;
        private ComboBox _cboKey;

        private Button _btnOpenFolder;
        private Button _btnClearShortcuts;

        private TextBox _txtProfilesRoot;
        private Button _btnBrowseProfilesRoot;
        private Button _btnClearProfilesRoot;
        private Button _btnUseDropbox;
        private Button _btnUseOneDrive;
        private Panel _scrollPanel;
        private readonly System.Collections.Generic.List<Panel> _scrollPanels = new();
        private Button[] _tabBtns   = Array.Empty<Button>();
        private Panel[]  _tabPanels = Array.Empty<Panel>();
        private Panel    _tabSeparator = null!;
        private int      _selectedTabIndex = 0;

        // Mouse hotkey
        private CheckBox _chkMouseHotkeyEnabled;
        private Label _lblMouseButton;
        private Button _btnSetMouseButton;
        private string _capturedMouseButton;

        // Appearance
        private NumericUpDown _nudFontSize;
        private CheckBox _chkDarkMode;
        private RadioButton _radIconSmall;
        private RadioButton _radIconLarge;
        private TextBox _txtCleanupTerms;

        private Button _btnOK;
        private Button _btnCancel;

        public SettingsForm(ShortcutManager shortcutManager,
                            SettingsService settingsService,
                            HotkeyService hotkeyService,
                            MouseHotkeyService mouseHotkeyService = null)
        {
            _shortcutManager = shortcutManager;
            _settingsService = settingsService;
            _hotkeyService = hotkeyService;
            _mouseHotkeyService = mouseHotkeyService;
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "TaskFolder Settings";
            try { this.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            const int W = 440;
            const int X = 15;

            Panel MakeTabPanel()
            {
                var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(0, 0, 0, 8) };
                _scrollPanels.Add(p);
                return p;
            }

            // ── Custom tab bar (no TabControl — avoids system-drawn borders) ──────
            var tabBar     = new Panel { Dock = DockStyle.Top, Height = 34 };
            _tabSeparator  = new Panel { Dock = DockStyle.Top, Height = 1  };
            var contentHost = new Panel { Dock = DockStyle.Fill };

            string[] tabTitles = { "General", "Hotkeys", "Shortcuts", "Appearance" };
            _tabBtns   = new Button[tabTitles.Length];
            _tabPanels = new Panel[tabTitles.Length];

            for (int ti = 0; ti < tabTitles.Length; ti++)
            {
                int idx = ti;
                var tb = new Button
                {
                    Text      = tabTitles[ti],
                    FlatStyle = FlatStyle.Flat,
                    Size      = new Size(96, 34),
                    Location  = new Point(ti * 96, 0),
                    Cursor    = Cursors.Hand,
                    TabStop   = false,
                    Font      = new Font(Font, FontStyle.Regular)
                };
                tb.FlatAppearance.BorderSize = 0;
                tb.Click += (s, e) => SelectSettingsTab(idx);
                tabBar.Controls.Add(tb);
                _tabBtns[ti] = tb;
            }

            // ════════════════════════════════════════════════
            // TAB 1 — General
            // ════════════════════════════════════════════════
            var panGeneral = MakeTabPanel();
            contentHost.Controls.Add(panGeneral);
            _tabPanels[0] = panGeneral;

            int y = 10;

            var grpStartup = new GroupBox { Text = "Startup", Location = new Point(X, y), Size = new Size(W, 60) };
            _chkAutoStart = new CheckBox { Text = "Start TaskFolder when Windows starts", Location = new Point(12, 20), Size = new Size(W - 24, 20) };
            grpStartup.Controls.Add(_chkAutoStart);
            panGeneral.Controls.Add(grpStartup);
            y += grpStartup.Height + 8;

            var grpNotif = new GroupBox { Text = "Notifications", Location = new Point(X, y), Size = new Size(W, 80) };
            _chkShowNotifications = new CheckBox { Text = "Show a balloon tip when an application is launched", Location = new Point(12, 20), Size = new Size(W - 24, 20) };
            _chkCheckForUpdates   = new CheckBox { Text = "Check GitHub for a newer release on startup",       Location = new Point(12, 48), Size = new Size(W - 24, 20) };
            grpNotif.Controls.Add(_chkShowNotifications);
            grpNotif.Controls.Add(_chkCheckForUpdates);
            panGeneral.Controls.Add(grpNotif);
            y += grpNotif.Height + 8;

            var grpTray    = new GroupBox { Text = "Taskbar Visibility", Location = new Point(X, y), Size = new Size(W, 80) };
            var btnPinTray = new Button   { Text = "Pin to System Tray...", Location = new Point(12, 22), Size = new Size(160, 28) };
            btnPinTray.Click += BtnPinTray_Click;
            grpTray.Controls.Add(btnPinTray);
            grpTray.Controls.Add(new Label
            {
                Text = "Opens Windows Settings → Taskbar → Other system tray icons." + Environment.NewLine +
                       "Toggle 'TaskFolder' to On so the icon always shows on the taskbar.",
                Location = new Point(180, 18), Size = new Size(W - 192, 44), ForeColor = Color.Gray
            });
            panGeneral.Controls.Add(grpTray);
            y += grpTray.Height + 8;

            var grpAbout = new GroupBox { Text = "About", Location = new Point(X, y), Size = new Size(W, 55) };
            grpAbout.Controls.Add(new Label
            {
                Text = "TaskFolder v1.3.0 — Windows 11 Application Launcher — MIT License",
                Location = new Point(12, 18), Size = new Size(W - 24, 28), TextAlign = ContentAlignment.MiddleCenter
            });
            panGeneral.Controls.Add(grpAbout);

            // ════════════════════════════════════════════════
            // TAB 2 — Hotkeys
            // ════════════════════════════════════════════════
            var panHotkeys = MakeTabPanel();
            contentHost.Controls.Add(panHotkeys);
            _tabPanels[1] = panHotkeys;

            y = 10;

            var grpHotkey = new GroupBox { Text = "Global Hotkey (pops the tray menu)", Location = new Point(X, y), Size = new Size(W, 90) };
            _chkHotkeyEnabled = new CheckBox { Text = "Enable global hotkey", Location = new Point(12, 20), Size = new Size(200, 20) };
            grpHotkey.Controls.Add(_chkHotkeyEnabled);
            grpHotkey.Controls.Add(new Label { Text = "Modifiers:", Location = new Point(12, 48), AutoSize = true });
            _cboModifiers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(80, 45), Width = 130 };
            _cboModifiers.Items.AddRange(new object[] { "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift", "Win" });
            grpHotkey.Controls.Add(_cboModifiers);
            grpHotkey.Controls.Add(new Label { Text = "Key:", Location = new Point(224, 48), AutoSize = true });
            _cboKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(255, 45), Width = 80 };
            for (char c = 'A'; c <= 'Z'; c++) _cboKey.Items.Add(c.ToString());
            for (int i = 1; i <= 12; i++) _cboKey.Items.Add($"F{i}");
            grpHotkey.Controls.Add(_cboKey);
            panHotkeys.Controls.Add(grpHotkey);
            y += grpHotkey.Height + 8;

            var grpMouse = new GroupBox { Text = "Mouse Button Hotkey", Location = new Point(X, y), Size = new Size(W, 80) };
            _chkMouseHotkeyEnabled = new CheckBox { Text = "Enable mouse button hotkey", Location = new Point(12, 20), Size = new Size(200, 20) };
            grpMouse.Controls.Add(_chkMouseHotkeyEnabled);
            _lblMouseButton = new Label { Text = "Button: Middle Button", Location = new Point(12, 48), AutoSize = true };
            grpMouse.Controls.Add(_lblMouseButton);
            _btnSetMouseButton = new Button { Text = "Set Button...", Location = new Point(W - 130, 44), Size = new Size(110, 26) };
            _btnSetMouseButton.Click += BtnSetMouseButton_Click;
            grpMouse.Controls.Add(_btnSetMouseButton);
            panHotkeys.Controls.Add(grpMouse);

            // ════════════════════════════════════════════════
            // TAB 3 — Shortcuts
            // ════════════════════════════════════════════════
            var panShortcuts = MakeTabPanel();
            contentHost.Controls.Add(panShortcuts);
            _tabPanels[2] = panShortcuts;

            y = 10;

            var grpShortcuts = new GroupBox { Text = "Shortcuts Management", Location = new Point(X, y), Size = new Size(W, 100) };
            _btnOpenFolder    = new Button { Text = "Open Shortcuts Folder", Location = new Point(12,  25), Size = new Size(180, 28) };
            _btnClearShortcuts = new Button { Text = "Remove All Shortcuts", Location = new Point(210, 25), Size = new Size(180, 28) };
            _btnOpenFolder.Click     += BtnOpenFolder_Click;
            _btnClearShortcuts.Click += BtnClearShortcuts_Click;
            grpShortcuts.Controls.Add(_btnOpenFolder);
            grpShortcuts.Controls.Add(_btnClearShortcuts);
            grpShortcuts.Controls.Add(new Label
            {
                Text = $"Folder: {_shortcutManager.ShortcutsFolder}",
                Location = new Point(12, 65), Size = new Size(W - 24, 22), AutoEllipsis = true
            });
            panShortcuts.Controls.Add(grpShortcuts);
            y += grpShortcuts.Height + 8;

            var grpSync = new GroupBox { Text = "Profile Sync (OneDrive / Dropbox / Network Share)", Location = new Point(X, y), Size = new Size(W, 118) };
            grpSync.Controls.Add(new Label { Text = "Root path (leave blank to use %APPDATA%\\TaskFolder):", Location = new Point(12, 20), AutoSize = true });
            _txtProfilesRoot      = new TextBox { Location = new Point(12, 38), Width = W - 146, PlaceholderText = "e.g. C:\\Users\\You\\OneDrive\\TaskFolder" };
            _btnBrowseProfilesRoot = new Button { Text = "Browse...", Location = new Point(W - 130, 36), Width = 70 };
            _btnClearProfilesRoot  = new Button { Text = "Clear",     Location = new Point(W -  56, 36), Width = 44 };
            _btnUseOneDrive        = new Button { Text = "Use OneDrive", Location = new Point(12,  64), Width = 100 };
            _btnUseDropbox         = new Button { Text = "Use Dropbox",  Location = new Point(118, 64), Width = 100 };
            _btnBrowseProfilesRoot.Click += BtnBrowseProfilesRoot_Click;
            _btnClearProfilesRoot.Click  += (s, e) => _txtProfilesRoot.Clear();
            _btnUseOneDrive.Click        += BtnUseOneDrive_Click;
            _btnUseDropbox.Click         += BtnUseDropbox_Click;
            grpSync.Controls.Add(_txtProfilesRoot);
            grpSync.Controls.Add(_btnBrowseProfilesRoot);
            grpSync.Controls.Add(_btnClearProfilesRoot);
            grpSync.Controls.Add(_btnUseOneDrive);
            grpSync.Controls.Add(_btnUseDropbox);
            grpSync.Controls.Add(new Label
            {
                Text = "Restart TaskFolder after changing this path for it to take effect.",
                Location = new Point(12, 92), Size = new Size(W - 24, 18),
                ForeColor = Color.Gray, Font = new Font(Font, FontStyle.Italic)
            });
            panShortcuts.Controls.Add(grpSync);

            // ════════════════════════════════════════════════
            // TAB 4 — Appearance
            // ════════════════════════════════════════════════
            var panAppearance = MakeTabPanel();
            contentHost.Controls.Add(panAppearance);
            _tabPanels[3] = panAppearance;

            y = 10;

            var grpAppearance = new GroupBox { Text = "Appearance", Location = new Point(X, y), Size = new Size(W, 165) };
            grpAppearance.Controls.Add(new Label { Text = "Menu font size (pt):", Location = new Point(12, 22), AutoSize = true });
            _nudFontSize = new NumericUpDown { Location = new Point(130, 19), Width = 60, Minimum = 7, Maximum = 20, DecimalPlaces = 0, Value = 9 };
            grpAppearance.Controls.Add(_nudFontSize);
            grpAppearance.Controls.Add(new Label { Text = "(restart to apply)", Location = new Point(198, 22), AutoSize = true, ForeColor = Color.Gray });
            _chkDarkMode = new CheckBox { Text = "Dark mode menu", Location = new Point(12, 48), AutoSize = true };
            grpAppearance.Controls.Add(_chkDarkMode);
            grpAppearance.Controls.Add(new Label { Text = "(restart to apply)", Location = new Point(138, 50), AutoSize = true, ForeColor = Color.Gray });
            grpAppearance.Controls.Add(new Label { Text = "Icon size:", Location = new Point(12, 78), AutoSize = true });
            _radIconSmall = new RadioButton { Text = "Small (16 px)", Location = new Point(75,  75), AutoSize = true, Checked = true };
            _radIconLarge = new RadioButton { Text = "Large (32 px)", Location = new Point(185, 75), AutoSize = true };
            grpAppearance.Controls.Add(new Label { Text = "(restart to apply)", Location = new Point(300, 78), AutoSize = true, ForeColor = Color.Gray });
            grpAppearance.Controls.Add(_radIconSmall);
            grpAppearance.Controls.Add(_radIconLarge);
            grpAppearance.Controls.Add(new Label { Text = "Strip from shortcut names (comma-separated, case-insensitive):", Location = new Point(12, 108), AutoSize = true });
            _txtCleanupTerms = new TextBox { Location = new Point(12, 126), Size = new Size(W - 24, 20) };
            grpAppearance.Controls.Add(_txtCleanupTerms);
            panAppearance.Controls.Add(grpAppearance);

            // ─────────────────────────────────────────────────────────────────────
            // Button panel — docked to the bottom
            // ─────────────────────────────────────────────────────────────────────
            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };

            _btnOK = new Button
            {
                Text = "OK", DialogResult = DialogResult.OK,
                Size = new Size(80, 28), Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            _btnOK.Click += BtnOK_Click;

            _btnCancel = new Button
            {
                Text = "Cancel", DialogResult = DialogResult.Cancel,
                Size = new Size(80, 28), Anchor = AnchorStyles.Right | AnchorStyles.Top
            };

            btnPanel.Controls.Add(_btnOK);
            btnPanel.Controls.Add(_btnCancel);
            btnPanel.Resize += (s, e) =>
            {
                _btnCancel.Location = new Point(btnPanel.Width - _btnCancel.Width - 8, 8);
                _btnOK.Location     = new Point(btnPanel.Width - _btnOK.Width - _btnCancel.Width - 16, 8);
            };

            AcceptButton = _btnOK;
            CancelButton = _btnCancel;

            _scrollPanel = panGeneral;

            // Dock order: Bottom first, then Top panels, then Fill
            Controls.Add(contentHost);
            Controls.Add(_tabSeparator);
            Controls.Add(tabBar);
            Controls.Add(btnPanel);

            // Show first tab
            SelectSettingsTab(0);

            // Form height: tallest tab (General ~340px) + tab strip + buttons
            int screenH = Screen.PrimaryScreen?.WorkingArea.Height ?? 900;
            ClientSize = new Size(W + 50, Math.Min(420, (int)(screenH * 0.82)));
            MinimumSize = new Size(W + 50, 340);

            ResumeLayout(false);
            PerformLayout();
        }

        private void LoadSettings()
        {
            _chkAutoStart.Checked = IsAutoStartEnabled();
            _chkShowNotifications.Checked = _settingsService.Settings.ShowNotifications;
            _chkCheckForUpdates.Checked = _settingsService.Settings.CheckForUpdates;
            _chkHotkeyEnabled.Checked = _settingsService.Settings.HotkeyEnabled;
            _txtProfilesRoot.Text = _settingsService.Settings.ProfilesRootPath ?? string.Empty;

            _chkMouseHotkeyEnabled.Checked = _settingsService.Settings.MouseHotkeyEnabled;
            _capturedMouseButton = _settingsService.Settings.MouseHotkeyButton ?? "Middle";
            _lblMouseButton.Text = "Button: " + MouseHotkeyService.FriendlyName(_capturedMouseButton);

            float fs = _settingsService.Settings.MenuFontSize;
            _nudFontSize.Value = (decimal)(fs >= 7f && fs <= 20f ? fs : 9f);
            _chkDarkMode.Checked = _settingsService.Settings.DarkMode;

            bool largeIcon = _settingsService.Settings.MenuIconSize == 32;
            _radIconSmall.Checked = !largeIcon;
            _radIconLarge.Checked = largeIcon;

            _txtCleanupTerms.Text = string.Join(", ", _settingsService.Settings.NameCleanupTerms ?? []);

            string mod = _settingsService.Settings.HotkeyModifiers ?? "Ctrl+Alt";
            int modIdx = _cboModifiers.Items.IndexOf(mod);
            _cboModifiers.SelectedIndex = modIdx >= 0 ? modIdx : 0;

            string key = _settingsService.Settings.HotkeyKey ?? "T";
            int keyIdx = _cboKey.Items.IndexOf(key);
            _cboKey.SelectedIndex = keyIdx >= 0 ? keyIdx : _cboKey.Items.IndexOf("T");
            if (_cboKey.SelectedIndex < 0) _cboKey.SelectedIndex = 0;

            if (_settingsService.Settings.DarkMode)
            {
                ApplyDarkMode(this);
                SelectSettingsTab(_selectedTabIndex);
            }
        }

        private static void ApplyDarkMode(Control root)
        {
            var bg     = Color.FromArgb(30, 30, 46);
            var bgCtrl = Color.FromArgb(45, 45, 65);
            var fg     = Color.FromArgb(205, 214, 244);
            var fgMute = Color.FromArgb(140, 145, 165);
            var border = Color.FromArgb(50, 50, 70);

            void Style(Control c)
            {
                switch (c)
                {
                    case GroupBox gb:
                        gb.BackColor = bg;
                        gb.ForeColor = fg;
                        break;
                    case TabPage tp:
                        tp.BackColor = bg;
                        tp.ForeColor = fg;
                        break;
                    case Panel p:
                        p.BackColor = bg;
                        break;
                    case Label lbl:
                        lbl.BackColor = Color.Transparent;
                        lbl.ForeColor = lbl.ForeColor == Color.Gray ? fgMute : fg;
                        break;
                    case CheckBox cb:
                        cb.BackColor = Color.Transparent;
                        cb.ForeColor = fg;
                        break;
                    case RadioButton rb:
                        rb.BackColor = Color.Transparent;
                        rb.ForeColor = fg;
                        break;
                    case TextBox tb:
                        tb.BackColor = bgCtrl;
                        tb.ForeColor = fg;
                        break;
                    case ComboBox cbo:
                        cbo.BackColor = bgCtrl;
                        cbo.ForeColor = fg;
                        break;
                    case NumericUpDown nud:
                        nud.BackColor = bgCtrl;
                        nud.ForeColor = fg;
                        break;
                    case Button btn:
                        btn.BackColor = bgCtrl;
                        btn.ForeColor = fg;
                        btn.FlatStyle = FlatStyle.Flat;
                        btn.FlatAppearance.BorderColor = border;
                        break;
                    case TabControl tc:
                        tc.BackColor = bg;
                        tc.ForeColor = fg;
                        break;
                    default:
                        c.BackColor = bg;
                        c.ForeColor = fg;
                        break;
                }
                foreach (Control child in c.Controls)
                    Style(child);
            }

            root.BackColor = bg;
            root.ForeColor = fg;
            foreach (Control c in root.Controls)
                Style(c);
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
                        ?? Path.Combine(AppContext.BaseDirectory, "TaskFolder.exe");
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

        private void BtnSetMouseButton_Click(object sender, EventArgs e)
        {
            using var dlg = new MouseButtonCaptureDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK && dlg.CapturedButton != null)
            {
                _capturedMouseButton = dlg.CapturedButton;
                _lblMouseButton.Text = "Button: " + MouseHotkeyService.FriendlyName(_capturedMouseButton);
                _chkMouseHotkeyEnabled.Checked = true;
            }
        }

        private void BtnPinTray_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Windows Settings will open to the Taskbar page.\n\n" +
                "Scroll down to 'Other system tray icons', find TaskFolder in the list, " +
                "and toggle it to On.\n\n" +
                "The icon will then always be visible on your taskbar.",
                "Pin TaskFolder to System Tray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "ms-settings:taskbar",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not open Windows Settings:\n{ex.Message}\n\n" +
                    "Go to Settings → Personalization → Taskbar → Other system tray icons manually.",
                    "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BtnUseOneDrive_Click(object sender, EventArgs e)
        {
            string oneDrivePath = OneDriveDetector.GetOneDrivePath();
            if (oneDrivePath == null)
            {
                MessageBox.Show(
                    "OneDrive does not appear to be installed or configured on this machine.",
                    "OneDrive Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string suggested = Path.Combine(oneDrivePath, "TaskFolder");
            _txtProfilesRoot.Text = suggested;

            MessageBox.Show(
                $"OneDrive path set to:\n{suggested}\n\nTaskFolder will create this folder if it does not exist. " +
                "Click OK in Settings to save, then restart TaskFolder.",
                "OneDrive Sync", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnUseDropbox_Click(object sender, EventArgs e)
        {
            string dropboxPath = DropboxDetector.GetDropboxPath();
            if (dropboxPath == null)
            {
                MessageBox.Show(
                    "Dropbox does not appear to be installed or configured on this machine.",
                    "Dropbox Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string suggested = Path.Combine(dropboxPath, "TaskFolder");
            _txtProfilesRoot.Text = suggested;

            MessageBox.Show(
                $"Dropbox path set to:\n{suggested}\n\nTaskFolder will create this folder if it does not exist. " +
                "Click OK in Settings to save, then restart TaskFolder.",
                "Dropbox Sync", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void BtnBrowseProfilesRoot_Click(object sender, EventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select root folder for shortcuts/profiles (e.g. your OneDrive folder)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = _txtProfilesRoot.Text.Trim()
            };
            if (dlg.ShowDialog() == DialogResult.OK)
                _txtProfilesRoot.Text = dlg.SelectedPath;
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

        private void BtnClearShortcuts_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show(
                    "Remove all shortcuts in the current profile? This cannot be undone.",
                    "Confirm Clear Shortcuts",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;
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
            SetAutoStart(_chkAutoStart.Checked);
            _settingsService.Settings.StartWithWindows = _chkAutoStart.Checked;
            _settingsService.Settings.ShowNotifications = _chkShowNotifications.Checked;
            _settingsService.Settings.CheckForUpdates = _chkCheckForUpdates.Checked;

            bool hotkeyEnabled = _chkHotkeyEnabled.Checked;
            string mods = _cboModifiers.SelectedItem?.ToString() ?? "Ctrl+Alt";
            string key = _cboKey.SelectedItem?.ToString() ?? "T";
            _settingsService.Settings.HotkeyEnabled = hotkeyEnabled;
            _settingsService.Settings.HotkeyModifiers = mods;
            _settingsService.Settings.HotkeyKey = key;

            string syncPath = _txtProfilesRoot.Text.Trim();
            _settingsService.Settings.ProfilesRootPath = string.IsNullOrEmpty(syncPath) ? null : syncPath;

            _settingsService.Settings.MouseHotkeyEnabled = _chkMouseHotkeyEnabled.Checked;
            _settingsService.Settings.MouseHotkeyButton = _capturedMouseButton;

            _settingsService.Settings.MenuFontSize = (float)_nudFontSize.Value;
            _settingsService.Settings.DarkMode = _chkDarkMode.Checked;
            _settingsService.Settings.MenuIconSize = _radIconLarge.Checked ? 32 : 16;

            var terms = _txtCleanupTerms.Text
                .Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            _settingsService.Settings.NameCleanupTerms =
                new System.Collections.Generic.List<string>(
                    Array.ConvertAll(terms, t => t.Trim()).Where(t => t.Length > 0));

            _settingsService.Save();

            _hotkeyService.Unregister();
            if (hotkeyEnabled)
                _hotkeyService.Register(mods, key);

            if (_mouseHotkeyService != null)
            {
                _mouseHotkeyService.Unregister();
                if (_chkMouseHotkeyEnabled.Checked)
                    _mouseHotkeyService.Register(_capturedMouseButton);
            }

            Close();
        }
    }
}
