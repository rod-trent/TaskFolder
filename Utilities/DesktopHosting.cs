using System;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskFolder.Utilities
{
    // Explorer's desktop tree is an implementation detail. Only use the existing
    // icon tree; never create WorkerW windows or fall back to a top-level overlay.
    internal static class DesktopNative
    {
        internal static TaskbarHost FindHost(TaskbarHost current = null)
        {
            IntPtr shell = GetShellWindow();
            if (shell == IntPtr.Zero) return null;
            TaskbarNative.GetWindowThreadProcessId(shell, out uint shellProcess);
            IntPtr host = IntPtr.Zero;
            bool Inspect(IntPtr window)
            {
                if (TaskbarNative.WindowClass(window) != "SHELLDLL_DefView") return true;
                IntPtr parent = TaskbarNative.GetParent(window);
                string parentClass = TaskbarNative.WindowClass(parent);
                TaskbarNative.GetWindowThreadProcessId(parent, out uint process);
                if (process != shellProcess || parentClass != "Progman" && parentClass != "WorkerW") return true;
                host = SelectHostSurface(window, parent);
                return false;
            }
            // On newer Explorer layouts WorkerW can itself be a child of Progman.
            EnumChildWindows(shell, (window, _) => Inspect(window), IntPtr.Zero);
            if (host == IntPtr.Zero)
                EnumWindows((window, _) =>
                {
                    TaskbarNative.GetWindowThreadProcessId(window, out uint process);
                    if (process == shellProcess && TaskbarNative.WindowClass(window) == "WorkerW")
                        EnumChildWindows(window, (child, unused) => Inspect(child), IntPtr.Zero);
                    return host == IntPtr.Zero;
                }, IntPtr.Zero);
            if (host != IntPtr.Zero)
            {
                try { return new TaskbarHost(host); }
                catch (Win32Exception) { return null; }
            }
            // A transient discovery miss is safe only for the same live desktop tree.
            var retained = current?.RefreshIfCurrent();
            if (retained == null) return null;
            TaskbarNative.GetWindowThreadProcessId(retained.Handle, out uint owner);
            string name = TaskbarNative.WindowClass(retained.Handle);
            if (owner != shellProcess) return null;
            if (name == "Progman" || name == "WorkerW") return retained;
            if (name != "SHELLDLL_DefView") return null;
            IntPtr container = TaskbarNative.GetParent(retained.Handle);
            TaskbarNative.GetWindowThreadProcessId(container, out uint containerProcess);
            string containerClass = TaskbarNative.WindowClass(container);
            return containerProcess == shellProcess && (containerClass == "Progman" || containerClass == "WorkerW")
                ? retained : null;
        }

        internal static IntPtr SelectHostSurface(IntPtr view, IntPtr container)
        {
            const int noRedirectionBitmap = 0x00200000;
            const int visible = 0x10000000;
            // Newer Explorer builds compose the icon view as its own layered surface.
            // A sibling HWND under the nonredirected Progman can report WS_VISIBLE
            // without appearing above that surface. Host within the rendered view.
            // When desktop icons are hidden, use the container instead of hiding our
            // widget with the view. Never show or change styles on Explorer windows.
            return (TaskbarNative.GetWindowLong(container, -20) & noRedirectionBitmap) != 0 &&
                (TaskbarNative.GetWindowLong(view, -20) & TaskbarNative.LayeredWindowStyle) != 0 &&
                (TaskbarNative.GetWindowLong(view, -16) & visible) != 0 ? view : container;
        }

        internal static Rectangle ClientBounds(IntPtr host)
        {
            if (!TaskbarNative.GetClientRect(host, out var client)) throw new Win32Exception();
            Point origin = Point.Empty;
            if (!ClientToScreen(host, ref origin)) throw new Win32Exception();
            return new Rectangle(origin, client.Rectangle.Size);
        }

        internal static Rectangle PlacementArea(TaskbarHost host, string monitor, Point? pointer = null)
        {
            var screens = Screen.AllScreens;
            Screen preferred = pointer.HasValue ? Screen.FromPoint(pointer.Value) :
                screens.FirstOrDefault(s => string.Equals(s.DeviceName, monitor, StringComparison.OrdinalIgnoreCase)) ?? Screen.PrimaryScreen;
            Rectangle desktop = ClientBounds(host.Handle);
            Rectangle area = Rectangle.Intersect(preferred.Bounds, desktop);
            if (area.Width > 0 && area.Height > 0) return area;
            foreach (var screen in screens)
            {
                area = Rectangle.Intersect(screen.Bounds, desktop);
                if (area.Width > 0 && area.Height > 0) return area;
            }
            throw new InvalidOperationException("The desktop host has no visible display area.");
        }

        internal static bool HasSiblingAbove(IntPtr widget) => GetWindow(widget, 3) != IntPtr.Zero; // GW_HWNDPREV

        private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    }
}
