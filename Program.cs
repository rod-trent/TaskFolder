using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Views;
using TaskFolder.Utilities;

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
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
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
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // ── Services ────────────────────────────────────────────────────────────
        private SettingsService _settingsService;
        private ShortcutManager _shortcutManager;
        private ProfileService _profileService;
        private HotkeyService _hotkeyService;
        private MouseHotkeyService _mouseHotkeyService;

        // ── Tray UI ─────────────────────────────────────────────────────────────
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;

        // ── Menu state ──────────────────────────────────────────────────────────
        private ToolStripTextBox _searchBox;
        private string _searchFilter = string.Empty;
        private bool _itemContextMenuOpen = false;

        public TaskFolderApplicationContext(bool isPortable = false)
        {
            _settingsService = new SettingsService(isPortable);
            _shortcutManager = new ShortcutManager(_settingsService);
            _profileService = new ProfileService(_settingsService, _shortcutManager);
            _hotkeyService = new HotkeyService();
            _mouseHotkeyService = new MouseHotkeyService();

            _shortcutManager.ShortcutsChanged += OnShortcutsChanged;
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _mouseHotkeyService.MouseHotkeyPressed += OnHotkeyPressed;

            // Register keyboard hotkey if enabled
            if (_settingsService.Settings.HotkeyEnabled)
            {
                _hotkeyService.Register(
                    _settingsService.Settings.HotkeyModifiers,
                    _settingsService.Settings.HotkeyKey);
            }

            // Register mouse hotkey if enabled
            if (_settingsService.Settings.MouseHotkeyEnabled)
                _mouseHotkeyService.Register(_settingsService.Settings.MouseHotkeyButton);

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
            var settings = _settingsService.Settings;
            float fontSize = settings.MenuFontSize is >= 7f and <= 20f ? settings.MenuFontSize : 9f;

            var menu = new ContextMenuStrip
            {
                Font = new Font("Segoe UI", fontSize)
            };
            if (settings.DarkMode)
                menu.Renderer = new Views.DarkModeRenderer();

            // Keep the tray menu alive while a per-item right-click context menu is open.
            menu.Closing += (s, e) =>
            {
                if (_itemContextMenuOpen)
                    e.Cancel = true;
            };

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
            menu.Items.Add(new ToolStripMenuItem("Add Note...", null, OnAddNote));
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
                    (mi.Tag is ShortcutItem || mi.Tag is string s && (s == "category" || s == "stub") ||
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

            // Apply fuzzy search filter, sorted by score descending
            IEnumerable<ShortcutItem> visible = allShortcuts;
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                visible = allShortcuts
                    .Select(sc => (sc, score: FuzzySearch.Score(sc.GetDisplayName(), _searchFilter)))
                    .Where(t => t.score > 0)
                    .OrderByDescending(t => t.score)
                    .Select(t => t.sc);
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

            // ── Pinned section ─────────────────────────────────────────────────
            var pinned = allShortcuts.Where(sc => sc.IsPinned).ToList();
            if (pinned.Count > 0)
            {
                foreach (var sc in pinned)
                    _trayMenu.Items.Insert(insertAt++, BuildShortcutItem(sc));
                var pinSep = new ToolStripSeparator { Tag = "shortcuts-sep" };
                _trayMenu.Items.Insert(insertAt++, pinSep);
            }

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
                    : $"(No matches for \"{_searchFilter}\")";
                _trayMenu.Items.Insert(insertAt, new ToolStripMenuItem(emptyText)
                {
                    Tag = "stub",
                    Enabled = false,
                    Font = new Font(_trayMenu.Font, FontStyle.Italic),
                    ForeColor = Color.Gray
                });
            }
        }

        private string CleanDisplayName(string name)
        {
            foreach (var term in _settingsService.Settings.NameCleanupTerms ?? [])
                name = name.Replace(term, string.Empty, StringComparison.OrdinalIgnoreCase);
            return name.Trim();
        }

        private ToolStripMenuItem BuildShortcutItem(ShortcutItem shortcut)
        {
            string displayName = CleanDisplayName(shortcut.GetDisplayName());
            string pinnedPrefix = shortcut.IsPinned ? "📌 " : "";

            var menuItem = new ToolStripMenuItem(
                pinnedPrefix + displayName,
                shortcut.Icon?.ToBitmap())
            {
                Tag = shortcut,
                ToolTipText = BuildTooltip(shortcut)
            };

            // Left-click: launch
            menuItem.Click += (s, e) => LaunchShortcut(shortcut);

            // Right-click context menu
            var ctx = new ContextMenuStrip();

            // Header showing which shortcut this menu is for
            var header = new ToolStripLabel($"({displayName})")
            {
                Font = new Font(ctx.Font, FontStyle.Bold),
                ForeColor = SystemColors.HotTrack,
                Enabled = false
            };
            ctx.Items.Add(header);
            ctx.Items.Add(new ToolStripSeparator());

            // Pin / Unpin
            var pinItem = new ToolStripMenuItem(shortcut.IsPinned ? "Unpin from Top" : "Pin to Top");
            pinItem.Click += (s, e) => OnTogglePin(shortcut);
            ctx.Items.Add(pinItem);
            ctx.Items.Add(new ToolStripSeparator());

            if (!shortcut.IsNote)
            {
                var adminItem = new ToolStripMenuItem("Run as Administrator");
                adminItem.Click += (s, e) => LaunchAsAdmin(shortcut);
                ctx.Items.Add(adminItem);
                ctx.Items.Add(new ToolStripSeparator());
            }

            var renameItem = new ToolStripMenuItem("Rename...");
            renameItem.Click += (s, e) => OnRenameShortcut(shortcut);
            ctx.Items.Add(renameItem);

            var removeItem = new ToolStripMenuItem("Remove");
            removeItem.Click += (s, e) => OnRemoveShortcut(shortcut);
            ctx.Items.Add(removeItem);

            if (!shortcut.IsNote)
            {
                var openLocItem = new ToolStripMenuItem("Open File Location");
                openLocItem.Click += (s, e) => OnOpenFileLocation(shortcut);
                ctx.Items.Add(openLocItem);

                ctx.Items.Add(new ToolStripSeparator());

                var changeIconItem = new ToolStripMenuItem("Change Icon...");
                changeIconItem.Click += (s, e) => OnChangeIcon(shortcut);
                ctx.Items.Add(changeIconItem);

                ctx.Items.Add(new ToolStripSeparator());

                string scriptLabel = string.IsNullOrEmpty(shortcut.PreLaunchScript)
                    ? "Set Pre-Launch Script..."
                    : $"Pre-Launch Script: {shortcut.PreLaunchScript}";
                var scriptItem = new ToolStripMenuItem(scriptLabel);
                scriptItem.Click += (s, e) => OnSetPreLaunchScript(shortcut);
                ctx.Items.Add(scriptItem);
            }

            ctx.Items.Add(new ToolStripSeparator());

            var moveUpItem = new ToolStripMenuItem("Move Up");
            moveUpItem.Click += (s, e) => OnMoveShortcut(shortcut, -1);
            ctx.Items.Add(moveUpItem);

            var moveDownItem = new ToolStripMenuItem("Move Down");
            moveDownItem.Click += (s, e) => OnMoveShortcut(shortcut, +1);
            ctx.Items.Add(moveDownItem);

            menuItem.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    _itemContextMenuOpen = true;
                    ctx.Closed += (cs, ce) =>
                    {
                        _itemContextMenuOpen = false;
                        if (ce.CloseReason == ToolStripDropDownCloseReason.ItemClicked)
                            _trayMenu.Close();
                    };
                    ctx.Show(Cursor.Position);
                }
                else if (e.Button == MouseButtons.Middle && !shortcut.IsNote)
                    LaunchAsAdmin(shortcut);
            };

            return menuItem;
        }

        private static string BuildTooltip(ShortcutItem sc)
        {
            var parts = new System.Text.StringBuilder();
            parts.AppendLine(sc.IsNote ? "(Quick Note)" : sc.TargetPath);
            if (sc.LaunchCount > 0)
                parts.AppendLine($"Launched {sc.LaunchCount} time{(sc.LaunchCount == 1 ? "" : "s")}");
            if (sc.LastUsed.HasValue)
            {
                var ago = DateTime.Now - sc.LastUsed.Value;
                string agoStr = ago.TotalDays >= 1
                    ? $"{(int)ago.TotalDays}d ago"
                    : ago.TotalHours >= 1
                        ? $"{(int)ago.TotalHours}h ago"
                        : "just now";
                parts.Append($"Last used: {agoStr}");
            }
            if (!string.IsNullOrEmpty(sc.PreLaunchScript))
                parts.AppendLine($"\nPre-launch: {sc.PreLaunchScript}");
            return parts.ToString().TrimEnd();
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
                    _searchFilter = string.Empty;
                    if (_searchBox != null) _searchBox.Text = string.Empty;
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
            // Show the menu at the cursor so it appears on the same monitor the user is working on.
            ShowMenuAtCursor();
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

        /// <summary>
        /// Shows the menu at the current cursor position, snapping to keep it
        /// fully on the screen the cursor is on. Used for hotkey-triggered opens
        /// so the menu appears on the same monitor as the active work.
        ///
        /// A 1×1 invisible owner form is created on the target screen so the
        /// ContextMenuStrip receives proper focus and auto-closes when the user
        /// clicks elsewhere (without an owner, WM_ACTIVATE is never delivered
        /// and the menu stays open indefinitely).
        /// </summary>
        private void ShowMenuAtCursor()
        {
            var cursorPos = Cursor.Position;
            var screen = Screen.FromPoint(cursorPos);

            // Clamp so the menu fits on the screen (approximate 250×400 max size)
            int x = Math.Min(cursorPos.X, screen.WorkingArea.Right - 260);
            int y = Math.Min(cursorPos.Y, screen.WorkingArea.Bottom - 420);

            // Invisible 1×1 owner form positioned on the correct monitor.
            // It activates the app so the menu receives focus and click-away events.
            var owner = new Form
            {
                Size = new Size(1, 1),
                StartPosition = FormStartPosition.Manual, // must be Manual or Location is ignored
                Location = new Point(x, y),
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false,
                Opacity = 0
            };
            owner.Show();
            SetForegroundWindow(owner.Handle);

            // Dispose the owner form once the menu closes.
            ToolStripDropDownClosedEventHandler onClosed = null;
            onClosed = (s, e) =>
            {
                _trayMenu.Closed -= onClosed;
                owner.Dispose();
            };
            _trayMenu.Closed += onClosed;

            _trayMenu.Show(owner, new Point(0, 0));
        }

        private void LaunchShortcut(ShortcutItem shortcut)
        {
            try
            {
                // Quick Note — open sticky-note editor
                if (shortcut.IsNote)
                {
                    var noteForm = new StickyNoteForm(
                        shortcut.ShortcutFilePath,
                        shortcut.GetDisplayName(),
                        shortcut.NoteContent ?? string.Empty);
                    noteForm.FormClosed += (s, e) =>
                    {
                        // Reload note content after editing
                        if (File.Exists(shortcut.ShortcutFilePath))
                            shortcut.NoteContent = File.ReadAllText(shortcut.ShortcutFilePath);
                    };
                    noteForm.Show();
                    return;
                }

                // Pre-launch script
                if (!string.IsNullOrWhiteSpace(shortcut.PreLaunchScript))
                    RunPreLaunchScript(shortcut.PreLaunchScript);

                string ext = Path.GetExtension(shortcut.ShortcutFilePath ?? "").ToLowerInvariant();

                if (ext == ".url")
                {
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

        private void LaunchAsAdmin(ShortcutItem shortcut)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(shortcut.PreLaunchScript))
                    RunPreLaunchScript(shortcut.PreLaunchScript);

                string target = File.Exists(shortcut.ShortcutFilePath ?? "")
                    ? shortcut.ShortcutFilePath
                    : shortcut.TargetPath;

                var si = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = target,
                    Verb = "runas",
                    UseShellExecute = true
                };
                if (!string.IsNullOrEmpty(shortcut.Arguments))
                    si.Arguments = shortcut.Arguments;
                if (!string.IsNullOrEmpty(shortcut.WorkingDirectory))
                    si.WorkingDirectory = shortcut.WorkingDirectory;

                System.Diagnostics.Process.Start(si);
                _shortcutManager.RecordLaunch(shortcut);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to launch as administrator:\n{ex.Message}",
                    "TaskFolder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void RunPreLaunchScript(string command)
        {
            try
            {
                var si = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{command}\"",
                    UseShellExecute = true,
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized
                };
                var proc = System.Diagnostics.Process.Start(si);
                proc?.WaitForExit(30_000); // max 30 s
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Pre-launch script failed: {ex.Message}");
            }
        }

        private void OnAddNote(object sender, EventArgs e)
        {
            using var dlg = new InputDialog("Add Note", "Note title:", string.Empty);
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                _shortcutManager.AddNote(dlg.Value);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create note:\n{ex.Message}",
                    "TaskFolder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnTogglePin(ShortcutItem shortcut)
        {
            _shortcutManager.SetPinned(shortcut, !shortcut.IsPinned);
            RefreshShortcutsList();
        }

        private void OnSetPreLaunchScript(ShortcutItem shortcut)
        {
            using var dlg = new InputDialog(
                "Pre-Launch Script",
                "Command to run before launching (leave blank to clear):",
                shortcut.PreLaunchScript ?? string.Empty,
                allowEmpty: true);

            if (dlg.ShowDialog() != DialogResult.OK) return;
            _shortcutManager.SetPreLaunchScript(shortcut, dlg.Value);
            RefreshShortcutsList();
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
            using var form = new SettingsForm(_shortcutManager, _settingsService, _hotkeyService, _mouseHotkeyService);
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
            _mouseHotkeyService?.Dispose();
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
                _mouseHotkeyService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
