using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TaskFolder.Utilities
{
    internal sealed class TaskbarHost
    {
        internal IntPtr Handle { get; }
        internal Rectangle Bounds { get; }
        internal Size ClientSize { get; }
        internal int Dpi { get; }
        internal string MonitorName { get; }
        internal uint ProcessId { get; }
        private string WindowClass { get; }

        internal TaskbarHost(IntPtr handle)
        {
            Handle = handle;
            if (!TaskbarNative.IsWindow(handle) || !TaskbarNative.GetWindowRect(handle, out var window) ||
                !TaskbarNative.GetClientRect(handle, out var client))
                throw new Win32Exception("The widget host is no longer available.");
            Bounds = window.Rectangle;
            ClientSize = client.Rectangle.Size;
            Dpi = Math.Max(96, (int)TaskbarNative.GetDpiForWindow(handle));
            MonitorName = Screen.FromHandle(handle).DeviceName;
            TaskbarNative.GetWindowThreadProcessId(handle, out uint process);
            ProcessId = process;
            WindowClass = TaskbarNative.WindowClass(handle);
        }

        internal TaskbarHost RefreshIfCurrent()
        {
            bool StillCurrent()
            {
                TaskbarNative.GetWindowThreadProcessId(Handle, out uint process);
                return TaskbarNative.IsWindow(Handle) && process == ProcessId &&
                    TaskbarNative.WindowClass(Handle) == WindowClass;
            }
            if (!StillCurrent()) return null;
            try { return new TaskbarHost(Handle); }
            catch (Win32Exception)
            {
                // A failed geometry query is not evidence that Explorer destroyed the host.
                return StillCurrent() ? this : null;
            }
        }
    }

    internal static class TaskbarLayout
    {
        internal const int MaximumDpi = 1536;
        internal const long MaximumSurfaceBytes = 4L * 1024 * 1024;

        internal static void ValidateSurface(Size size, int dpi)
        {
            if (dpi < 96 || dpi > MaximumDpi || size.Width < 1 || size.Height < 1 ||
                size.Width > 1024 || size.Height > 1024 ||
                checked((long)size.Width * size.Height * 4) > MaximumSurfaceBytes)
                throw new ArgumentOutOfRangeException(nameof(size), "Invalid TaskFolder widget surface geometry.");
        }

        internal static bool Horizontal(Size host) => host.Width >= host.Height;

        internal static Size WidgetSize(Size host, int dpi, int sizePercent = 100)
        {
            if (dpi < 96 || dpi > MaximumDpi)
                throw new ArgumentOutOfRangeException(nameof(dpi));
            int gap = Math.Max(1, (int)Math.Round(2 * dpi / 96.0));
            int side = Math.Min(1024, (int)Math.Round(44 * dpi / 96.0 * Math.Clamp(sizePercent, 50, 200) / 100.0));
            return new Size(Math.Max(1, Math.Min(side, host.Width - gap * 2)),
                Math.Max(1, Math.Min(side, host.Height - gap * 2)));
        }

        internal static Point Restore(Size host, Size widget, double fraction)
        {
            double position = double.IsFinite(fraction) ? Math.Clamp(fraction, 0, 1) : 0.2;
            int x = Math.Max(0, host.Width - widget.Width);
            int y = Math.Max(0, host.Height - widget.Height);
            return Horizontal(host) ? new Point((int)Math.Round(x * position), y / 2)
                : new Point(x / 2, (int)Math.Round(y * position));
        }

        internal static Point ClampDrag(Size host, Size widget, Point point)
        {
            int x = Math.Max(0, host.Width - widget.Width);
            int y = Math.Max(0, host.Height - widget.Height);
            return Horizontal(host) ? new Point(Math.Clamp(point.X, 0, x), y / 2)
                : new Point(x / 2, Math.Clamp(point.Y, 0, y));
        }

        internal static double Normalize(Size host, Size widget, Point point) => Horizontal(host)
            ? Math.Clamp((double)point.X / Math.Max(1, host.Width - widget.Width), 0, 1)
            : Math.Clamp((double)point.Y / Math.Max(1, host.Height - widget.Height), 0, 1);
    }

    // Four failures for an unchanged environment: initial attempt and three bounded retries.
    // Only a real host/display/session change or an explicit user request rearms a latched failure.
    internal sealed class WidgetRecoveryState
    {
        private (IntPtr Handle, uint Process, int Dpi, Size Size)? _environment;
        private long _retryAt;
        internal int Failures { get; private set; }
        internal bool Exhausted => Failures >= 4;
        internal bool CanAttempt(long now) => !Exhausted && now >= _retryAt;
        internal void Reset() { Failures = 0; _retryAt = 0; }
        internal void ObserveHost(IntPtr handle, uint process, int dpi, Size size)
        {
            var key = (handle, process, dpi, size);
            if (_environment.HasValue && _environment.Value != key) Reset();
            _environment = key;
        }
        internal void Failed(long now)
        {
            if (Exhausted) return;
            Failures++;
            _retryAt = now + (Failures == 1 ? 1000 : Failures == 2 ? 2000 : 5000);
        }
    }

    internal static class TaskbarNative
    {
        internal const int ChildStyle = 0x40000000;
        internal const int PopupStyle = unchecked((int)0x80000000);
        internal const int ToolWindowStyle = 0x80;
        internal const int AppWindowStyle = 0x40000;
        internal const int LayeredWindowStyle = 0x80000;

        internal static bool IsGraphicsFailure(Exception error) => error is Win32Exception ||
            error is OutOfMemoryException || error is ExternalException ||
            error is InvalidOperationException || error is ArgumentException;

        internal static Bitmap CreateSurface(Size size, int dpi, Action<Graphics> draw,
            Func<Size, Bitmap> allocate = null)
        {
            TaskbarLayout.ValidateSurface(size, dpi);
            Bitmap surface = allocate == null
                ? new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb)
                : allocate(size);
            try
            {
                using var graphics = Graphics.FromImage(surface);
                draw(graphics);
                return surface;
            }
            catch
            {
                // Ownership transfers only on successful return, including failures in FromImage/draw.
                surface.Dispose();
                throw;
            }
        }

        internal static List<TaskbarHost> FindHosts()
        {
            var hosts = new List<TaskbarHost>();
            var seen = new HashSet<IntPtr>();
            // Query the shell classes directly instead of depending on a generic
            // desktop-window enumeration during Start/taskbar transitions.
            foreach (string windowClass in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr window = IntPtr.Zero;
                while ((window = FindWindowEx(IntPtr.Zero, window, windowClass, null)) != IntPtr.Zero)
                {
                    if (!seen.Add(window)) break; // A concurrent shell rebuild must not loop forever.
                    try { hosts.Add(new TaskbarHost(window)); }
                    catch (Win32Exception) { /* Explorer changed during lookup. */ }
                }
            }
            return hosts;
        }

        internal static TaskbarHost PreferredHost(string monitor, TaskbarHost current = null, Action<string> diagnostic = null)
        {
            var discovered = FindHosts();
            var selected = SelectHost(discovered, monitor, current);
            if (discovered.Count == 0 && selected != null)
                diagnostic?.Invoke($"Taskbar lookup temporarily empty; retaining live host HWND={selected.Handle}, process={selected.ProcessId}.");
            return selected;
        }

        internal static TaskbarHost SelectHost(IEnumerable<TaskbarHost> discovered, string monitor, TaskbarHost current) => discovered
            .OrderByDescending(h => string.Equals(h.MonitorName, monitor, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(h => Screen.FromHandle(h.Handle).Primary).FirstOrDefault()
            ?? current?.RefreshIfCurrent();

        internal static string WindowClass(IntPtr window)
        {
            var name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            return name.ToString();
        }

        internal static void Attach(IntPtr widget, IntPtr parent)
        {
            int oldStyle = GetWindowLong(widget, -16);
            SetStyle(widget, -16, (oldStyle & ~PopupStyle) | ChildStyle);
            SetStyle(widget, -20, (GetWindowLong(widget, -20) | ToolWindowStyle) & ~AppWindowStyle);
            SetParent(widget, parent);
            if (GetParent(widget) != parent)
            {
                int error = Marshal.GetLastWin32Error();
                SetStyle(widget, -16, oldStyle);
                throw new Win32Exception(error, "Windows could not attach the TaskFolder widget to its host.");
            }
            // The widget supplies a per-pixel alpha surface through PresentLayered.
            SetStyle(widget, -20, GetWindowLong(widget, -20) | LayeredWindowStyle);
        }

        internal static void PresentLayered(IntPtr widget, Bitmap surface)
        {
            IntPtr memoryDc = CreateCompatibleDC(IntPtr.Zero);
            if (memoryDc == IntPtr.Zero) throw new Win32Exception();
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            try
            {
                bitmap = surface.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memoryDc, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception();
                var size = surface.Size;
                var origin = Point.Empty;
                var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 }; // AC_SRC_ALPHA
                // Keep the current native position; SetWindowPos handles parent-client coordinates.
                if (!UpdateLayeredWindow(widget, IntPtr.Zero, IntPtr.Zero, ref size,
                    memoryDc, ref origin, 0, ref blend, 2)) // ULW_ALPHA
                    throw new Win32Exception();
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memoryDc, previous);
                // Release the DC first so the owned bitmap is deletable even if restoring its selection failed.
                DeleteDC(memoryDc);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            }
        }

        internal static Rectangle ScreenBounds(IntPtr window)
        {
            if (!GetWindowRect(window, out var rect)) return Rectangle.Empty;
            return rect.Rectangle;
        }

        internal static Point ToClient(IntPtr parent, Point screen)
        {
            if (!ScreenToClient(parent, ref screen)) throw new Win32Exception();
            return screen;
        }

        internal static void MoveChild(IntPtr widget, Point location, Size size, bool show = false)
        {
            // Raise only our child above host siblings, never alter Explorer's controls.
            // Moving/redrawing alone does not restore WS_VISIBLE after a native hide.
            // Only the ready widget's recovery path requests SHOWWINDOW; creation stays hidden.
            uint flags = 0x0010; // NOACTIVATE, HWND_TOP; no frame recalculation on drag/watch ticks.
            if (show) flags |= 0x0040; // SHOWWINDOW; never show or activate the host.
            if (!SetWindowPos(widget, IntPtr.Zero, location.X, location.Y, size.Width, size.Height,
                flags))
                throw new Win32Exception();
        }

        private static void SetStyle(IntPtr window, int index, int style)
        {
            // The style value is 32 bits on both architectures; only HWND/pointers are pointer-sized.
            SetWindowLong(window, index, style);
            if (GetWindowLong(window, index) != style) throw new Win32Exception();
        }

        internal sealed class DpiScope : IDisposable
        {
            private readonly IntPtr _previous;
            internal DpiScope(IntPtr parent)
            {
                _previous = SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(parent));
                if (_previous == IntPtr.Zero) throw new Win32Exception();
            }
            public void Dispose() => SetThreadDpiAwarenessContext(_previous);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRect
        {
            public int Left, Top, Right, Bottom;
            internal Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetClientRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window,
            IntPtr destinationDc, IntPtr destination, ref Size size, IntPtr sourceDc, ref Point source,
            uint colorKey, ref BlendFunction blend, uint flags);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(IntPtr parent, ref Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
