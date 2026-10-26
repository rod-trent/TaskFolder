using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Utilities;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception("FAIL: " + description);
        _checks++;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--live") { RunLiveCheck(args[1]); return; }
        if (args.Length == 2 && args[0] == "--probe-running") { ProbeRunningWidget(uint.Parse(args[1])); return; }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        CheckRenderingRecovery();
        CheckOutsideClickDismissal();
        var pointer = new WidgetInteraction();
        pointer.Enter(100);
        Check(!pointer.HoverDue(399, true, false), "hover waits the full 300 ms");
        Check(pointer.HoverDue(400, true, false), "300 ms opens once");
        Check(!pointer.HoverDue(1000, true, false), "staying does not reopen");
        pointer.Leave();
        pointer.Enter(2000);
        pointer.Leave();
        Check(!pointer.HoverDue(2500, true, false), "leaving cancels pending hover");
        pointer.Enter(3000);
        pointer.Enter(3200);
        Check(pointer.HoverDue(3300, true, false), "repeated enter does not postpone the visit");
        pointer.Leave();
        pointer.Enter(4000);
        Check(!pointer.HoverDue(4500, false, false), "disabled hover does not open");
        pointer.ConsumeHover();
        Check(!pointer.HoverDue(5000, true, false), "reenable can require a fresh visit");
        pointer.Leave();
        pointer.Enter(6000);
        Check(!pointer.HoverDue(6500, true, true), "open menu blocks hover");
        pointer.ConsumeHover();
        Check(!pointer.HoverDue(6600, true, false), "closing menu does not reopen it under the pointer");
        pointer.BlockUntilExit();
        pointer.Leave(physicallyOutside: false);
        pointer.Enter(7000);
        Check(!pointer.HoverDue(7500, true, false), "menu covering and uncovering widget cannot rearm hover");
        pointer.Leave();
        pointer.Enter(8000);
        Check(pointer.HoverDue(8300, true, false), "physical exit rearms hover after an overlapping menu closes");

        pointer.Press(new Point(100, 100));
        Check(!pointer.HoverDue(9000, true, false), "press cancels hover");
        Check(!pointer.Move(new Point(103, 102), new Size(8, 8)), "minor jitter remains a click");
        Check(pointer.Release(true) == WidgetRelease.Click, "click release opens");
        pointer.Press(new Point(100, 100));
        Check(pointer.Move(new Point(105, 100), new Size(8, 8)), "threshold starts a drag");
        Check(pointer.Move(new Point(100, 100), new Size(8, 8)), "returning to origin remains a drag");
        Check(pointer.Release(true) == WidgetRelease.Drag, "drag release never opens the menu");
        Check(!pointer.HoverDue(9500, true, false), "drag release cannot trigger hover");
        Check(pointer.Release(true) == WidgetRelease.None, "duplicate release does nothing");
        pointer.Press(Point.Empty);
        Check(pointer.Release(false) == WidgetRelease.None, "release outside is not a click");
        pointer.Press(Point.Empty);
        pointer.CancelPress();
        Check(!pointer.Pressed && !pointer.Dragging, "lost capture resets gesture");
        Check(pointer.Release(true) == WidgetRelease.None, "capture loss cannot produce a phantom click");

        var display = new Rectangle(-1920, -200, 1920, 1080);
        var widget = new Size(52, 44);
        Check(WidgetPlacement.Clamp(new Point(-2500, -900), widget, display) == display.Location,
            "clamp supports negative coordinates");
        Check(WidgetPlacement.Clamp(new Point(500, 2000), widget, display) == new Point(-52, 836),
            "widget stays fully inside its monitor");
        Check(WidgetPlacement.Restore(display, widget, 1, 1) == new Point(-52, 836), "right-bottom placement");
        var original = new Rectangle(-1300, 420, 52, 44);
        var normalized = WidgetPlacement.Normalize(display, original);
        Check(WidgetPlacement.Restore(display, widget, normalized.X, normalized.Y) == original.Location,
            "save/restore roundtrip preserves placement");
        Check(WidgetPlacement.Restore(new Rectangle(0, 0, 3840, 2160), new Size(104, 88), 1, 1) ==
            new Point(3736, 2072), "scaled display and widget preserve edge alignment");
        Check(WidgetPlacement.Restore(display, widget, -10, 10) == new Point(-1920, 836), "invalid fractions are clamped");
        var invalid = WidgetPlacement.Restore(display, widget, double.NaN, double.PositiveInfinity);
        Check(display.Contains(new Rectangle(invalid, widget)), "non-finite placement cannot lose widget offscreen");
        Check(WidgetPlacement.Clamp(new Point(5, 5), widget, new Rectangle(0, 0, 20, 20)) == Point.Empty,
            "tiny display does not throw");

        var panel = new Size(1920, 48);
        var embedded = TaskbarLayout.WidgetSize(panel, 96);
        Check(embedded == new Size(44, 44), "icon-only widget fits a normal horizontal taskbar");
        Check(TaskbarLayout.Restore(panel, embedded, 0) == new Point(0, 2), "taskbar left endpoint");
        Check(TaskbarLayout.Restore(panel, embedded, 1) == new Point(1876, 2), "taskbar right endpoint");
        Check(TaskbarLayout.ClampDrag(panel, embedded, new Point(1000, -500)) == new Point(1000, 2),
            "drag remains centered in panel thickness");
        Check(TaskbarLayout.ClampDrag(panel, embedded, new Point(9000, 1000)) == new Point(1876, 2),
            "drag cannot leave the taskbar endpoint");
        Point placed = TaskbarLayout.Restore(panel, embedded, 0.65);
        Check(TaskbarLayout.Restore(panel, embedded, TaskbarLayout.Normalize(panel, embedded, placed)) == placed,
            "taskbar offset save/restore roundtrip");
        var portrait = new Size(48, 1200);
        var vertical = TaskbarLayout.WidgetSize(portrait, 96);
        Check(vertical == new Size(44, 44), "widget fits a vertical taskbar");
        Check(TaskbarLayout.Restore(portrait, vertical, 1) == new Point(2, 1156), "vertical taskbar endpoint");
        Check(TaskbarLayout.ClampDrag(portrait, vertical, new Point(-700, 500)) == new Point(2, 500),
            "vertical dragging uses the vertical axis");
        Check(TaskbarLayout.Restore(panel, embedded, double.NaN) == TaskbarLayout.Restore(panel, embedded, 0.2),
            "invalid taskbar fraction uses default");
        Check(TaskbarLayout.WidgetSize(new Size(3840, 96), 192) == new Size(88, 88), "taskbar widget scales at 200 percent");

        var settings = JsonSerializer.Deserialize<AppSettings>("{\"HotkeyKey\":\"F9\",\"TaskbarHoverEnabled\":false}");
        Check(settings.TaskbarButtonEnabled && !settings.TaskbarHoverEnabled && settings.HotkeyKey == "F9",
            "old preferences are preserved");
        Check(settings.WidgetPositionX == null && settings.WidgetPositionY == null, "first launch has no saved location");
        settings.WidgetDisplayName = @"\\.\DISPLAY2";
        settings.WidgetPositionX = normalized.X;
        settings.WidgetPositionY = normalized.Y;
        settings.WidgetTaskbarPosition = 0.65;
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings));
        Check(restored.WidgetDisplayName == settings.WidgetDisplayName && restored.WidgetPositionX == normalized.X &&
            restored.WidgetPositionY == normalized.Y && !restored.TaskbarHoverEnabled,
            "position, monitor, and disabled hover survive serialization together");
        Check(restored.WidgetTaskbarPosition == 0.65 && restored.WidgetPositionX == normalized.X,
            "embedded axis fraction is saved without reinterpreting old desktop coordinates");
        if (args.Length == 2 && args[0] == "--render") RenderHiddenWidget(args[1]);
        Console.WriteLine($"WIDGET_PROTOTYPE_SMOKE_OK: {_checks} cases. No visible windows or real desktop interaction.");
    }

    private static bool IsDisposed(Bitmap bitmap)
    {
        try { _ = bitmap.Width; return false; }
        catch (ArgumentException) { return true; }
    }

    private static void CheckRenderingRecovery()
    {
        Bitmap captured = null;
        Graphics capturedGraphics = null;
        var drawingError = new OutOfMemoryException("Injected drawing failure");
        try
        {
            using var unexpected = TaskbarNative.CreateSurface(new Size(44, 44), 96,
                graphics => { capturedGraphics = graphics; throw drawingError; },
                size => captured = new Bitmap(size.Width, size.Height));
            Check(false, "injected drawing failure must propagate");
        }
        catch (OutOfMemoryException ex) { Check(ReferenceEquals(ex, drawingError), "drawing failure preserves the original exception"); }
        Check(IsDisposed(captured), "Bitmap is disposed when drawing fails before ownership transfer");
        try { capturedGraphics.Clear(Color.Red); Check(false, "failed drawing Graphics must be disposed"); }
        catch (ArgumentException) { Check(true, "Graphics is disposed after drawing failure"); }
        int allocationCalls = 0;
        try
        {
            using var unexpected = TaskbarNative.CreateSurface(new Size(44, 44), 96, _ => { },
                size => { allocationCalls++; throw new OutOfMemoryException("Injected allocation failure"); });
            Check(false, "allocation failure must propagate");
        }
        catch (OutOfMemoryException) { Check(allocationCalls == 1, "allocation is attempted only once per render"); }
        using (var surface = TaskbarNative.CreateSurface(new Size(44, 44), 96, graphics => graphics.Clear(Color.Blue)))
            Check(surface.GetPixel(2, 2).ToArgb() == Color.Blue.ToArgb(), "successful drawing transfers a usable surface to its owner");
        foreach (var badSize in new[] { Size.Empty, new Size(-1, 44), new Size(1025, 1), new Size(int.MaxValue, int.MaxValue) })
        {
            try
            {
                using var unexpected = TaskbarNative.CreateSurface(badSize, 96, _ => { },
                    size => { allocationCalls++; return new Bitmap(1, 1); });
                Check(false, "invalid geometry must be rejected");
            }
            catch (ArgumentOutOfRangeException) { Check(allocationCalls == 1, "invalid surface is rejected before any allocator call"); }
        }
        foreach (int dpi in new[] { 0, 95, int.MaxValue })
        {
            try { _ = TaskbarLayout.WidgetSize(new Size(1920, 96), dpi); Check(false, "invalid DPI must be rejected"); }
            catch (ArgumentOutOfRangeException) { Check(true, "invalid DPI is rejected before scaling arithmetic"); }
        }
        TaskbarLayout.ValidateSurface(new Size(704, 704), 1536);
        Check(true, "1600 percent DPI remains within the widget surface budget");

        var recovery = new WidgetRecoveryState();
        recovery.ObserveHost(new IntPtr(1), 10, 96, new Size(1920, 48));
        Check(recovery.CanAttempt(0), "initial recovery is allowed");
        recovery.Failed(0);
        Check(!recovery.CanAttempt(999) && recovery.CanAttempt(1000), "first retry waits one second");
        recovery.Failed(1000);
        Check(!recovery.CanAttempt(2999) && recovery.CanAttempt(3000), "second retry waits two seconds");
        recovery.Failed(3000);
        Check(!recovery.CanAttempt(7999) && recovery.CanAttempt(8000), "third retry waits five seconds");
        recovery.Failed(8000);
        recovery.ObserveHost(new IntPtr(1), 10, 96, new Size(1920, 48));
        Check(recovery.Exhausted && !recovery.CanAttempt(long.MaxValue), "unchanged host cannot retry indefinitely after four failures");
        recovery.Failed(9000);
        Check(recovery.Failures == 4, "latched failure count is bounded");
        recovery.ObserveHost(new IntPtr(1), 10, 192, new Size(1920, 96));
        Check(recovery.CanAttempt(9000) && recovery.Failures == 0, "a genuine DPI/geometry change rearms recovery");
        for (int i = 0; i < 4; i++) recovery.Failed(9000);
        recovery.Reset();
        Check(recovery.CanAttempt(0), "explicit session/display/user recovery clears the latch");

        using var source = new Bitmap(44, 44);
        try { TaskbarNative.PresentLayered(IntPtr.Zero, source); } catch (System.ComponentModel.Win32Exception) { }
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        uint before = GetGuiResources(process.Handle, 0);
        for (int i = 0; i < 250; i++)
        {
            try { TaskbarNative.PresentLayered(IntPtr.Zero, source); }
            catch (System.ComponentModel.Win32Exception) { }
        }
        uint after = GetGuiResources(process.Handle, 0);
        Check(after <= before + 2, "250 failed native presentations release their HBITMAP and DC resources");
        Console.WriteLine($"NATIVE_FAILURE_RESOURCES: GDI {before}->{after}; no resource exhaustion was induced.");
    }

    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static void CheckOutsideClickDismissal()
    {
        var regions = new[] { new Rectangle(-500, 100, 200, 300), new Rectangle(-300, 140, 150, 240),
            new Rectangle(-150, 180, 140, 180) };
        var queue = new Queue<Action>();
        Action<Point> click = null;
        int listening = 0, removed = 0, closed = 0;
        OutsideClickMenuDismissal dismissal = null;
        dismissal = new OutsideClickMenuDismissal(
            point => OutsideClickMenuDismissal.ContainsPoint(point, regions),
            queue.Enqueue,
            () => { closed++; dismissal.Stop(); },
            callback => { listening++; click = callback; return new DismissalSubscription(() => removed++); });
        using (dismissal)
        {
            dismissal.Start();
            Check(listening == 1 && removed == 0, "opening starts one outside-click subscription");
            click(new Point(-450, 150));
            click(new Point(-250, 200));
            click(new Point(-100, 220));
            Check(queue.Count == 0 && closed == 0, "clicks in root and nested menus remain active at negative monitor coordinates");
            click(new Point(400, 500));
            click(new Point(500, 600));
            Check(queue.Count == 1 && closed == 0, "outside click queues one dismissal without closing in the native callback");
            queue.Dequeue()();
            Check(closed == 1 && removed == 1, "queued outside click dismisses and releases its listener");
            click(new Point(600, 700));
            Check(queue.Count == 0, "a callback from a closed menu is ignored");

            dismissal.Start();
            click(new Point(400, 500));
            dismissal.Stop();
            dismissal.Start();
            Action<Point> currentClick = click;
            currentClick(new Point(500, 600));
            queue.Dequeue()();
            Check(closed == 1, "a queued click from an old menu cannot close a newly opened menu");
            queue.Dequeue()();
            Check(closed == 2 && removed == 3, "the new menu still dismisses after an old queued callback");

            dismissal.Start();
            Action<Point> staleClick = click;
            dismissal.Start();
            Check(listening == 5 && removed == 4, "restarting observation replaces the listener without duplicates");
            staleClick(new Point(500, 600));
            Check(queue.Count == 0, "a stale native callback cannot affect a new menu session");
            click(new Point(500, 600));
        }
        queue.Dequeue()();
        Check(closed == 2 && removed == listening, "disposal removes observation and cancels queued dismissal");
        Check(!OutsideClickMenuDismissal.ContainsPoint(new Point(-250, 120), regions), "space outside visible submenu bounds dismisses");
        Check(!OutsideClickMenuDismissal.ContainsPoint(new Point(-10, 200), regions), "right edge belongs to the outside target");
        using var hidden = new ContextMenuStrip();
        Check(!OutsideClickMenuDismissal.ContainsPoint(Point.Empty, OutsideClickMenuDismissal.VisibleMenuBounds(hidden)),
            "hidden menus do not protect stale screen regions");
        Check(OutsideClickMenuDismissal.IsButtonDown(0x201) && OutsideClickMenuDismissal.IsButtonDown(0x204) &&
            OutsideClickMenuDismissal.IsButtonDown(0x207) && OutsideClickMenuDismissal.IsButtonDown(0x20B),
            "left, right, middle and extra-button presses dismiss outside the menu");
        Check(!OutsideClickMenuDismissal.IsButtonDown(0x200) && !OutsideClickMenuDismissal.IsButtonDown(0x202) &&
            !OutsideClickMenuDismissal.IsButtonDown(0x20A), "movement, release and wheel events do not dismiss");
    }

    private sealed class DismissalSubscription : IDisposable
    {
        private Action _onDispose;
        internal DismissalSubscription(Action onDispose) => _onDispose = onDispose;
        public void Dispose() { _onDispose?.Invoke(); _onDispose = null; }
    }

    // Read-only native diagnostics for an approved, already-running prototype.
    // Capture the actual desktop strip: HWND hit testing alone cannot prove DWM visibility.
    private static void ProbeRunningWidget(uint processId)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        string directory = Path.GetFullPath(".qa/setparent-prototype");
        Directory.CreateDirectory(directory);
        IntPtr primary = FindWindow("Shell_TrayWnd", null);
        Console.WriteLine($"PRIMARY_TASKBAR: hwnd={primary}, parent={TaskbarNative.GetParent(primary)}, style={GetWindowLong(primary, -16):X8}, rect={TaskbarNative.ScreenBounds(primary)}");
        if (primary != IntPtr.Zero)
        {
            try { _ = new TaskbarHost(primary); }
            catch (Exception ex) { Console.WriteLine($"HOST_SNAPSHOT_ERROR: {ex}"); }
        }
        foreach (var host in TaskbarNative.FindHosts())
        {
            Console.WriteLine($"HOST: hwnd={host.Handle}, bounds={host.Bounds}, dpi={host.Dpi}");
            EnumChildWindows(host.Handle, (window, _) =>
            {
                TaskbarNative.GetWindowThreadProcessId(window, out uint owner);
                if (owner != processId) return true;
                var bounds = TaskbarNative.ScreenBounds(window);
                var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                Console.WriteLine($"CHILD: hwnd={window}, parent={TaskbarNative.GetParent(window)}, bounds={bounds}, visible={TaskbarNative.IsWindowVisible(window)}, style={GetWindowLong(window, -16):X8}, exstyle={GetWindowLong(window, -20):X8}, centerHit={TaskbarNative.WindowFromPoint(center)}");
                var area = Rectangle.Intersect(host.Bounds, Rectangle.Inflate(bounds, 100, 0));
                using var crop = new Bitmap(area.Width, area.Height);
                using (var graphics = Graphics.FromImage(crop))
                    graphics.CopyFromScreen(area.Location, Point.Empty, crop.Size);
                crop.Save(Path.Combine(directory, "widget-on-taskbar.png"), System.Drawing.Imaging.ImageFormat.Png);
                return true;
            }, IntPtr.Zero);
            using var bitmap = new Bitmap(host.Bounds.Width, host.Bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(host.Bounds.Location, Point.Empty, bitmap.Size);
            string output = Path.Combine(directory, $"taskbar-running-{host.Handle}.png");
            bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"ACTUAL_TASKBAR_CAPTURE: {output}");
        }
    }

    private delegate bool ChildCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, ChildCallback callback, IntPtr data);

    // This mode is explicitly opt-in and is run only with permission to open the GUI.
    // It uses the production ApplicationContext; only its own temporary UI is disposed afterward.
    private static void RunLiveCheck(string assemblyPath)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var app = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        var contextType = app.GetType("TaskFolder.TaskFolderApplicationContext", true);
        using var context = (ApplicationContext)Activator.CreateInstance(contextType,
            new object[] { true, null, false, null, false });
        var widgetField = contextType.GetField("_widget", BindingFlags.Instance | BindingFlags.NonPublic);
        var menuField = contextType.GetField("_trayMenu", BindingFlags.Instance | BindingFlags.NonPublic);
        using var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        Exception failure = null;
        int elapsed = 0;
        timer.Tick += (_, _) =>
        {
            try
            {
                elapsed++;
                if (elapsed == 1)
                {
                    var widget = (Form)widgetField.GetValue(context);
                    Check(widget != null && widget.IsHandleCreated, "production context creates a desktop widget");
                    string parentClass = TaskbarNative.WindowClass(TaskbarNative.GetParent(widget.Handle));
                    Check(parentClass == "Progman" || parentClass == "WorkerW" || parentClass == "SHELLDLL_DefView", "production child belongs to the desktop layer");
                    Check((GetWindowLong(widget.Handle, -16) & TaskbarNative.ChildStyle) != 0 && !widget.ShowInTaskbar,
                        "live widget is a child without native app-button registration");
                    var rect = TaskbarNative.ScreenBounds(widget.Handle);
                    Point center = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
                    Check(Screen.FromPoint(center).Bounds.Contains(center), "desktop test point is onscreen");
                    Check(TaskbarNative.WindowFromPoint(center) == widget.Handle,
                        "live hit test reaches the widget on an uncovered desktop");
                    Console.WriteLine($"LIVE_DESKTOP: widget={widget.Handle}, parentClass={parentClass}, bounds={rect}, hit-test=widget.");
                    contextType.GetMethod("OpenFromWidget", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(context, null);
                }
                if (elapsed == 2)
                {
                    var menu = (ContextMenuStrip)menuField.GetValue(context);
                    Check(menu.Visible, "production launcher menu opens");
                    Check(TaskbarNative.GetAncestor(menu.Handle, 2) == menu.Handle, "launcher popup is a top-level window");
                    Point center = new Point(menu.Bounds.Left + menu.Width / 2, menu.Bounds.Top + menu.Height / 2);
                    Check(TaskbarNative.GetAncestor(TaskbarNative.WindowFromPoint(center), 2) == menu.Handle,
                        "live menu hit test reaches the menu outside taskbar clipping");
                    Console.WriteLine($"LIVE_MENU: bounds={menu.Bounds}, hit-test=menu; no fake native application button created.");
                    menu.Close();
                }
                if (elapsed >= 15)
                {
                    Console.WriteLine($"LIVE_SETPARENT_SMOKE_OK: {_checks} checks. Real mouse hover/drag and restart persistence still require operator acceptance.");
                    timer.Stop();
                    context.ExitThread();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                timer.Stop();
                context.ExitThread();
            }
        };
        timer.Start();
        Application.Run(context);
        if (failure != null) throw new Exception("Live SetParent verification failed.", failure);
    }

    private static void RenderHiddenWidget(string assemblyPath)
    {
        var app = Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        var settings = Activator.CreateInstance(app.GetType("TaskFolder.Models.AppSettings", true));
        var widgetType = app.GetType("TaskFolder.Views.TaskbarWidgetForm", true);
        var hostType = app.GetType("TaskFolder.Utilities.TaskbarHost", true);
        object Host(IntPtr handle) => Activator.CreateInstance(hostType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { handle }, null);
        using var parent = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(1400, 96) };
        using var replacement = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(1800, 96) };
        using var icon = new Icon(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(assemblyPath)), "Resources", "TaskFolder.ico"));
        using var form = (Form)Activator.CreateInstance(widgetType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { icon, settings, Host(parent.Handle) }, null);
        int style = GetWindowLong(form.Handle, -20);
        Check(!form.Visible, "offscreen renderer never shows a window");
        Check(!form.ShowInTaskbar, "production widget does not request a taskbar button");
        Check((style & 0x80) != 0 && (style & 0x40000) == 0, "real hidden HWND is TOOLWINDOW without APPWINDOW");
        Check((style & TaskbarNative.LayeredWindowStyle) != 0, "GDI widget has a separate layered composition surface");
        Check(!form.TopMost, "widget no longer relies on TopMost overlay behavior");
        int windowStyle = GetWindowLong(form.Handle, -16);
        Check((windowStyle & TaskbarNative.ChildStyle) != 0 && (windowStyle & TaskbarNative.PopupStyle) == 0,
            "production HWND is WS_CHILD without WS_POPUP");
        Check(TaskbarNative.GetParent(form.Handle) == parent.Handle, "production SetParent attached the real hidden child HWND");
        Check(form.ClientSize == TaskbarLayout.WidgetSize(parent.ClientSize, (int)TaskbarNative.GetDpiForWindow(parent.Handle)),
            "desktop child dimensions use the host DPI");
        void RefreshHost(IntPtr handle) => widgetType.GetMethod("RefreshHost", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { Host(handle) });
        RefreshHost(parent.Handle);
        Check((GetWindowLong(form.Handle, -16) & 0x10000000) == 0,
            "host refresh before Show does not display an unready widget");
        form.Enabled = false;
        form.Enabled = true;
        Check(TaskbarNative.GetParent(form.Handle) == parent.Handle && (GetWindowLong(form.Handle, -16) & TaskbarNative.ChildStyle) != 0,
            "WinForms enabled-state updates preserve native child parenting");
        form.Show(); // Its synthetic ancestor stays hidden; no desktop window becomes visible.
        Check(TaskbarNative.GetParent(form.Handle) == parent.Handle && !form.TopLevel,
            "the real Show path preserves child ownership and managed TopLevel=false");
        Check((GetWindowLong(form.Handle, -20) & TaskbarNative.LayeredWindowStyle) != 0,
            "composition surface survives Enabled changes and the real Show path");
        Check((GetWindowLong(form.Handle, -16) & 0x10000000) != 0 && !TaskbarNative.IsWindowVisible(form.Handle),
            "child Show sets WS_VISIBLE while the hidden ancestor prevents desktop display");
        IntPtr foreground = GetForegroundWindow();
        Rectangle placement = TaskbarNative.ScreenBounds(form.Handle);
        ShowWindow(form.Handle, 0); // Simulate a native hide without destroying the child.
        Check((GetWindowLong(form.Handle, -16) & 0x10000000) == 0,
            "native hide clears WS_VISIBLE without changing the host");
        RefreshHost(parent.Handle);
        Check((GetWindowLong(form.Handle, -16) & 0x10000000) != 0,
            "host recovery restores a natively hidden widget");
        Check(!TaskbarNative.IsWindowVisible(parent.Handle) && !TaskbarNative.IsWindowVisible(form.Handle),
            "child recovery leaves its hidden ancestor hidden");
        var knownHost = new TaskbarHost(parent.Handle);
        var retainedHost = TaskbarNative.SelectHost(Array.Empty<TaskbarHost>(), null, knownHost);
        Check(retainedHost != null && retainedHost.Handle == parent.Handle,
            "empty taskbar discovery retains a verified existing host");
        Check(retainedHost.ProcessId == knownHost.ProcessId && TaskbarNative.GetParent(form.Handle) == parent.Handle,
            "discovery miss does not detach or destroy the live widget");
        // Hidden top-level forms can be clamped to the current desktop width by
        // Windows. Shrink the known native size so this really changes geometry
        // on both a small test desktop and the full-size workstation.
        parent.ClientSize = new Size(Math.Max(100, knownHost.ClientSize.Width / 2), 96);
        retainedHost = TaskbarNative.SelectHost(Array.Empty<TaskbarHost>(), null, knownHost);
        Check(retainedHost.ClientSize == parent.ClientSize,
            $"retained host geometry: native={retainedHost.ClientSize}, managed={parent.ClientSize}, DPI={retainedHost.Dpi}");
        Check(TaskbarNative.SelectHost(Array.Empty<TaskbarHost>(), null, null) == null,
            "first launch without a discovered or known taskbar still waits");
        var discoveredHost = new TaskbarHost(replacement.Handle);
        Check(TaskbarNative.SelectHost(new[] { discoveredHost }, null, knownHost).Handle == replacement.Handle,
            "a discovered replacement takes priority over the retained host");
        using (var lostHost = new Form { ShowInTaskbar = false })
        {
            var oldHost = new TaskbarHost(lostHost.Handle);
            lostHost.Dispose();
            Check(TaskbarNative.SelectHost(Array.Empty<TaskbarHost>(), null, oldHost) == null,
                "a destroyed host is not retained after discovery fails");
        }
        Check(TaskbarNative.GetParent(form.Handle) == parent.Handle && TaskbarNative.ScreenBounds(form.Handle) == placement,
            "visibility recovery preserves parenting and saved placement");
        Check(GetForegroundWindow() == foreground, "visibility recovery does not steal foreground focus");
        var desktopNative = app.GetType("TaskFolder.Utilities.DesktopNative", true);
        Rectangle Area(IntPtr handle) => (Rectangle)desktopNative.GetMethod("PlacementArea", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { Host(handle), null, null });
        var area = Area(parent.Handle);
        var xProperty = settings.GetType().GetProperty("WidgetPositionX");
        var yProperty = settings.GetType().GetProperty("WidgetPositionY");
        xProperty.SetValue(settings, .25); yProperty.SetValue(settings, .75);
        RefreshHost(parent.Handle);
        Check(TaskbarNative.ScreenBounds(form.Handle).Location == WidgetPlacement.Restore(area, form.Size, .25, .75),
            "production refresh restores both saved desktop axes");
        var alternative = WidgetPlacement.Restore(area, form.Size, .75, .25);
        xProperty.SetValue(settings, .75); yProperty.SetValue(settings, .25);
        RefreshHost(parent.Handle);
        Check(TaskbarNative.ScreenBounds(form.Handle).Location == alternative,
            "production placement changes X and Y independently without taskbar snapping");
        using (var sibling = new Control { Parent = parent })
        {
            _ = sibling.Handle;
            Check(SetWindowPos(form.Handle, new IntPtr(1), 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002),
                "synthetic sibling can cover the widget in child Z order");
            Check(GetWindow(form.Handle, 3) != IntPtr.Zero, "widget is below a synthetic sibling before recovery");
            RefreshHost(parent.Handle);
            Check(GetWindow(form.Handle, 3) == IntPtr.Zero, "host recovery raises only the widget above sibling windows");
            Check(GetForegroundWindow() == foreground, "sibling-order recovery does not steal foreground focus");
        }
        TaskbarNative.Attach(form.Handle, replacement.Handle);
        RefreshHost(replacement.Handle);
        Check(TaskbarNative.GetParent(form.Handle) == replacement.Handle, "child reparenting to a second host succeeds");
        Check(form.AccessibleRole == AccessibleRole.PushButton && form.AccessibleName == "TaskFolder",
            "widget has a named accessible button role");
        Check(!File.Exists(Path.Combine(AppContext.BaseDirectory, "settings.json")), "render did not persist user settings");
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        int innerPadding = Math.Max(2, (int)Math.Round(4 * form.DeviceDpi / 96.0));
        Color frameFill = bitmap.GetPixel(innerPadding, bitmap.Height / 2);
        Check(frameFill.A == 255 && frameFill.R >= 220 && frameFill.G >= 220 && frameFill.B >= 220,
            "widget restores its light filled frame around the larger icon");
        string output = Path.Combine(Path.GetTempPath(), "taskfolder-desktop-widget-20261026", "widget-offscreen.png");
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"OFFSCREEN_WIDGET_RENDER: {output}; {bitmap.Width}x{bitmap.Height}; DPI={form.DeviceDpi}. Not a desktop screenshot.");
        IntPtr child = form.Handle;
        replacement.Dispose();
        Check(!TaskbarNative.IsWindow(child), "destroying the synthetic host destroys its hosted child");
        using var recoveredParent = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(1400, 96) };
        using var recoveredWidget = (Form)Activator.CreateInstance(widgetType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { icon, settings, Host(recoveredParent.Handle) }, null);
        Check(TaskbarNative.GetParent(recoveredWidget.Handle) == recoveredParent.Handle,
            "a new production child can be created after host destruction");
        Check(!recoveredWidget.Visible && !recoveredParent.Visible, "recovery smoke never opens visible windows");
        using var menuOwner = (Form)Activator.CreateInstance(app.GetType("TaskFolder.Views.WidgetMenuOwnerForm", true), true);
        Check((GetWindowLong(menuOwner.Handle, -16) & TaskbarNative.ChildStyle) == 0 && !menuOwner.ShowInTaskbar,
            "menu owner is independent from the child and has no taskbar button");
        var desktopHost = desktopNative.GetMethod("FindHost", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { null });
        Check(desktopHost != null, "read-only discovery finds Explorer's desktop icon host");
        var desktopHandle = (IntPtr)hostType.GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(desktopHost);
        string desktopClass = TaskbarNative.WindowClass(desktopHandle);
        Check(desktopClass == "Progman" || desktopClass == "WorkerW" || desktopClass == "SHELLDLL_DefView", "desktop discovery never selects a taskbar host");
        if (desktopClass == "SHELLDLL_DefView")
        {
            IntPtr container = TaskbarNative.GetParent(desktopHandle);
            Check((GetWindowLong(container, -20) & 0x00200000) != 0 &&
                (GetWindowLong(desktopHandle, -20) & TaskbarNative.LayeredWindowStyle) != 0,
                "read-only discovery selects the rendered icon view on the raised desktop");
        }
        Console.WriteLine($"READ_ONLY_DESKTOP_PROBE: {desktopClass}, bounds={TaskbarNative.ScreenBounds(desktopHandle)}; no window attached to Explorer during tests.");
        CheckLayeredDesktopHosting(app, widgetType, hostType, settings, icon);
        CheckWidgetResizing(app, widgetType, hostType, icon);
        CheckProductionRenderingRecovery(app, widgetType, hostType, settings, icon);
    }

    private static void CheckWidgetResizing(Assembly app, Type widgetType, Type hostType, Icon icon)
    {
        var settings = Activator.CreateInstance(app.GetType("TaskFolder.Models.AppSettings", true));
        var sizePercent = settings.GetType().GetProperty("WidgetSizePercent");
        settings.GetType().GetProperty("WidgetPositionX").SetValue(settings, 1.0);
        settings.GetType().GetProperty("WidgetPositionY").SetValue(settings, 1.0);
        using var parent = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(1600, 1000) };
        object Host() => Activator.CreateInstance(hostType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { parent.Handle }, null);
        using var widget = (Form)Activator.CreateInstance(widgetType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { icon, settings, Host() }, null);
        widget.Show();
        var refresh = widgetType.GetMethod("RefreshHost", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (int percent in new[] { 50, 100, 200, 100 })
        {
            sizePercent.SetValue(settings, percent);
            refresh.Invoke(widget, new[] { Host() });
            int side = (int)Math.Round(44 * TaskbarNative.GetDpiForWindow(parent.Handle) / 96.0 * percent / 100.0);
            Check(widget.ClientSize == new Size(side, side), "production widget resizes both axes at " + percent + " percent");
            var desktop = app.GetType("TaskFolder.Utilities.DesktopNative", true);
            var bounds = (Rectangle)desktop.GetMethod("ClientBounds", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { parent.Handle });
            Check(bounds.Contains(TaskbarNative.ScreenBounds(widget.Handle)), "resizing at a saved desktop corner keeps the widget inside its host");
            using var bitmap = new Bitmap(widget.Width, widget.Height);
            widget.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            Check(!(bool)widgetType.GetProperty("HasRenderFailure", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(widget),
                "production widget renders after resizing at " + percent + " percent");
        }
        Check(!TaskbarNative.IsWindowVisible(widget.Handle) && !parent.Visible, "size tests never show a desktop window");
        foreach (int dpi in new[] { 96, 144, 192, TaskbarLayout.MaximumDpi })
        {
            var size = TaskbarLayout.WidgetSize(new Size(32000, 32000), dpi, 200);
            TaskbarLayout.ValidateSurface(size, dpi);
            Check((long)size.Width * size.Height * 4 <= TaskbarLayout.MaximumSurfaceBytes,
                "maximum widget size respects the graphics memory bound at DPI " + dpi);
        }
    }

    private static void CheckLayeredDesktopHosting(Assembly app, Type widgetType, Type hostType,
        object settings, Icon icon)
    {
        object Host(IntPtr handle) => Activator.CreateInstance(hostType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { handle }, null);
        var select = app.GetType("TaskFolder.Utilities.DesktopNative", true)
            .GetMethod("SelectHostSurface", BindingFlags.Static | BindingFlags.NonPublic);
        IntPtr Select(IntPtr view, IntPtr container) => (IntPtr)select.Invoke(null, new object[] { view, container });
        using var root = new NonredirectedDesktopFixture();
        // The production layered child stands in for Explorer's layered icon view.
        // Only its own WS_VISIBLE is set; the synthetic root always stays hidden.
        using var view = (Form)Activator.CreateInstance(widgetType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { icon, settings, Host(root.Handle) }, null);
        view.Show();
        Check(Select(view.Handle, root.Handle) == view.Handle,
            "raised desktop selects its layered icon view rather than nonredirected Progman");
        using var child = (Form)Activator.CreateInstance(widgetType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { icon, settings, Host(Select(view.Handle, root.Handle)) }, null);
        child.Show();
        Check(TaskbarNative.GetParent(child.Handle) == view.Handle && !TaskbarNative.IsWindowVisible(child.Handle),
            "production widget attaches inside the layered view without showing the hidden fixture");
        Check(!TaskbarNative.IsWindowVisible(root.Handle) && !TaskbarNative.IsWindowVisible(view.Handle),
            "layered desktop fixture never opens an onscreen window");
        ShowWindow(view.Handle, 0);
        Check(Select(view.Handle, root.Handle) == root.Handle,
            "hidden desktop icon view does not become the widget host");
        ShowWindow(view.Handle, 4); // A native hide does not reset WinForms' visible state.
        using var legacyRoot = new Form { ShowInTaskbar = false };
        Check(Select(view.Handle, legacyRoot.Handle) == legacyRoot.Handle,
            "legacy redirected desktop retains its container host");
        using var plainView = new Control { Parent = root };
        ShowWindow(plainView.Handle, 4); // SW_SHOWNOACTIVATE; its root remains hidden.
        Check((GetWindowLong(plainView.Handle, -16) & 0x10000000) != 0,
            "nonlayered fixture view has its own visible style beneath the hidden root");
        Check(Select(plainView.Handle, root.Handle) == root.Handle,
            "a nonlayered view does not replace the container host");
    }

    private sealed class NonredirectedDesktopFixture : Form
    {
        internal NonredirectedDesktopFixture()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(1400, 900);
        }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x00200000; return cp; }
        }
    }

    private static void CheckProductionRenderingRecovery(Assembly app, Type widgetType, Type hostType,
        object settings, Icon icon)
    {
        object Host(IntPtr handle) => Activator.CreateInstance(hostType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { handle }, null);
        using var parent = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, ClientSize = new Size(1500, 96) };
        int allocated = 0, presented = 0;
        Func<Size, Bitmap> allocate = size => { allocated++; return new Bitmap(size.Width, size.Height); };
        Action<IntPtr, Bitmap> present = (handle, surface) => { presented++; TaskbarNative.PresentLayered(handle, surface); };
        using var widget = (Form)Activator.CreateInstance(widgetType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { icon, settings, Host(parent.Handle), allocate, present }, null);
        _ = widget.Handle;
        widget.Show(); // Only the child is marked visible; its synthetic parent stays hidden.
        widgetType.GetMethod("SetMenuOpen", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(widget, new object[] { true });
        var refresh = widgetType.GetMethod("RefreshHost", BindingFlags.Instance | BindingFlags.NonPublic);
        refresh.Invoke(widget, new[] { Host(parent.Handle) });
        int initialAllocations = allocated, initialPresentations = presented;
        using var positionMessages = new PositionMessages(widget.Handle);
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        uint gdiBefore = GetGuiResources(process.Handle, 0), userBefore = GetGuiResources(process.Handle, 1);
        process.Refresh();
        long privateBefore = process.PrivateMemorySize64;
        for (int i = 0; i < 1000; i++) refresh.Invoke(widget, new[] { Host(parent.Handle) });
        process.Refresh();
        long privateAfter = process.PrivateMemorySize64;
        uint gdiAfter = GetGuiResources(process.Handle, 0), userAfter = GetGuiResources(process.Handle, 1);
        Check(allocated == initialAllocations && presented == initialPresentations,
            "1000 unchanged production host refreshes allocate no replacement surfaces or native presentation copies");
        Check(positionMessages.Count == 0, "1000 unchanged production refreshes issue no native window-position changes");
        Check(gdiAfter <= gdiBefore + 2 && userAfter <= userBefore + 2,
            "unchanged host refreshes do not grow GDI/USER resources");
        Check(privateAfter <= privateBefore + 16 * 1024 * 1024,
            "unchanged host refreshes keep private memory within a bounded runtime tolerance");
        Console.WriteLine($"UNCHANGED_HOST_RESOURCES: GDI {gdiBefore}->{gdiAfter}, USER {userBefore}->{userAfter}, private {privateBefore}->{privateAfter}; 1000 refreshes.");
        var pointer = widgetType.GetField("_pointer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(widget);
        pointer.GetType().GetMethod("Press", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pointer, new object[] { Point.Empty });
        using (var changedContent = new Bitmap(widget.Width, widget.Height))
            widget.DrawToBitmap(changedContent, new Rectangle(Point.Empty, changedContent.Size));
        Check(allocated > initialAllocations && presented > initialPresentations,
            "content state changes still replace and present the cached surface");
        pointer.GetType().GetMethod("CancelPress", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pointer, null);
        refresh.Invoke(widget, new[] { Host(parent.Handle) });
        int changedAllocations = allocated, changedPresentations = presented;
        ShowWindow(widget.Handle, 0);
        refresh.Invoke(widget, new[] { Host(parent.Handle) });
        Check(allocated == changedAllocations && presented == changedPresentations + 1,
            "native hide restores cached pixels without allocating another Bitmap");
        Check((GetWindowLong(widget.Handle, -16) & 0x10000000) != 0 && !parent.Visible,
            "cached recovery restores child visibility without showing its hidden parent");
        parent.ClientSize = new Size(1500, 120);
        refresh.Invoke(widget, new[] { Host(parent.Handle) });
        Check(presented > changedPresentations + 1, "changed host geometry forces native surface restoration");

        foreach (bool failAllocation in new[] { true, false })
        {
            int failures = 0, attempts = 0;
            Func<Size, Bitmap> brokenAllocation = size =>
            {
                attempts++;
                if (failAllocation) throw new OutOfMemoryException("Injected production allocation failure");
                return new Bitmap(size.Width, size.Height);
            };
            Action<IntPtr, Bitmap> brokenPresent = (_, _) => throw new ExternalException("Injected presentation failure", unchecked((int)0x80263001));
            using var failedWidget = (Form)Activator.CreateInstance(widgetType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { icon, settings, Host(parent.Handle), brokenAllocation, brokenPresent }, null);
            widgetType.GetEvent("HostingFailed", BindingFlags.Instance | BindingFlags.NonPublic).GetAddMethod(true)
                .Invoke(failedWidget, new object[] { (Action<Exception>)(_ => failures++) });
            _ = failedWidget.Handle;
            Check((bool)widgetType.GetProperty("HasRenderFailure", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(failedWidget),
                "production allocation/presentation failure latches rendering safely");
            for (int i = 0; i < 100; i++) refresh.Invoke(failedWidget, new[] { Host(parent.Handle) });
            using var destination = new Bitmap(100, 100);
            failedWidget.DrawToBitmap(destination, new Rectangle(0, 0, 100, 100));
            Check(attempts == 1 && failures == 1, "failed production widget neither reallocates nor emits repeated failure events");
            Check(widgetType.GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(failedWidget) == null,
                "failed production widget releases its owned cached Bitmap");
        }
        Check(!parent.Visible, "failure tests never display a real desktop window");
        foreach (bool failAllocation in new[] { true, false })
        {
            bool armed = false;
            int failureEvents = 0, renderAttempts = 0;
            Func<Size, Bitmap> delayedAllocation = size =>
            {
                renderAttempts++;
                if (armed && failAllocation) throw new OutOfMemoryException("Injected repaint allocation failure");
                return new Bitmap(size.Width, size.Height);
            };
            Action<IntPtr, Bitmap> delayedPresent = (handle, surface) =>
            {
                if (armed) throw new ExternalException("Injected repaint presentation failure", unchecked((int)0x80263001));
                TaskbarNative.PresentLayered(handle, surface);
            };
            using var repaintWidget = (Form)Activator.CreateInstance(widgetType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { icon, settings, Host(parent.Handle), delayedAllocation, delayedPresent }, null);
            widgetType.GetEvent("HostingFailed", BindingFlags.Instance | BindingFlags.NonPublic).GetAddMethod(true)
                .Invoke(repaintWidget, new object[] { (Action<Exception>)(_ => failureEvents++) });
            _ = repaintWidget.Handle;
            armed = true;
            var repaintPointer = widgetType.GetField("_pointer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(repaintWidget);
            repaintPointer.GetType().GetMethod("Press", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(repaintPointer, new object[] { Point.Empty });
            using var destination = new Bitmap(100, 100);
            repaintWidget.DrawToBitmap(destination, new Rectangle(0, 0, 100, 100));
            Check((bool)widgetType.GetProperty("HasRenderFailure", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(repaintWidget) && failureEvents == 1,
                "OnPaint catches a newly injected allocation/presentation failure after a successful initial render");
            int attemptsAfterFailure = renderAttempts;
            for (int i = 0; i < 20; i++) repaintWidget.DrawToBitmap(destination, new Rectangle(0, 0, 100, 100));
            Check(renderAttempts == attemptsAfterFailure && failureEvents == 1,
                "repeated WM_PRINT/paint callbacks cannot retry a latched rendering failure");
            Check(widgetType.GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(repaintWidget) == null,
                "OnPaint failure releases its owned old/new cached surface");
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private sealed class PositionMessages : NativeWindow, IDisposable
    {
        internal int Count { get; private set; }
        internal PositionMessages(IntPtr handle) => AssignHandle(handle);
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0046) Count++; // WM_WINDOWPOSCHANGING
            base.WndProc(ref message);
        }
        public void Dispose() => ReleaseHandle();
    }
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
