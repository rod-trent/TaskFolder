using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;
using TaskFolder.Services;

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

        // Controls
        private CheckBox _chkAutoStart;
        private CheckBox _chkShowNotifications;

        private CheckBox _chkHotkeyEnabled;
        private ComboBox _cboModifiers;
        private ComboBox _cboKey;

        private Button _btnOpenFolder;
        private Button _btnClearShortcuts;

        private TextBox _txtProfilesRoot;
        private Button _btnBrowseProfilesRoot;
        private Button _btnClearProfilesRoot;
        private Button _btnUseDropbox;

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

            // ── Profile Sync ─────────────────────────────────────────────────────
            var grpSync = new GroupBox
            {
                Text = "Profile Sync (OneDrive / Dropbox / Network Share)",
                Location = new Point(X, y),
                Size = new Size(W, 95)
            };

            grpSync.Controls.Add(new Label
            {
                Text = "Root path (leave blank to use %APPDATA%\\TaskFolder):",
                Location = new Point(12, 20),
                AutoSize = true
            });

            _txtProfilesRoot = new TextBox
            {
                Location = new Point(12, 38),
                Width = W - 230,
                PlaceholderText = "e.g. C:\\Users\\You\\OneDrive\\TaskFolder"
            };
            grpSync.Controls.Add(_txtProfilesRoot);

            _btnUseDropbox = new Button
            {
                Text = "Use Dropbox",
                Location = new Point(W - 214, 36),
                Width = 90
            };
            _btnUseDropbox.Click += BtnUseDropbox_Click;
            grpSync.Controls.Add(_btnUseDropbox);

            _btnBrowseProfilesRoot = new Button
            {
                Text = "Browse...",
                Location = new Point(W - 120, 36),
                Width = 58
            };
            _btnBrowseProfilesRoot.Click += BtnBrowseProfilesRoot_Click;
            grpSync.Controls.Add(_btnBrowseProfilesRoot);

            _btnClearProfilesRoot = new Button
            {
                Text = "Clear",
                Location = new Point(W - 58, 36),
                Width = 46
            };
            _btnClearProfilesRoot.Click += (s, e) => _txtProfilesRoot.Clear();
            grpSync.Controls.Add(_btnClearProfilesRoot);

            grpSync.Controls.Add(new Label
            {
                Text = "Restart TaskFolder after changing this path for it to take effect.",
                Location = new Point(12, 68),
                Size = new Size(W - 24, 18),
                ForeColor = System.Drawing.Color.Gray,
                Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Italic)
            });

            Controls.Add(grpSync);
            y += grpSync.Height + 8;

            // ── Taskbar Visibility ───────────────────────────────────────────────
            var grpTray = new GroupBox
            {
                Text = "Taskbar Visibility",
                Location = new Point(X, y),
                Size = new Size(W, 80)
            };

            var btnPinTray = new Button
            {
                Text = "Pin to System Tray...",
                Location = new Point(12, 22),
                Size = new Size(160, 28)
            };
            btnPinTray.Click += BtnPinTray_Click;
            grpTray.Controls.Add(btnPinTray);

            grpTray.Controls.Add(new Label
            {
                Text = "Opens Windows Settings → Taskbar → Other system tray icons." + Environment.NewLine +
                       "Toggle 'TaskFolder' to On so the icon always shows on the taskbar.",
                Location = new Point(180, 18),
                Size = new Size(W - 192, 44),
                ForeColor = System.Drawing.Color.Gray
            });

            Controls.Add(grpTray);
            y += grpTray.Height + 8;

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
            _txtProfilesRoot.Text = _settingsService.Settings.ProfilesRootPath ?? string.Empty;

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

            // Suggest a TaskFolder subfolder inside Dropbox so it doesn't clutter the root
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

            string syncPath = _txtProfilesRoot.Text.Trim();
            _settingsService.Settings.ProfilesRootPath = string.IsNullOrEmpty(syncPath) ? null : syncPath;

            _settingsService.Save();

            // Apply hotkey change
            _hotkeyService.Unregister();
            if (hotkeyEnabled)
                _hotkeyService.Register(mods, key);

            Close();
        }
    }
}
