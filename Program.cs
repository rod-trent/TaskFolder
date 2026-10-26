using L = TaskFolder.Services.LocalizationService;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Timer = System.Windows.Forms.Timer;
using System.Windows.Forms;
using Microsoft.Win32;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;
using TaskFolder.Views;

namespace TaskFolder
{
    /// <summary>
    /// Main application entry point for TaskFolder
    /// </summary>
    static class Program
    {
        // THESIS: A crisp Windows 11 launcher surface replaces legacy Win32 chrome without changing the launch workflow.
        // OWN-WORLD: Fluent neutral surfaces, Segoe UI Variable, soft 8px corners, precise one-pixel dividers, and blue focus.
        // STORY: Open the tray, scan or search with confidence, launch in one click, and retain full nested-menu familiarity.
        // FIRST VIEWPORT: A compact rounded menu opens with an inset search field, readable shortcut rows, and quiet utility actions.
        // FORM: Native Windows 11 Fluent utility menu, pinned by the user brief; no concept seed required.
        // FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Marshal.ThrowExceptionForHR(WindowsShellLink.SetCurrentProcessExplicitAppUserModelID(
                    WindowsIntegrationService.AppUserModelId));

                bool isPortable = Array.Exists(args, a =>
                    a.Equals("--portable", StringComparison.OrdinalIgnoreCase));
                bool isStartup = Array.Exists(args, a =>
                    a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
                bool showSettings = Array.Exists(args, a =>
                    a.Equals("--settings", StringComparison.OrdinalIgnoreCase));
                bool showMenu = Array.Exists(args, a =>
                    a.Equals("--menu", StringComparison.OrdinalIgnoreCase));

                bool createdNew;
                using (var activation = new EventWaitHandle(false, EventResetMode.AutoReset,
                    WindowsIntegrationService.ActivationEventName))
                using (var menuActivation = new EventWaitHandle(false, EventResetMode.AutoReset,
                    WindowsIntegrationService.MenuActivationEventName))
                using (var mutex = new Mutex(true, WindowsIntegrationService.InstanceMutexName, out createdNew))
                {
                    if (!createdNew)
                    {
                        if (!isStartup)
                        {
                            WindowsShellLink.AllowExistingInstanceActivation();
                            if (showSettings) activation.Set();
                            else menuActivation.Set();
                        }
                        return;
                    }

                    var integration = new WindowsIntegrationService(isPortable);
                    if (!isStartup)
                    {
                        try { integration.EnsureStartMenuShortcut(); }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Start menu registration: {ex.Message}"); }
                    }
                    using var context = new TaskFolderApplicationContext(isPortable, activation,
                        showSettings && !isStartup, menuActivation, showMenu && !isStartup && !showSettings);
                    Application.Run(context);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.Text("Fatal error starting TaskFolder:\n\n{0}", ex.Message),
                    L.Text("TaskFolder Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        private ShortcutLaunchService _shortcutLaunch;
        private RecoveryForm _recoveryForm;
        private ShortcutManagerForm _managerForm;
        private ProfileService _profileService;
        private HotkeyService _hotkeyService;
        private RegisteredWaitHandle _activationWait;
        private RegisteredWaitHandle _menuActivationWait;
        private SettingsForm _settingsForm;
        private TaskbarWidgetForm _widget;
        private WidgetMenuOwnerForm _widgetMenuOwner;
        private readonly Timer _widgetWatch = new Timer { Interval = 750 };
        private readonly WidgetRecoveryState _widgetRecovery = new WidgetRecoveryState();
        private bool _ensuringWidget;
        private int _environmentNotificationPending;
        private string _lastWidgetHostMessage;
        private bool _exiting;
        private bool _settingsOpening;

        // ── Tray UI ─────────────────────────────────────────────────────────────
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private Image _headerLogoImage;

        // ── Menu state ──────────────────────────────────────────────────────────
        private Windows11SearchBox _searchBox;
        private readonly Windows11MenuRenderer _menuRenderer = new Windows11MenuRenderer();
        private string _searchFilter = string.Empty;
        private readonly SearchSelection _searchSelection = new SearchSelection();
        private int _shortcutsInsertIndex;
        private ToolStripMenuItem _profileMenu;
        private LauncherMenuSnapshot _renderedMenuSnapshot;
        private Dictionary<Guid, int> _renderedPinPositions = new Dictionary<Guid, int>();

        public TaskFolderApplicationContext(bool isPortable = false,
            EventWaitHandle activation = null, bool showSettings = false,
            EventWaitHandle menuActivation = null, bool showMenu = false)
        {
            _settingsService = new SettingsService(isPortable);
            UiThemeService.Configure(_settingsService.Settings);
            _settingsService.SettingsChanged += (_, _) => UiThemeService.Configure(_settingsService.Settings);
            UiThemeService.Changed += OnPresentationChanged;
            if (_settingsService.StateLoad.RecoveredFromBackup)
                MessageBox.Show(L.Text("TaskFolder loaded the last-good saved data in read-only mode.\n") +
                    "The original state file has been preserved and needs recovery before changes can be saved.",
                    "TaskFolder — Recovery required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _shortcutManager = new ShortcutManager(_settingsService);
            _shortcutLaunch = new ShortcutLaunchService(_shortcutManager.RecordLaunch);
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
            _widgetMenuOwner = new WidgetMenuOwnerForm();
            _widgetWatch.Tick += (_, _) => EnsureWidgetHost();
            SystemEvents.SessionSwitch += OnWidgetSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnWidgetDisplayChanged;
            ApplyTaskbarSettings();
            if (menuActivation != null)
            {
                _menuActivationWait = ThreadPool.RegisterWaitForSingleObject(menuActivation, (_, _) =>
                {
                    try { _trayMenu.BeginInvoke(new Action(OpenFromTaskbar)); }
                    catch (InvalidOperationException) { /* Application is shutting down. */ }
                }, null, Timeout.Infinite, executeOnlyOnce: false);
            }
            if (activation != null)
            {
                _activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (_, _) =>
                {
                    try
                    {
                        _trayMenu.BeginInvoke(new Action(() => OnSettings(this, EventArgs.Empty)));
                    }
                    catch (InvalidOperationException) { /* Application is shutting down. */ }
                }, null, Timeout.Infinite, executeOnlyOnce: false);
            }
            if (showSettings)
                _trayMenu.BeginInvoke(new Action(() => OnSettings(this, EventArgs.Empty)));
            else if (showMenu)
                _trayMenu.BeginInvoke(new Action(OpenFromTaskbar));
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
            string tip = "TaskFolder — " + L.Plural("ShortcutCount_", count, _settingsService.Settings.Language);
            if (!profile.Equals("Default", StringComparison.OrdinalIgnoreCase))
                tip += $" [{profile}]";
            // NotifyIcon.Text max is 63 chars
            _trayIcon.Text = tip.Length > 63 ? tip[..63] : tip;
        }

        // ── Menu construction ───────────────────────────────────────────────────

        private ContextMenuStrip BuildTrayMenu()
        {
            float fontSize = Math.Clamp(_settingsService.Settings.MenuFontSize, 9f, 18f);
            var menu = new Windows11ContextMenuStrip(
                _menuRenderer,
                Windows11MenuStyle.CreateFont(fontSize));
            // Make the menu's actual monitor DPI available to all size helpers while it is built.
            _trayMenu = menu;

            int logoSize = Math.Clamp(GetMenuItemHeight(), ScaleForMenu(32), ScaleForMenu(64));
            _headerLogoImage = MenuLogoLoader.LoadScaledBitmap(
                _settingsService.Settings.MenuHeaderLogoPath,
                logoSize);

            if (_headerLogoImage != null)
            {
                var header = new ToolStripLabel
                {
                    Image = _headerLogoImage,
                    ImageAlign = ContentAlignment.MiddleLeft,
                    ImageScaling = ToolStripItemImageScaling.None,
                    Padding = new Padding(4, 3, 4, 3)
                };
                menu.Items.Add(header);
                menu.Items.Add(new ToolStripSeparator());
            }

            _profileMenu = CreateMenuItem(LauncherMenuBuilder.ProfileText(_shortcutManager.CaptureMenuSnapshot(), _settingsService.Settings.Language));
            menu.Items.Add(_profileMenu);
            menu.Opening += (_, _) =>
            {
                RefreshShortcutsList();
                RebuildProfileSubmenu(_profileMenu);
            };

            // Search box (persists across refreshes)
            _searchBox = new Windows11SearchBox(menu.Font)
            {
                Size = new Size(ScaleForMenu(264), ScaleForMenu(38))
            };
            _searchBox.SearchTextChanged += (s, e) =>
            {
                _searchFilter = _searchBox.SearchText;
                RefreshShortcutsList();
                _searchBox.FocusEditor();
            };
            _searchBox.SearchSubmitted += (s, e) => LaunchSelectedSearchMatch();
            _searchBox.SearchNavigation += direction => { _searchSelection.Move(direction); ApplySearchSelection(); };
            _searchBox.EscapeRequested += (_, _) => _trayMenu.Close(ToolStripDropDownCloseReason.Keyboard);
            var searchHost = new ToolStripControlHost(_searchBox)
            {
                AutoSize = false,
                Size = _searchBox.Size,
                Margin = new Padding(8, 6, 8, 6),
                Padding = Padding.Empty
            };
            menu.Items.Add(searchHost);
            menu.Items.Add(new ToolStripSeparator());
            _shortcutsInsertIndex = menu.Items.Count;

            RefreshShortcutsList();

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(BuildPowerMenu(
                (text, click) => CreateMenuItem(text, null, click), OnWindowsPowerAction));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateMenuItem(L.Text("Add Application..."), null, OnAddApplication));
            menu.Items.Add(CreateMenuItem(L.Text("Open Shortcuts Folder"), null, OnOpenFolder));
            menu.Items.Add(CreateMenuItem(LocalizationService.Get("Menu_Manager", _settingsService.Settings.Language), null, OnManageShortcuts));
            menu.Items.Add(CreateMenuItem(L.Text("Recovery..."), null, OnRecovery));

            menu.Items.Add(CreateMenuItem(L.Text("Settings..."), null, OnSettings));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(CreateMenuItem(L.Text("Exit TaskFolder"), null, OnExit));

            menu.Opened += (s, e) => menu.BeginInvoke(new Action(
                () => _searchBox.SelectAllAndFocus()));
            menu.Opened += (_, _) => _widget?.SetMenuOpen(true);
            menu.Closed += (_, _) =>
            {
                _widget?.SetMenuOpen(false);
                _widgetMenuOwner?.Hide();
            };

            return menu;
        }

        internal static ToolStripMenuItem BuildPowerMenu(
            Func<string, EventHandler, ToolStripMenuItem> createItem,
            Action<WindowsPowerAction> execute)
        {
            var menu = createItem(L.Text("Power"), null);
            foreach (var entry in new[]
            {
                (L.Text("Sign Out"), WindowsPowerAction.SignOut),
                (L.Text("Sleep"), WindowsPowerAction.Sleep),
                (L.Text("Hibernate"), WindowsPowerAction.Hibernate),
                (L.Text("Shut Down"), WindowsPowerAction.ShutDown),
                (L.Text("Restart"), WindowsPowerAction.Restart)
            })
            {
                menu.DropDownItems.Add(createItem(entry.Item1, (_, _) => execute(entry.Item2)));
            }
            return menu;
        }

        private void OnWindowsPowerAction(WindowsPowerAction action)
        {
            // Let WinForms close the whole menu tree before Windows suspends or ends the session.
            _trayMenu.BeginInvoke(new Action(() =>
            {
                try
                {
                    new WindowsPowerService().Execute(action);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(L.Text("Unable to complete this action. It may be unavailable in Windows power settings or blocked by system policy.\n\n{0}", ex.Message),
                        L.Text("TaskFolder — Power"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }));
        }

        // ── Shortcut list refresh ───────────────────────────────────────────────

        /// <summary>
        /// The index in _trayMenu.Items where shortcut rows start. It is calculated
        /// after the optional logo header and the search row are added.
        /// </summary>
        private void RefreshShortcutsList()
        {
            // Remove and dispose every dynamic shortcut/category/empty row from the previous result set.
            for (int i = _trayMenu.Items.Count - 1; i >= _shortcutsInsertIndex; i--)
            {
                var item = _trayMenu.Items[i];
                if (item is ToolStripMenuItem mi &&
                    (mi.Tag is ShortcutItem || mi.Tag is string s && s == "category" ||
                     mi.Tag is string emptyTag && emptyTag == "empty"))
                {
                    _trayMenu.Items.RemoveAt(i);
                    DisposeMenuTreeItem(mi);
                }
                else if (item is ToolStripSeparator sep && sep.Tag is string sepTag && sepTag == "shortcuts-sep")
                {
                    _trayMenu.Items.RemoveAt(i);
                    sep.Dispose();
                }
            }

            var snapshot = _shortcutManager.CaptureMenuSnapshot();
            _renderedMenuSnapshot = snapshot;
            _renderedPinPositions = snapshot.Pins.Select((item, index) => (item.Id, Index: index)).ToDictionary(pair => pair.Id, pair => pair.Index);
            var allShortcuts = snapshot.AllShortcuts.ToList();
            if (_profileMenu != null)
            {
                _profileMenu.Text = LauncherMenuBuilder.ProfileText(snapshot, _settingsService.Settings.Language);
                _profileMenu.AccessibleName = string.Format(LocalizationService.Get("Menu_Profile", _settingsService.Settings.Language), snapshot.ProfileName);
            }

            // Apply search filter
            bool isSearching = !string.IsNullOrWhiteSpace(_searchFilter);
            List<ShortcutItem> visible;
            if (isSearching)
            {
                var results = ShortcutSearchService.Search(_searchFilter, snapshot);
                _searchSelection.Update(_searchFilter, snapshot.ProfileId, results);
                visible = results.Select(result => result.Item).ToList();
            }
            else
                visible = allShortcuts;

            if (!isSearching) _searchSelection.Update(_searchFilter, snapshot.ProfileId, Array.Empty<SearchResult>());

            // ── Most Used section ──────────────────────────────────────────────
            var mostUsed = !isSearching ? snapshot.MostUsed.ToList() : new List<ShortcutItem>();

            // ── Recently Used section ──────────────────────────────────────────
            var recentlyUsed = !isSearching ? snapshot.Recent.ToList() : new List<ShortcutItem>();

            int insertAt = _shortcutsInsertIndex;
            if (!isSearching)
                foreach (var row in LauncherMenuBuilder.BuildFavorites(snapshot, BuildShortcutItem,
                    text => CreateMenuItem(text), _settingsService.Settings.Language))
                    _trayMenu.Items.Insert(insertAt++, row);

            // Insert Most Used submenu
            if (mostUsed.Count > 0)
            {
                var muMenu = CreateMenuItem(L.Text("Most Used"));
                muMenu.Tag = "category";
                foreach (var sc in mostUsed)
                    muMenu.DropDownItems.Add(BuildShortcutItem(sc));
                _trayMenu.Items.Insert(insertAt++, muMenu);
            }

            // Insert Recently Used submenu
            if (recentlyUsed.Count > 0)
            {
                var ruMenu = CreateMenuItem(L.Text("Recently Used"));
                ruMenu.Tag = "category";
                foreach (var sc in recentlyUsed)
                    ruMenu.DropDownItems.Add(BuildShortcutItem(sc));
                _trayMenu.Items.Insert(insertAt++, ruMenu);
            }

            if ((mostUsed.Count > 0 || recentlyUsed.Count > 0) && visible.Any())
            {
                var divSep = new ToolStripSeparator { Tag = "shortcuts-sep" };
                _trayMenu.Items.Insert(insertAt++, divSep);
            }

            bool anyAdded = false;
            if (isSearching)
            {
                // Search results are direct launch targets. Keeping category matches
                // inside closed submenus makes fast keyboard search unusable.
                foreach (var shortcut in visible)
                {
                    var row = BuildShortcutItem(shortcut);
                    if (visible.Count(item => item.GetDisplayName() == shortcut.GetDisplayName()) > 1) row.Text += " — " + snapshot.ProfileName + " / " + (shortcut.Category ?? "");
                    row.Checked = shortcut.Id == _searchSelection.SelectedResultId;
                    _trayMenu.Items.Insert(insertAt++, row);
                    anyAdded = true;
                }
            }
            else
            {
                // ── Main shortcut list (recursive folder hierarchy) ──────────
                foreach (var shortcut in visible.Where(sc => string.IsNullOrEmpty(sc.Category)))
                {
                    _trayMenu.Items.Insert(insertAt++, BuildShortcutItem(shortcut));
                    anyAdded = true;
                }

                MenuFolderNode folderTree = BuildFolderTree(
                    snapshot.FolderPaths,
                    visible.Where(sc => !string.IsNullOrEmpty(sc.Category)));

                foreach (MenuFolderNode folder in folderTree.Children.Values)
                {
                    _trayMenu.Items.Insert(insertAt++, BuildFolderMenuItem(folder));
                    anyAdded = true;
                }
            }

            if (!anyAdded)
            {
                string emptyText = string.IsNullOrWhiteSpace(_searchFilter)
                    ? L.Text("(No applications added)")
                    : L.Text("(No matches)");
                var emptyItem = CreateMenuItem(emptyText);
                emptyItem.Enabled = false;
                emptyItem.Tag = "empty";
                emptyItem.ForeColor = Color.Gray;
                _trayMenu.Items.Insert(insertAt, emptyItem);
            }
        }

        private static MenuFolderNode BuildFolderTree(
            IEnumerable<string> folderPaths,
            IEnumerable<ShortcutItem> shortcuts)
        {
            var root = new MenuFolderNode(string.Empty);

            foreach (string folderPath in folderPaths)
                EnsureFolderNode(root, folderPath);

            foreach (ShortcutItem shortcut in shortcuts)
                EnsureFolderNode(root, shortcut.Category).Shortcuts.Add(shortcut);

            return root;
        }

        private static MenuFolderNode EnsureFolderNode(MenuFolderNode root, string folderPath)
        {
            MenuFolderNode current = root;
            string[] segments = (folderPath ?? string.Empty).Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);

            foreach (string segment in segments)
            {
                if (!current.Children.TryGetValue(segment, out MenuFolderNode child))
                {
                    child = new MenuFolderNode(segment);
                    current.Children.Add(segment, child);
                }
                current = child;
            }

            return current;
        }

        private ToolStripMenuItem BuildFolderMenuItem(MenuFolderNode folder)
        {
            var folderItem = CreateMenuItem(
                folder.Name,
                Windows11MenuIcons.CreateFolderIcon(ScaleForMenu(16)));
            folderItem.Tag = "category";

            foreach (ShortcutItem shortcut in folder.Shortcuts)
                folderItem.DropDownItems.Add(BuildShortcutItem(shortcut));

            foreach (MenuFolderNode child in folder.Children.Values)
                folderItem.DropDownItems.Add(BuildFolderMenuItem(child));

            if (folderItem.DropDownItems.Count == 0)
            {
                ToolStripMenuItem emptyItem = CreateMenuItem(L.Text("(Empty folder)"));
                emptyItem.Enabled = false;
                emptyItem.Tag = "empty";
                folderItem.DropDownItems.Add(emptyItem);
            }

            return folderItem;
        }

        private int GetMenuItemHeight()
        {
            return ScaleForMenu(Math.Clamp(_settingsService.Settings.MenuItemHeight, 32, 72));
        }

        private int ScaleForMenu(int logicalPixels)
        {
            int dpi = _trayMenu?.DeviceDpi ?? 96;
            return Math.Max(1, (int)Math.Round(logicalPixels * dpi / 96f));
        }

        private ToolStripMenuItem CreateMenuItem(string text, Image image = null, EventHandler onClick = null)
        {
            var menuItem = new SizedToolStripMenuItem(
                text,
                image,
                onClick,
                GetMenuItemHeight(),
                _menuRenderer,
                Math.Clamp(_settingsService.Settings.MenuFontSize, 9f, 18f))
            {
                TextAlign = ContentAlignment.MiddleLeft,
                ImageAlign = ContentAlignment.MiddleLeft
            };

            // A focused hosted TextBox can leave the open submenu owner selected
            // until another submenu actually expands. Explicit pointer selection
            // keeps the hover highlight in sync without moving keyboard focus away
            // from Search shortcuts.
            menuItem.MouseEnter += (_, _) => menuItem.Select();
            return menuItem;
        }

        private static void DisposeMenuTreeItem(ToolStripMenuItem item)
        {
            for (int i = item.DropDownItems.Count - 1; i >= 0; i--)
            {
                ToolStripItem child = item.DropDownItems[i];
                if (child is ToolStripMenuItem childMenu)
                    DisposeMenuTreeItem(childMenu);
                else
                    child.Dispose();
            }

            Image image = item.Image;
            item.Image = null;
            image?.Dispose();
            item.Dispose();
        }

        private ToolStripMenuItem BuildShortcutItem(ShortcutItem shortcut)
        {
            string status = shortcut.Status switch
            {
                ShortcutStatus.MissingTarget => " — Missing target",
                ShortcutStatus.Unavailable => " — Unavailable",
                ShortcutStatus.Invalid => " — Invalid shortcut",
                ShortcutStatus.NeedsReview => " — Needs review",
                _ => string.Empty
            };
            var menuItem = CreateMenuItem(
                shortcut.GetDisplayName().Replace("&", "&&") + status,
                shortcut.Icon?.ToBitmap());
            menuItem.Tag = shortcut;
            menuItem.ToolTipText = string.IsNullOrEmpty(shortcut.TargetPath) ? shortcut.ShortcutFilePath : shortcut.TargetPath;

            // Left-click: launch
            menuItem.Click += (s, e) => LaunchCurrentShortcut(shortcut);

            // Right-click context menu: Rename, Remove, Open Location, Change Icon, Move Up/Down
            var ctx = new Windows11ContextMenuStrip(
                _menuRenderer,
                Windows11MenuStyle.CreateFont(
                    Math.Clamp(_settingsService.Settings.MenuFontSize, 9f, 18f)));

            var renameItem = CreateMenuItem(L.Text("Rename..."));
            renameItem.Click += (s, e) => OnRenameShortcut(shortcut);

            var removeItem = CreateMenuItem(L.Text("Remove"));
            removeItem.Click += (s, e) => OnRemoveShortcut(shortcut);

            var openLocItem = CreateMenuItem(L.Text("Open File Location"));
            openLocItem.Click += (s, e) => OnOpenFileLocation(shortcut);

            var changeIconItem = CreateMenuItem(L.Text("Change Icon..."));
            changeIconItem.Click += (s, e) => RunPersistenceAction(() => OnChangeIcon(shortcut));
            var repairItem = CreateMenuItem(L.Text("Repair target..."));
            repairItem.Enabled = _settingsService.UsesStateV2 && File.Exists(shortcut.ShortcutFilePath) &&
                !Path.GetExtension(shortcut.ShortcutFilePath).Equals(".exe", StringComparison.OrdinalIgnoreCase);
            repairItem.Click += (_, _) => OnRepairTarget(shortcut);

            var moveUpItem = CreateMenuItem(L.Text("Move Up"));
            moveUpItem.Click += (s, e) => RunPersistenceAction(() => OnMoveShortcut(shortcut, -1));

            var moveDownItem = CreateMenuItem(L.Text("Move Down"));
            moveDownItem.Click += (s, e) => RunPersistenceAction(() => OnMoveShortcut(shortcut, +1));
            var pinItem = CreateMenuItem(LocalizationService.Get(shortcut.IsPinned ? "Menu_Unpin" : "Menu_Pin", _settingsService.Settings.Language));
            pinItem.Enabled = _settingsService.UsesStateV2 && !_settingsService.StateLoad.ReadOnly;
            pinItem.Click += (_, _) => RunPersistenceAction(() => _shortcutManager.SetPinned(shortcut, !shortcut.IsPinned));
            var earlier = CreateMenuItem(LocalizationService.Get("Menu_PinEarlier", _settingsService.Settings.Language));
            var later = CreateMenuItem(LocalizationService.Get("Menu_PinLater", _settingsService.Settings.Language));
            var pins = _renderedMenuSnapshot?.Pins ?? Array.Empty<ShortcutItem>();
            int pinIndex = _renderedPinPositions.TryGetValue(shortcut.Id, out int position) ? position : -1;
            earlier.Available = later.Available = shortcut.IsPinned;
            earlier.Enabled = pinItem.Enabled && shortcut.IsPinned && pinIndex > 0;
            later.Enabled = pinItem.Enabled && shortcut.IsPinned && pinIndex >= 0 && pinIndex < pins.Count - 1;
            earlier.Click += (_, _) => RunPersistenceAction(() => OnMoveFavorite(shortcut, -1));
            later.Click += (_, _) => RunPersistenceAction(() => OnMoveFavorite(shortcut, 1));

            ctx.Items.AddRange(new ToolStripItem[]
            {
                pinItem, earlier, later, new ToolStripSeparator(),
                renameItem, removeItem, openLocItem,
                new ToolStripSeparator(),
                changeIconItem, repairItem,
                new ToolStripSeparator(),
                moveUpItem, moveDownItem
            });

            menuItem.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    ctx.Show(Cursor.Position);
            };
            ToolStrip owner = null;
            KeyEventHandler contextKey = (_, args) =>
            {
                if (menuItem.Selected && LauncherMenuBuilder.IsContextKey(args.KeyData))
                {
                    args.Handled = true;
                    args.SuppressKeyPress = true;
                    ctx.Show(owner, menuItem.Bounds.Location);
                }
            };
            menuItem.OwnerChanged += (_, _) =>
            {
                if (owner != null) owner.KeyDown -= contextKey;
                owner = menuItem.Owner;
                if (owner != null) owner.KeyDown += contextKey;
            };
            menuItem.Disposed += (_, _) => { if (owner != null) owner.KeyDown -= contextKey; ctx.Dispose(); };

            return menuItem;
        }

        private void ApplySearchSelection()
        {
            foreach (ToolStripItem row in _trayMenu.Items)
                if (row is ToolStripMenuItem menu && menu.Tag is ShortcutItem item) menu.Checked = item.Id == _searchSelection.SelectedResultId;
        }

        private void LaunchSelectedSearchMatch()
        {
            ShortcutItem shortcut = _searchSelection.Selected?.Item;
            if (shortcut == null || string.IsNullOrWhiteSpace(_searchFilter))
                return;

            _trayMenu.Close(ToolStripDropDownCloseReason.ItemClicked);
            LaunchCurrentShortcut(shortcut);
        }

        private void LaunchCurrentShortcut(ShortcutItem source)
        {
            var snapshot = _shortcutManager.CaptureMenuSnapshot();
            var current = LauncherMenuBuilder.ResolveActivation(snapshot, source);
            if (current != null) LaunchShortcut(current);
        }

        private void OnMoveFavorite(ShortcutItem shortcut, int direction)
        {
            var snapshot = _shortcutManager.CaptureMenuSnapshot();
            var ids = snapshot.Pins.Select(item => item.Id).ToList();
            int index = ids.IndexOf(shortcut.Id);
            int destination = index + direction;
            if (shortcut.ProfileId != snapshot.ProfileId || index < 0 || destination < 0 || destination >= ids.Count) return;
            (ids[index], ids[destination]) = (ids[destination], ids[index]);
            _shortcutManager.ReorderPins(ids);
        }

        // ── Profile submenu ─────────────────────────────────────────────────────

        private void RebuildProfileSubmenu(ToolStripMenuItem profileMenu)
        {
            for (int i = profileMenu.DropDownItems.Count - 1; i >= 0; i--)
            {
                ToolStripItem oldItem = profileMenu.DropDownItems[i];
                profileMenu.DropDownItems.RemoveAt(i);
                if (oldItem is ToolStripMenuItem oldMenuItem)
                    DisposeMenuTreeItem(oldMenuItem);
                else
                    oldItem.Dispose();
            }
            string active = _settingsService.Settings.ActiveProfileName;

            foreach (string name in _profileService.GetProfileNames())
            {
                var item = CreateMenuItem(name.Replace("&", "&&"));
                item.AccessibleName = name;
                item.Checked = name.Equals(active, StringComparison.OrdinalIgnoreCase);
                string captured = name;
                item.Click += (s, e) =>
                {
                    RunPersistenceAction(() => _profileService.SwitchProfile(captured));
                };
                profileMenu.DropDownItems.Add(item);
            }

            profileMenu.DropDownItems.Add(new ToolStripSeparator());

            var newProfile = CreateMenuItem(L.Text("New Profile..."));
            newProfile.Click += (s, e) =>
            {
                using var dlg = new RenameDialog(L.Text("New Profile"));
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
                if (ex is StatePersistenceException)
                    MessageBox.Show(ex.Message + L.Text("\nOpen Recovery to inspect interrupted operations."),
                        L.Text("TaskFolder — Refresh could not be completed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        private void ApplyTaskbarSettings()
        {
            _widgetRecovery.Reset();
            if (_settingsService.Settings.TaskbarButtonEnabled)
            {
                EnsureWidgetHost();
                _widgetWatch.Start();
            }
            else
            {
                _widgetWatch.Stop();
                RemoveWidget();
            }
        }

        private void EnsureWidgetHost()
        {
            if (_exiting || _ensuringWidget || !_settingsService.Settings.TaskbarButtonEnabled || _widget?.IsDragging == true) return;
            _ensuringWidget = true;
            try
            {
                if (_widget?.HasRenderFailure == true) RemoveWidget();
                // Do not allocate discovery snapshots while backing off or latched in tray fallback.
                // Session/display notifications and deliberate user requests explicitly rearm recovery.
                if (!_widgetRecovery.CanAttempt(Environment.TickCount64)) return;
                var host = DesktopNative.FindHost(_widget?.Host);
                if (host == null)
                {
                    RemoveWidget();
                    LogWidgetHost("Waiting for the Windows desktop. Tray access remains available.");
                    return;
                }
                _widgetRecovery.ObserveHost(host.Handle, host.ProcessId, host.Dpi, host.ClientSize);
                if (!_widgetRecovery.CanAttempt(Environment.TickCount64)) return;
                if (_widget == null || _widget.IsDisposed || !_widget.IsHandleCreated ||
                    !TaskbarNative.IsWindow(_widget.NativeHandle) || _widget.Host.Handle != host.Handle ||
                    _widget.Host.ProcessId != host.ProcessId || _widget.Host.Dpi != host.Dpi ||
                    TaskbarNative.GetParent(_widget.NativeHandle) != host.Handle)
                {
                    RemoveWidget();
                    using var dpi = new TaskbarNative.DpiScope(host.Handle);
                    _widget = new TaskbarWidgetForm(_trayIcon.Icon, _settingsService.Settings, host);
                    _widget.OpenRequested += (_, _) => OpenFromWidget();
                    _widget.HoverOpenRequested += (_, _) => OpenWidgetMenu(true);
                    _widget.InteractionStarted += (_, _) => _trayMenu.Close();
                    _widget.SettingsRequested += OnSettings;
                    _widget.PositionCommitted += (_, _) => RunPersistenceAction(() => _settingsService.Save());
                    _widget.HostingFailed += OnWidgetHostingFailed;
                    _ = _widget.Handle; // Attach and verify before any request to show the child.
                    if (_widget.HasRenderFailure) { RemoveWidget(); return; }
                    _widget.Show();
                    if (_widget.HasRenderFailure) { RemoveWidget(); return; }
                    LogWidgetHost($"Attached desktop child HWND={_widget.NativeHandle}, parent={host.Handle}, DPI={host.Dpi}.");
                }
                else _widget.RefreshHost(host);
                if (_widget.HasRenderFailure) { RemoveWidget(); return; }
                _widget.Enabled = _settingsForm == null;
                _widget.HoverEnabled = _settingsForm == null && _settingsService.Settings.TaskbarHoverEnabled;
                _widget.ApplyPreferences();
                _widgetRecovery.Reset();
            }
            catch (Exception ex) when (TaskbarNative.IsGraphicsFailure(ex))
            {
                RemoveWidget();
                RecordWidgetFailure(ex);
            }
            finally { _ensuringWidget = false; }
        }

        private void RecordWidgetFailure(Exception error)
        {
            _widgetRecovery.Failed(Environment.TickCount64);
            // One bounded message per failure; never open another error dialog from rendering.
            try
            {
                LogWidgetHost($"Widget rendering paused ({_widgetRecovery.Failures}/4): {error.GetType().Name}, " +
                    $"HRESULT=0x{error.HResult:X8}. " +
                    (_widgetRecovery.Exhausted ? "Tray remains available; waiting for a display/session change or a menu request." : "Retry is delayed."));
            }
            catch (OutOfMemoryException) { /* Logging must not turn a resource failure into an error-dialog loop. */ }
        }

        private void OnWidgetHostingFailed(Exception error)
        {
            RecordWidgetFailure(error);
            var failed = _widget;
            // Dispose after the native paint/DPI callback has unwound, never inside it.
            try
            {
                _trayMenu.BeginInvoke(new Action(() =>
                {
                    if (ReferenceEquals(_widget, failed)) RemoveWidget();
                }));
            }
            catch (InvalidOperationException) { /* Shutdown or no UI handle: the watcher also removes failed widgets. */ }
            catch (OutOfMemoryException) { /* The watcher removes it without allocating another callback. */ }
        }

        private void OnWidgetSessionSwitch(object sender, SessionSwitchEventArgs e) => QueueWidgetEnvironmentChange();
        private void OnWidgetDisplayChanged(object sender, EventArgs e) => QueueWidgetEnvironmentChange();

        private void QueueWidgetEnvironmentChange()
        {
            if (_exiting || Interlocked.Exchange(ref _environmentNotificationPending, 1) != 0) return;
            try
            {
                _trayMenu.BeginInvoke(new Action(() =>
                {
                    Interlocked.Exchange(ref _environmentNotificationPending, 0);
                    if (_exiting) return;
                    _widgetRecovery.Reset();
                    _widget?.InvalidatePresentation();
                    EnsureWidgetHost();
                }));
            }
            catch (InvalidOperationException) { Interlocked.Exchange(ref _environmentNotificationPending, 0); }
            catch (OutOfMemoryException) { Interlocked.Exchange(ref _environmentNotificationPending, 0); }
        }

        private void RemoveWidget()
        {
            var old = _widget;
            _widget = null;
            if (old != null && _trayMenu.Visible) _trayMenu.Close();
            old?.Dispose();
        }

        private void LogWidgetHost(string message)
        {
            if (_lastWidgetHostMessage == message) return;
            _lastWidgetHostMessage = message;
            try
            {
                string path = Path.Combine(_settingsService.DataRoot, "taskbar-embedded.log");
                if (!File.Exists(path) || new FileInfo(path).Length < 128 * 1024)
                    File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void OpenFromTaskbar()
        {
            // --menu / pinned activation opens at the tray, even when an app covers the widget.
            if (!_exiting) ShowContextMenu();
        }

        private void OpenFromWidget()
        {
            OpenWidgetMenu(false);
        }

        private void OpenWidgetMenu(bool fromHover)
        {
            if (_exiting) return;
            _widgetRecovery.Reset(); // A deliberate user request permits a new bounded recovery cycle.
            EnsureWidgetHost();
            if (_widget == null) { ShowContextMenu(); return; }
            ShowTaskbarMenu(_widget.ScreenBounds, fromHover);
        }

        private void ShowTaskbarMenu(Rectangle button, bool fromHover = false)
        {
            if (_exiting || _trayMenu.Visible || _settingsForm != null || _widget == null) return;
            Rectangle area = Screen.FromRectangle(button).WorkingArea;
            Size size = _trayMenu.GetPreferredSize(Size.Empty);
            int x = button.Left + button.Width / 2 - size.Width / 2;
            int gap = (int)Math.Round(4 * _widget.RenderDpi / 96.0);
            int y = button.Top - size.Height - gap;
            if (y < area.Top) y = button.Bottom + gap;
            else if (button.Right <= area.Left) { x = button.Right; y = button.Top; }
            else if (button.Left >= area.Right) { x = button.Left - size.Width; y = button.Top; }
            x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - size.Width));
            y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - size.Height));
            _widgetMenuOwner.Location = button.Location;
            _widgetMenuOwner.Show();
            SetForegroundWindow(_widgetMenuOwner.Handle);
            _trayMenu.Show(_widgetMenuOwner, _widgetMenuOwner.PointToClient(new Point(x, y)));
            if (fromHover && _trayMenu is Windows11ContextMenuStrip hoverMenu)
                hoverMenu.StartHoverDismissal(point => _widget != null && !_widget.IsDisposed &&
                    _widget.ScreenBounds.Contains(point) && TaskbarNative.WindowFromPoint(point) == _widget.NativeHandle,
                    _settingsService.Settings.WidgetMenuHideDelayMs);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private void LaunchShortcut(ShortcutItem shortcut)
        {
            var result = _shortcutLaunch.Launch(shortcut);
            if (!result.Dispatched)
            {
                MessageBox.Show(L.Text("Failed to launch application:\n{0}", result.DispatchError.Message),
                    L.Text("TaskFolder Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!result.StatisticsResult.Success)
                MessageBox.Show(L.Text("The launch request was sent, but its statistics could not be saved.\n") +
                    new StatePersistenceException(result.StatisticsResult).Message,
                    "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (_settingsService.Settings.ShowNotifications)
                _trayIcon.ShowBalloonTip(2000, "TaskFolder", L.Text("Launched {0}", shortcut.GetDisplayName()), ToolTipIcon.Info);
        }

        private void OnRecovery(object sender, EventArgs e)
        {
            if (_recoveryForm != null && !_recoveryForm.IsDisposed) { _recoveryForm.Activate(); return; }
            _recoveryForm = new RecoveryForm(_settingsService, _shortcutManager);
            _recoveryForm.FormClosed += (_, _) => _recoveryForm = null;
            _recoveryForm.Show();
        }

        private void OnManageShortcuts(object sender, EventArgs e)
        {
            if (!_settingsService.UsesStateV2)
            {
                using var migration = new MigrationPreviewForm(_settingsService);
                if (migration.ShowDialog() != DialogResult.OK) return;
                _shortcutManager.ReloadShortcuts(); RefreshShortcutsList();
            }
            if (_managerForm != null && !_managerForm.IsDisposed)
            {
                if (_managerForm.WindowState == FormWindowState.Minimized) _managerForm.WindowState = FormWindowState.Normal;
                _managerForm.Activate(); return;
            }
            _managerForm = new ShortcutManagerForm(_settingsService, _shortcutManager);
            _managerForm.FormClosed += (_, _) => _managerForm = null;
            _managerForm.Show();
        }

        private void OnAddApplication(object sender, EventArgs e)
        {
            if (_settingsService.UsesStateV2)
            {
                using var add = new AddItemDialog(_settingsService);
                add.ShowDialog(); return;
            }
            using var dlg = new OpenFileDialog
            {
                Filter = "Applications (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|All Files (*.*)|*.*",
                Title = L.Text("Select Application to Add")
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
                    MessageBox.Show(L.Text("Failed to add application:\n{0}", ex.Message),
                        L.Text("TaskFolder Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                MessageBox.Show(L.Text("Failed to open shortcuts folder:\n{0}", ex.Message),
                    L.Text("TaskFolder Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSettings(object sender, EventArgs e)
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                if (_settingsForm.WindowState == FormWindowState.Minimized)
                    _settingsForm.WindowState = FormWindowState.Normal;
                _settingsForm.Activate();
                return;
            }
            if (_exiting || _settingsOpening) return;
            _settingsOpening = true;
            try
            {
                MenuMotion.OpenDialogAfterMenu(_trayMenu, () =>
                {
                    _settingsOpening = false;
                    if (!_exiting) ShowSettingsDialog();
                });
            }
            catch { _settingsOpening = false; throw; }
        }

        private void ShowSettingsDialog()
        {
            using var form = new SettingsForm(_shortcutManager, _settingsService, _hotkeyService);
            _settingsForm = form;
            if (_widget != null) { _widget.HoverEnabled = false; _widget.Enabled = false; }
            try
            {
                if (form.ShowDialog() == DialogResult.OK)
                    RebuildTrayMenu();
            }
            finally
            {
                _settingsForm = null;
                ApplyTaskbarSettings();
            }
            // Re-apply tooltip in case profile changed
            UpdateTrayTooltip();
        }

        private void RebuildTrayMenu()
        {
            _searchFilter = string.Empty;
            var previousMenu = _trayMenu;
            var previousHeaderLogo = _headerLogoImage;
            _headerLogoImage = null;
            _trayMenu = BuildTrayMenu();
            _trayIcon.ContextMenuStrip = _trayMenu;
            _ = _trayMenu.Handle;
            DisposeMenuImages(previousMenu?.Items);
            previousMenu?.Dispose();
            previousHeaderLogo?.Dispose();
        }

        private static void DisposeMenuImages(ToolStripItemCollection items)
        {
            if (items == null) return;

            foreach (ToolStripItem item in items)
            {
                if (item is not ToolStripMenuItem menuItem) continue;
                DisposeMenuImages(menuItem.DropDownItems);
                Image image = menuItem.Image;
                menuItem.Image = null;
                image?.Dispose();
            }
        }

        private void OnRenameShortcut(ShortcutItem shortcut)
        {
            using var dlg = new RenameDialog(shortcut.GetDisplayName());
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                bool ok = _shortcutManager.RenameShortcut(shortcut, dlg.NewName);
                if (!ok)
                    MessageBox.Show(L.Text("Rename failed."), "TaskFolder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnRemoveShortcut(ShortcutItem shortcut)
        {
            var result = MessageBox.Show(
                L.Text("Remove '{0}' from TaskFolder?", shortcut.GetDisplayName()),
                L.Text("Remove Shortcut"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
                RunPersistenceAction(() => _shortcutManager.RemoveShortcut(shortcut));
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
                MessageBox.Show(L.Text("Failed to open location:\n{0}", ex.Message),
                    L.Text("TaskFolder Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnChangeIcon(ShortcutItem shortcut)
        {
            var meta = _settingsService.GetMetadata(shortcut);
            using var dlg = new IconPickerDialog(meta.CustomIconPath ?? "", meta.CustomIconIndex);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _shortcutManager.SetCustomIcon(shortcut, dlg.SelectedIconPath, dlg.SelectedIconIndex);
                RefreshShortcutsList();
            }
        }

        private void OnRepairTarget(ShortcutItem shortcut)
        {
            try
            {
                string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(shortcut.ShortcutFilePath)));
                using var dialog = new RepairTargetDialog(shortcut);
                if (dialog.ShowDialog() == DialogResult.OK)
                    _shortcutManager.RepairTarget(shortcut, dialog.Target, fingerprint);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, L.Text("Target could not be repaired"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        private static void RunPersistenceAction(Action action)
        {
            try { action(); }
            catch (StatePersistenceException ex)
            {
                MessageBox.Show(ex.Message, L.Text("TaskFolder — Data could not be saved"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnMoveShortcut(ShortcutItem shortcut, int direction)
        {
            _settingsService.RequireV2();
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
            var orders = sameGroup.Select((item, index) => (item.Id, Order: index * 10))
                .ToDictionary(pair => pair.Id, pair => pair.Order);
            (orders[sameGroup[idx].Id], orders[sameGroup[swapIdx].Id]) =
                (orders[sameGroup[swapIdx].Id], orders[sameGroup[idx].Id]);
            _shortcutManager.UpdateSortOrders(sameGroup, orders);

            RefreshShortcutsList();
        }

        private void OnExit(object sender, EventArgs e)
        {
            if (_exiting) return;
            // A cancelled alias-draft close must leave the tray and services running.
            if (_managerForm != null && !_managerForm.IsDisposed)
            {
                _managerForm.Close();
                if (_managerForm != null && !_managerForm.IsDisposed) return;
            }
            _exiting = true;
            _widgetWatch.Stop();
            RemoveWidget();
            _widgetMenuOwner?.Hide();
            _menuActivationWait?.Unregister(null);
            _menuActivationWait = null;
            _activationWait?.Unregister(null);
            _activationWait = null;
            _trayIcon.Visible = false;
            _shortcutManager?.Dispose();
            _hotkeyService?.Dispose();
            Application.Exit();
        }

        private void OnPresentationChanged(object sender, EventArgs args)
        {
            if (_exiting || _trayMenu == null || _trayMenu.IsDisposed || !_trayMenu.IsHandleCreated) return;
            if (_trayMenu.InvokeRequired) { _trayMenu.BeginInvoke(new Action(() => OnPresentationChanged(sender, args))); return; }
            UiThemeService.Apply(_trayMenu);
        }

        private sealed class SizedToolStripMenuItem : ToolStripMenuItem
            , IWindows11AnimatedMenuItem
        {
            private readonly int _minimumHeight;
            private readonly Windows11MenuRenderer _renderer;
            private readonly float _fontSize;
            private readonly Timer _hoverTimer;
            private readonly Timer _folderHoverTimer;
            private const int FolderHoverDelay = 200;
            private long _folderHoverStarted;
            private bool _folderHoverPending;
            private ToolStrip _hoverOwner;
            private float _hoverProgress;
            private float _hoverTarget;
            private bool _pointerIsOver;

            public float HoverProgress => _pointerIsOver
                ? _hoverProgress
                : Selected ? 1f : 0f;

            public SizedToolStripMenuItem(
                string text,
                Image image,
                EventHandler onClick,
                int minimumHeight,
                Windows11MenuRenderer renderer,
                float fontSize)
                : base(text, image, onClick)
            {
                _minimumHeight = minimumHeight;
                _renderer = renderer;
                _fontSize = fontSize;
                _hoverTimer = new Timer { Interval = 16 };
                _hoverTimer.Tick += (_, _) => AdvanceHoverEffect();
                _folderHoverTimer = new Timer { Interval = FolderHoverDelay };
                _folderHoverTimer.Tick += (_, _) => OpenHoveredFolder();
                MouseEnter += (_, _) =>
                {
                    _pointerIsOver = true;
                    SetHoverTarget(1f);
                    if (Enabled && HasDropDownItems && !DropDown.Visible)
                    {
                        _folderHoverStarted = Environment.TickCount64;
                        _folderHoverPending = true;
                        _folderHoverTimer.Start();
                    }
                };
                MouseLeave += (_, _) =>
                {
                    CancelFolderHover();
                    _pointerIsOver = false;
                    SetHoverTarget(0f);
                };
            }

            public override Size GetPreferredSize(Size constrainingSize)
            {
                Size preferredSize = base.GetPreferredSize(constrainingSize);
                preferredSize.Height = Math.Max(preferredSize.Height, _minimumHeight);
                return preferredSize;
            }

            protected override ToolStripDropDown CreateDefaultDropDown()
            {
                var menu = new Windows11ContextMenuStrip(
                    _renderer,
                    Windows11MenuStyle.CreateFont(_fontSize));
                // Autogenerated dropdowns inherit OwnerItem.Font when no local
                // font is set. Keep that live DPI-adjusted font through every
                // level instead of freezing the saved point size at creation.
                menu.OwnerItem = this;
                menu.ResetFont();
                // A shorter Windows menu delay must not open a folder before
                // our 200 ms dwell. Clicks and keyboard input cancel the dwell.
                menu.Opening += (_, e) =>
                {
                    if (_folderHoverPending &&
                        Environment.TickCount64 - _folderHoverStarted < FolderHoverDelay)
                        e.Cancel = true;
                };
                return menu;
            }

            private void OpenHoveredFolder()
            {
                if (!_folderHoverPending)
                    return;
                CancelFolderHover();
                ToolStrip parent = GetCurrentParent();
                if (IsDisposed || !Enabled || !Available || !_pointerIsOver ||
                    !Selected || !HasDropDownItems || parent == null ||
                    !parent.Visible || parent.IsDisposed ||
                    parent.GetItemAt(parent.PointToClient(Control.MousePosition)) != this)
                    return;

                if (!DropDown.Visible)
                    ShowDropDown();
            }

            private void CancelFolderHover()
            {
                _folderHoverPending = false;
                _folderHoverTimer?.Stop();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                CancelFolderHover();
                base.OnMouseDown(e);
            }

            protected override bool ProcessDialogKey(Keys keyData)
            {
                CancelFolderHover();
                return base.ProcessDialogKey(keyData);
            }

            protected override bool ProcessCmdKey(ref Message m, Keys keyData)
            {
                CancelFolderHover();
                return base.ProcessCmdKey(ref m, keyData);
            }

            protected override bool ProcessMnemonic(char charCode)
            {
                CancelFolderHover();
                return base.ProcessMnemonic(charCode);
            }

            protected override void OnDropDownOpened(EventArgs e)
            {
                CancelFolderHover();
                base.OnDropDownOpened(e);
            }

            protected override void OnOwnerChanged(EventArgs e)
            {
                CancelFolderHover();
                if (_hoverOwner != null)
                    _hoverOwner.VisibleChanged -= OnHoverOwnerVisibleChanged;
                base.OnOwnerChanged(e);
                _hoverOwner = Owner;
                if (_hoverOwner != null)
                    _hoverOwner.VisibleChanged += OnHoverOwnerVisibleChanged;
            }

            private void OnHoverOwnerVisibleChanged(object sender, EventArgs e)
            {
                if (_hoverOwner?.Visible != true)
                    CancelFolderHover();
            }

            private void SetHoverTarget(float target)
            {
                _hoverTarget = target;
                if (!SystemInformation.IsMenuAnimationEnabled ||
                    !SystemInformation.IsMenuFadeEnabled)
                {
                    _hoverProgress = target;
                    Invalidate();
                    return;
                }

                _hoverTimer.Start();
            }

            private void AdvanceHoverEffect()
            {
                const float step = 0.22f;
                float distance = _hoverTarget - _hoverProgress;
                if (Math.Abs(distance) <= step)
                {
                    _hoverProgress = _hoverTarget;
                    _hoverTimer.Stop();
                }
                else
                {
                    _hoverProgress += Math.Sign(distance) * step;
                }
                Invalidate();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (_hoverOwner != null)
                        _hoverOwner.VisibleChanged -= OnHoverOwnerVisibleChanged;
                    _folderHoverTimer.Dispose();
                    _hoverTimer.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        private sealed class MenuFolderNode
        {
            internal MenuFolderNode(string name)
            {
                Name = name;
            }

            internal string Name { get; }
            internal SortedDictionary<string, MenuFolderNode> Children { get; } =
                new SortedDictionary<string, MenuFolderNode>(StringComparer.OrdinalIgnoreCase);
            internal List<ShortcutItem> Shortcuts { get; } = new List<ShortcutItem>();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _exiting = true;
                UiThemeService.Changed -= OnPresentationChanged;
                SystemEvents.SessionSwitch -= OnWidgetSessionSwitch;
                SystemEvents.DisplaySettingsChanged -= OnWidgetDisplayChanged;
                _widgetWatch.Dispose();
                RemoveWidget();
                _widgetMenuOwner?.Dispose();
                _widgetMenuOwner = null;
                _menuActivationWait?.Unregister(null);
                _menuActivationWait = null;
                _activationWait?.Unregister(null);
                _activationWait = null;
                _trayIcon?.Dispose();
                DisposeMenuImages(_trayMenu?.Items);
                _trayMenu?.Dispose();
                _headerLogoImage?.Dispose();
                _managerForm?.Dispose();
                _shortcutManager?.Dispose();
                _hotkeyService?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
