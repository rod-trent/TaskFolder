using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Views;

namespace TaskFolder
{
    /// <summary>
    /// Main application entry point for TaskFolder
    /// </summary>
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool isPortable = Array.Exists(args, a =>
                    a.Equals("--portable", StringComparison.OrdinalIgnoreCase));

                bool createdNew;
                using (var mutex = new System.Threading.Mutex(true, "TaskFolder_SingleInstance", out createdNew))
                {
                    if (!createdNew)
                    {
                        MessageBox.Show("TaskFolder is already running. Check your system tray.",
                            "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    Application.Run(new TaskFolderApplicationContext(isPortable));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fatal error starting TaskFolder:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "TaskFolder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>
    /// Application context that manages the system tray icon and core services.
    /// </summary>
    public class TaskFolderApplicationContext : ApplicationContext
    {
        // ── Services ────────────────────────────────────────────────────────────
        private SettingsService _settingsService;
        private ShortcutManager _shortcutManager;
        private ProfileService _profileService;
        private HotkeyService _hotkeyService;

        // ── Tray UI ─────────────────────────────────────────────────────────────
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;

        // ── Menu state ──────────────────────────────────────────────────────────
        private ToolStripTextBox _searchBox;
        private string _searchFilter = string.Empty;

        public TaskFolderApplicationContext(bool isPortable = false)
        {
            _settingsService = new SettingsService(isPortable);
            _shortcutManager = new ShortcutManager(_settingsService);
            _profileService = new ProfileService(_settingsService, _shortcutManager);
            _hotkeyService = new HotkeyService();

            _shortcutManager.ShortcutsChanged += OnShortcutsChanged;
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;

            // Register hotkey if enabled
            if (_settingsService.Settings.HotkeyEnabled)
            {
                _hotkeyService.Register(
                    _settingsService.Settings.HotkeyModifiers,
                    _settingsService.Settings.HotkeyKey);
            }

            InitializeTrayIcon();
            // Force handle creation so InvokeRequired/BeginInvoke work from background threads.
            _ = _trayMenu.Handle;
        }

        // ── Tray icon ───────────────────────────────────────────────────────────

        private void InitializeTrayIcon()
        {
            _trayMenu = BuildTrayMenu();

            _trayIcon = new NotifyIcon
            {
                Icon = GetApplicationIcon(),
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            UpdateTrayTooltip();

            _trayIcon.Click += OnTrayIconClick;
            _trayIcon.DoubleClick += OnTrayIconDoubleClick;
        }

        private Icon GetApplicationIcon()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "TaskFolder.ico");
                if (File.Exists(iconPath))
                    return new Icon(iconPath);

                var exePath = Environment.ProcessPath
                    ?? Path.Combine(AppContext.BaseDirectory, "TaskFolder.exe");
                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null) return icon;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load app icon: {ex.Message}");
            }
            return SystemIcons.Application;
        }

        private void UpdateTrayTooltip()
        {
            var shortcuts = _shortcutManager.GetAllShortcuts();
            int count = shortcuts.Count;
            string profile = _settingsService.Settings.ActiveProfileName;
            string tip = $"TaskFolder — {count} shortcut{(count == 1 ? "" : "s")}";
            if (!profile.Equals("Default", StringComparison.OrdinalIgnoreCase))
                tip += $" [{profile}]";
            // NotifyIcon.Text max is 63 chars
            _trayIcon.Text = tip.Length > 63 ? tip[..63] : tip;
        }

        // ── Menu construction ───────────────────────────────────────────────────

        private ContextMenuStrip BuildTrayMenu()
        {
            var menu = new ContextMenuStrip();

            var header = new ToolStripLabel("Applications")
            {
                Font = new Font(menu.Font, FontStyle.Bold),
                ForeColor = Color.DarkBlue
            };
            menu.Items.Add(header);
            menu.Items.Add(new ToolStripSeparator());

            // Search box (persists across refreshes)
            _searchBox = new ToolStripTextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Width = 180
            };
            _searchBox.TextBox.PlaceholderText = "Search shortcuts...";
            _searchBox.TextChanged += (s, e) =>
            {
                _searchFilter = _searchBox.Text;
                RefreshShortcutsList();
                // Re-focus the search box after the rebuild
                _searchBox.Focus();
            };
            menu.Items.Add(_searchBox);
            menu.Items.Add(new ToolStripSeparator());

            // _trayMenu must be set before RefreshShortcutsList() is called,
            // because that method references _trayMenu directly.
            _trayMenu = menu;
            RefreshShortcutsList();

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Add Application...", null, OnAddApplication));
            menu.Items.Add(new ToolStripMenuItem("Open Shortcuts Folder", null, OnOpenFolder));

            // Switch Profile submenu
            var profileMenu = new ToolStripMenuItem("Switch Profile");
            menu.Items.Add(profileMenu);
            menu.Opening += (s, e) => RebuildProfileSubmenu(profileMenu);

            menu.Items.Add(new ToolStripMenuItem("Settings...", null, OnSettings));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit TaskFolder", null, OnExit));

            return menu;
        }

        // ── Shortcut list refresh ───────────────────────────────────────────────

        /// <summary>
        /// The index in _trayMenu.Items where shortcut rows start.
        /// Layout: [0] header, [1] sep, [2] searchBox, [3] sep, [SHORTCUTS...], [sep], [Add], [Open], [Profile], [Settings], [sep], [Exit]
        /// </summary>
        private const int ShortcutsInsertIndex = 4;

        private void RefreshShortcutsList()
        {
            // Remove old shortcut rows (tagged with ShortcutItem or the "(No applications)" stub)
            for (int i = _trayMenu.Items.Count - 1; i >= ShortcutsInsertIndex; i--)
            {
                var item = _trayMenu.Items[i];
                if (item is ToolStripMenuItem mi &&
                    (mi.Tag is ShortcutItem || mi.Tag is string s && s == "category" ||
                     mi.Text == "(No applications added)"))
                {
                    _trayMenu.Items.RemoveAt(i);
                }
                else if (item is ToolStripSeparator sep && sep.Tag is string sepTag && sepTag == "shortcuts-sep")
                {
                    _trayMenu.Items.RemoveAt(i);
                }
            }

            var allShortcuts = _shortcutManager.GetAllShortcuts();

            // Apply search filter
            IEnumerable<ShortcutItem> visible = allShortcuts;
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                visible = allShortcuts.Where(sc =>
                    sc.GetDisplayName().Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));
            }

            // ── Most Used section ──────────────────────────────────────────────
            var mostUsed = allShortcuts
                .Where(sc => sc.LaunchCount > 0)
                .OrderByDescending(sc => sc.LaunchCount)
                .Take(5)
                .ToList();

            // ── Recently Used section ──────────────────────────────────────────
            var recentlyUsed = allShortcuts
                .Where(sc => sc.LastUsed.HasValue)
                .OrderByDescending(sc => sc.LastUsed)
                .Take(5)
                .ToList();

            int insertAt = ShortcutsInsertIndex;

            // Insert Most Used submenu
            if (mostUsed.Count > 0)
            {
                var muMenu = new ToolStripMenuItem("Most Used") { Tag = "category" };
                foreach (var sc in mostUsed)
                    muMenu.DropDownItems.Add(BuildShortcutItem(sc));
                _trayMenu.Items.Insert(insertAt++, muMenu);
            }

            // Insert Recently Used submenu
            if (recentlyUsed.Count > 0)
            {
                var ruMenu = new ToolStripMenuItem("Recently Used") { Tag = "category" };
                foreach (var sc in recentlyUsed)
                    ruMenu.DropDownItems.Add(BuildShortcutItem(sc));
                _trayMenu.Items.Insert(insertAt++, ruMenu);
            }

            if ((mostUsed.Count > 0 || recentlyUsed.Count > 0) && visible.Any())
            {
                var divSep = new ToolStripSeparator { Tag = "shortcuts-sep" };
                _trayMenu.Items.Insert(insertAt++, divSep);
            }

            // ── Main shortcut list (grouped by category) ──────────────────────
            var grouped = visible
                .GroupBy(sc => sc.Category ?? string.Empty)
                .OrderBy(g => g.Key);

            bool anyAdded = false;
            foreach (var group in grouped)
            {
                if (string.IsNullOrEmpty(group.Key))
                {
                    // Root-level shortcuts
                    foreach (var sc in group)
                    {
                        _trayMenu.Items.Insert(insertAt++, BuildShortcutItem(sc));
                        anyAdded = true;
                    }
                }
                else
                {
                    // Category submenu
                    var catMenu = new ToolStripMenuItem(group.Key) { Tag = "category" };
                    foreach (var sc in group)
                        catMenu.DropDownItems.Add(BuildShortcutItem(sc));
                    _trayMenu.Items.Insert(insertAt++, catMenu);
                    anyAdded = true;
                }
            }

            if (!anyAdded)
            {
                string emptyText = string.IsNullOrWhiteSpace(_searchFilter)
                    ? "(No applications added)"
                    : "(No matches)";
                _trayMenu.Items.Insert(insertAt, new ToolStripMenuItem(emptyText)
                {
                    Enabled = false,
                    Font = new Font(_trayMenu.Font, FontStyle.Italic),
                    ForeColor = Color.Gray
                });
            }
        }

        private ToolStripMenuItem BuildShortcutItem(ShortcutItem shortcut)
        {
            var menuItem = new ToolStripMenuItem(
                shortcut.GetDisplayName(),
                shortcut.Icon?.ToBitmap())
            {
                Tag = shortcut,
                ToolTipText = shortcut.TargetPath
            };

            // Left-click: launch
            menuItem.Click += (s, e) => LaunchShortcut(shortcut);

            // Right-click context menu: Rename, Remove, Open Location, Change Icon, Move Up/Down
            var ctx = new ContextMenuStrip();

            var renameItem = new ToolStripMenuItem("Rename...");
            renameItem.Click += (s, e) => OnRenameShortcut(shortcut);

            var removeItem = new ToolStripMenuItem("Remove");
            removeItem.Click += (s, e) => OnRemoveShortcut(shortcut);

            var openLocItem = new ToolStripMenuItem("Open File Location");
            openLocItem.Click += (s, e) => OnOpenFileLocation(shortcut);

            var changeIconItem = new ToolStripMenuItem("Change Icon...");
            changeIconItem.Click += (s, e) => OnChangeIcon(shortcut);

            var moveUpItem = new ToolStripMenuItem("Move Up");
            moveUpItem.Click += (s, e) => OnMoveShortcut(shortcut, -1);

            var moveDownItem = new ToolStripMenuItem("Move Down");
            moveDownItem.Click += (s, e) => OnMoveShortcut(shortcut, +1);

            ctx.Items.AddRange(new ToolStripItem[]
            {
                renameItem, removeItem, openLocItem,
                new ToolStripSeparator(),
                changeIconItem,
                new ToolStripSeparator(),
                moveUpItem, moveDownItem
            });

            menuItem.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    ctx.Show(Cursor.Position);
            };

            return menuItem;
        }

        // ── Profile submenu ─────────────────────────────────────────────────────

        private void RebuildProfileSubmenu(ToolStripMenuItem profileMenu)
        {
            profileMenu.DropDownItems.Clear();
            string active = _settingsService.Settings.ActiveProfileName;

            foreach (string name in _profileService.GetProfileNames())
            {
                var item = new ToolStripMenuItem(name)
                {
                    Checked = name.Equals(active, StringComparison.OrdinalIgnoreCase)
                };
                string captured = name;
                item.Click += (s, e) =>
                {
                    _profileService.SwitchProfile(captured);
                    UpdateTrayTooltip();
                    RefreshShortcutsList();
                };
                profileMenu.DropDownItems.Add(item);
            }

            profileMenu.DropDownItems.Add(new ToolStripSeparator());

            var newProfile = new ToolStripMenuItem("New Profile...");
            newProfile.Click += (s, e) =>
            {
                using var dlg = new RenameDialog("New Profile");
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        _profileService.CreateProfile(dlg.NewName);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message, "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            };
            profileMenu.DropDownItems.Add(newProfile);
        }

        // ── Event handlers ──────────────────────────────────────────────────────

        private void OnShortcutsChanged(object sender, EventArgs e)
        {
            if (_trayMenu.InvokeRequired)
            {
                _trayMenu.BeginInvoke(new Action(() => OnShortcutsChanged(sender, e)));
                return;
            }

            try
            {
                _shortcutManager.ReloadShortcuts();
                RefreshShortcutsList();
                UpdateTrayTooltip();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing shortcuts: {ex.Message}");
            }
        }

        private void OnHotkeyPressed(object sender, EventArgs e)
        {
            ShowContextMenu();
        }

        private void OnTrayIconClick(object sender, EventArgs e)
        {
            if (e is MouseEventArgs me && me.Button == MouseButtons.Left)
                ShowContextMenu();
        }

        private void OnTrayIconDoubleClick(object sender, EventArgs e)
        {
            OnOpenFolder(sender, e);
        }

        private void ShowContextMenu()
        {
            var method = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            method?.Invoke(_trayIcon, null);
        }

        private void LaunchShortcut(ShortcutItem shortcut)
        {
            try
            {
                string ext = Path.GetExtension(shortcut.ShortcutFilePath ?? "").ToLowerInvariant();

                if (ext == ".url")
                {
                    // Launch URL in default browser
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = shortcut.TargetPath,
                        UseShellExecute = true
                    });
                }
                else if (!string.IsNullOrEmpty(shortcut.ShortcutFilePath) &&
                         File.Exists(shortcut.ShortcutFilePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = shortcut.ShortcutFilePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    var si = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = shortcut.TargetPath,
                        UseShellExecute = true
                    };
                    if (!string.IsNullOrEmpty(shortcut.Arguments))
                        si.Arguments = shortcut.Arguments;
                    if (!string.IsNullOrEmpty(shortcut.WorkingDirectory))
                        si.WorkingDirectory = shortcut.WorkingDirectory;
                    System.Diagnostics.Process.Start(si);
                }

                _shortcutManager.RecordLaunch(shortcut);

                if (_settingsService.Settings.ShowNotifications)
                {
                    _trayIcon.ShowBalloonTip(2000, "TaskFolder",
                        $"Launched {shortcut.GetDisplayName()}", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch application:\n{ex.Message}",
                    "TaskFolder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnAddApplication(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Filter = "Applications (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|All Files (*.*)|*.*",
                Title = "Select Application to Add"
            };

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _shortcutManager.AddShortcut(dlg.FileName);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to add application:\n{ex.Message}",
                        "TaskFolder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
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
                MessageBox.Show($"Failed to open shortcuts folder:\n{ex.Message}",
                    "TaskFolder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSettings(object sender, EventArgs e)
        {
            using var form = new SettingsForm(_shortcutManager, _settingsService, _hotkeyService);
            form.ShowDialog();
            // Re-apply tooltip in case profile changed
            UpdateTrayTooltip();
        }

        private void OnRenameShortcut(ShortcutItem shortcut)
        {
            using var dlg = new RenameDialog(shortcut.GetDisplayName());
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                bool ok = _shortcutManager.RenameShortcut(shortcut, dlg.NewName);
                if (!ok)
                    MessageBox.Show("Rename failed.", "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnRemoveShortcut(ShortcutItem shortcut)
        {
            var result = MessageBox.Show(
                $"Remove '{shortcut.GetDisplayName()}' from TaskFolder?",
                "Remove Shortcut",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
                _shortcutManager.RemoveShortcut(shortcut);
        }

        private void OnOpenFileLocation(ShortcutItem shortcut)
        {
            try
            {
                string path = shortcut.ShortcutFilePath ?? shortcut.TargetPath;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open location:\n{ex.Message}",
                    "TaskFolder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnChangeIcon(ShortcutItem shortcut)
        {
            var meta = _settingsService.GetMetadata(Path.GetFileName(shortcut.ShortcutFilePath));
            using var dlg = new IconPickerDialog(meta.CustomIconPath ?? "", meta.CustomIconIndex);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _shortcutManager.SetCustomIcon(shortcut, dlg.SelectedIconPath, dlg.SelectedIconIndex);
                RefreshShortcutsList();
            }
        }

        private void OnMoveShortcut(ShortcutItem shortcut, int direction)
        {
            var all = _shortcutManager.GetAllShortcuts();

            // Only move within the same category
            var sameGroup = all
                .Where(sc => string.Equals(sc.Category, shortcut.Category, StringComparison.OrdinalIgnoreCase)
                          || (sc.Category == null && shortcut.Category == null))
                .OrderBy(sc => sc.SortOrder)
                .ThenBy(sc => sc.GetDisplayName(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            int idx = sameGroup.IndexOf(shortcut);
            int swapIdx = idx + direction;
            if (idx < 0 || swapIdx < 0 || swapIdx >= sameGroup.Count) return;

            // Assign sequential sort orders to the whole group, then swap
            for (int i = 0; i < sameGroup.Count; i++)
                sameGroup[i].SortOrder = i * 10;

            int tmp = sameGroup[idx].SortOrder;
            sameGroup[idx].SortOrder = sameGroup[swapIdx].SortOrder;
            sameGroup[swapIdx].SortOrder = tmp;

            _shortcutManager.UpdateSortOrder(sameGroup[idx], sameGroup[idx].SortOrder);
            _shortcutManager.UpdateSortOrder(sameGroup[swapIdx], sameGroup[swapIdx].SortOrder);

            RefreshShortcutsList();
        }

        private void OnExit(object sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _shortcutManager?.Dispose();
            _hotkeyService?.Dispose();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _trayIcon?.Dispose();
                _trayMenu?.Dispose();
                _shortcutManager?.Dispose();
                _hotkeyService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
