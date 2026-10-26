using System;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TaskFolder.Utilities;

internal static class MenuMotionSmoke
{
    internal static void Run(Action<bool, string> check)
    {
        foreach (var endpoints in new[] { (From: .02, To: 1.0, Duration: 160), (From: 1.0, To: 0.0, Duration: 100), (From: .35, To: 0.0, Duration: 100) })
        {
            check(MenuMotion.OpacityAt(-10, endpoints.Duration, endpoints.From, endpoints.To) == endpoints.From, "Fade began before its start time.");
            check(MenuMotion.OpacityAt(endpoints.Duration, endpoints.Duration, endpoints.From, endpoints.To) == endpoints.To, "Fade did not finish at its deadline.");
            check(MenuMotion.OpacityAt(999, endpoints.Duration, endpoints.From, endpoints.To) == endpoints.To, "Delayed frame overshot the final opacity.");
            double previous = endpoints.From;
            for (int elapsed = 0; elapsed <= endpoints.Duration; elapsed += 5)
            {
                double opacity = MenuMotion.OpacityAt(elapsed, endpoints.Duration, endpoints.From, endpoints.To);
                check(opacity >= Math.Min(endpoints.From, endpoints.To) && opacity <= Math.Max(endpoints.From, endpoints.To), "Fade opacity escaped its bounds.");
                check(endpoints.To > endpoints.From ? opacity >= previous : opacity <= previous, "Fade reversed direction.");
                previous = opacity;
            }
            double halfway = MenuMotion.OpacityAt(endpoints.Duration / 2, endpoints.Duration, endpoints.From, endpoints.To);
            check(Math.Abs(halfway - (endpoints.From + (endpoints.To - endpoints.From) * .875)) < 1e-9, "Fade lost its eased timing.");
        }

        using var menu = new Windows11ContextMenuStrip(new Windows11MenuRenderer(), Windows11MenuStyle.CreateFont(10));
        menu.Items.Add("Synthetic item");
        _ = menu.Handle;
        void Invoke(string method, object args) => typeof(Windows11ContextMenuStrip).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(menu, new[] { args });
        CancelEventHandler cancel = (_, args) => args.Cancel = true;
        menu.Opening += cancel;
        var opening = new CancelEventArgs();
        Invoke("OnOpening", opening);
        check(opening.Cancel && menu.VisualOpacity == 1, "Cancelled opening changed opacity.");
        menu.Opening -= cancel;
        Invoke("OnOpening", new CancelEventArgs());
        check(menu.VisualOpacity == (MenuMotion.Enabled ? MenuMotion.InitialOpacity : 1), "Initial opacity does not follow the Windows animation preference.");
        IntPtr menuHandle = menu.Handle;
        int nativeStyle = TaskbarNative.GetWindowLong(menuHandle, -20);
        int styleChanges = 0;
        menu.StyleChanged += (_, _) => styleChanges++;
        foreach (double alpha in new[] { .02, .4, .9, 1.0, .02, 1.0 })
        {
            menu.PresentOpacity(alpha);
            check(menu.Handle == menuHandle && TaskbarNative.GetWindowLong(menuHandle, -20) == nativeStyle,
                "Fade completion changed the active menu HWND/style.");
        }
        check((nativeStyle & 0x80000) != 0 && styleChanges == 0, "Opacity frames updated WinForms window styles.");
        Invoke("OnOpened", EventArgs.Empty);
        menu.AdvanceOpening(long.MaxValue);
        check(!menu.Visible && menu.VisualOpacity == 1, "Hidden menu did not stop its opening animation.");
        foreach (var reason in new[] { ToolStripDropDownCloseReason.AppClicked, ToolStripDropDownCloseReason.ItemClicked, ToolStripDropDownCloseReason.Keyboard, ToolStripDropDownCloseReason.CloseCalled })
        {
            var closing = new ToolStripDropDownClosingEventArgs(reason);
            Invoke("OnClosing", closing);
            check(!closing.Cancel, "Animation delayed/cancelled an accepted close: " + reason);
            Invoke("OnClosed", new ToolStripDropDownClosedEventArgs(reason));
            check(menu.VisualOpacity == 1 && !menu.Visible, "Closing left a transparent or visible reusable menu.");
        }
        ToolStripDropDownClosingEventHandler cancelClose = (_, args) => args.Cancel = true;
        menu.Closing += cancelClose;
        var cancelled = new ToolStripDropDownClosingEventArgs(ToolStripDropDownCloseReason.AppClicked);
        Invoke("OnClosing", cancelled);
        check(cancelled.Cancel, "Animation overrode a consumer's close cancellation.");
        menu.Closing -= cancelClose;
        check(MenuMotion.KeepRootOpen(ToolStripDropDownCloseReason.AppFocusChange, false, true, new IntPtr(1), new IntPtr(1), false),
            "Passive focus bookkeeping dismissed a root whose foreground window did not change.");
        check(MenuMotion.KeepRootOpen(ToolStripDropDownCloseReason.AppFocusChange, false, true, new IntPtr(1), new IntPtr(2), true),
            "Focus within the visible menu hierarchy dismissed the root.");
        check(!MenuMotion.KeepRootOpen(ToolStripDropDownCloseReason.AppFocusChange, false, true, new IntPtr(1), new IntPtr(2), false),
            "Changing to another application no longer dismisses the menu.");
        check(!MenuMotion.KeepRootOpen(ToolStripDropDownCloseReason.AppFocusChange, true, true, new IntPtr(1), new IntPtr(1), false),
            "Sibling submenu navigation was incorrectly cancelled.");
        foreach (var reason in new[] { ToolStripDropDownCloseReason.AppClicked, ToolStripDropDownCloseReason.ItemClicked, ToolStripDropDownCloseReason.Keyboard, ToolStripDropDownCloseReason.CloseCalled })
            check(!MenuMotion.KeepRootOpen(reason, false, true, new IntPtr(1), new IntPtr(1), true), "Explicit dismissal was blocked: " + reason);
        bool dialogInvoked = false;
        MenuMotion.OpenDialogAfterMenu(menu, () => dialogInvoked = true);
        check(!dialogInvoked, "Settings opened reentrantly inside menu closing.");
        Application.DoEvents();
        check(dialogInvoked, "Settings was not dispatched after menu closing completed.");

        IntPtr foreground = GetForegroundWindow();
        Bitmap image = new Bitmap(64, 64);
        using var region = new Region(new Rectangle(0, 0, 64, 64));
        using var fade = new MenuFadeWindow(image, new Rectangle(20, 20, 64, 64), 1, region);
        int style = TaskbarNative.GetWindowLong(fade.Handle, -20);
        check((style & 0x080800A0) == 0x080800A0 && (style & 0x40000) == 0 && !fade.ShowInTaskbar,
            "Fade image can activate, intercept clicks, or register an app button.");
        check(!fade.Visible && fade.Region != region, "Fade construction displayed a window or borrowed the source region.");
        var hit = Message.Create(fade.Handle, 0x0084, IntPtr.Zero, IntPtr.Zero);
        var wndProc = typeof(MenuFadeWindow).GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic);
        object[] message = { hit }; wndProc.Invoke(fade, message);
        check(((Message)message[0]).Result == new IntPtr(-1), "Fade image is not hit-test transparent.");
        fade.Advance(long.MaxValue);
        check(fade.IsDisposed && GetForegroundWindow() == foreground, "Fade completion leaked a window or stole focus.");
        bool bitmapDisposed = false;
        try { _ = image.GetPixel(0, 0); } catch (ArgumentException) { bitmapDisposed = true; }
        check(bitmapDisposed, "Fade window did not release its owned image.");

        using var first = new MenuFadeWindow(new Bitmap(8, 8), new Rectangle(0, 0, 8, 8), .5, null);
        using var second = new MenuFadeWindow(new Bitmap(8, 8), new Rectangle(0, 0, 8, 8), .5, null);
        MenuFadeWindow.CancelAll();
        check(first.IsDisposed && second.IsDisposed, "A new menu cannot clear old fade images.");
        check(MenuFadeWindow.CreateSnapshot(menu) == null, "A hidden menu allocated a closing image.");
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        uint gdiBefore = GetGuiResources(process.Handle, 0), userBefore = GetGuiResources(process.Handle, 1);
        for (int i = 0; i < 64; i++)
        {
            using var snapshot = new MenuFadeWindow(new Bitmap(32, 32), new Rectangle(0, 0, 32, 32), .5, region);
            _ = snapshot.Handle;
        }
        check(GetGuiResources(process.Handle, 0) <= gdiBefore + 2 && GetGuiResources(process.Handle, 1) <= userBefore + 2,
            "Repeated hidden fade windows leaked GDI/USER resources.");
        Console.WriteLine("PASS menu-motion: eased time-based opacity, Windows preference, accepted/cancelled close lifecycle, reusable opacity, hidden no-activate/click-through snapshot, image disposal and reopen cleanup. No visible menu was shown.");
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
}
