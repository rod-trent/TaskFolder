using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskFolder.Utilities
{
    // Root-menu lifetime only. Does not depend on successful foreground activation.
    internal sealed class OutsideClickMenuDismissal : IDisposable
    {
        private readonly Func<Point, bool> _containsPoint;
        private readonly Action<Action> _enqueue;
        private readonly Action _close;
        private readonly Func<Action<Point>, IDisposable> _listen;
        private IDisposable _listener;
        private long _session;
        private bool _active;
        private bool _queued;

        internal OutsideClickMenuDismissal(Func<Point, bool> containsPoint, Action<Action> enqueue,
            Action close, Func<Action<Point>, IDisposable> listen = null)
        {
            _containsPoint = containsPoint;
            _enqueue = enqueue;
            _close = close;
            _listen = listen ?? (callback => new MouseDownListener(callback));
        }

        internal void Start()
        {
            Stop();
            long session = _session;
            _active = true;
            try { _listener = _listen(point => OnMouseDown(point, session)); }
            catch { Stop(); throw; }
        }

        internal void Stop()
        {
            _active = false;
            _queued = false;
            _session++;
            _listener?.Dispose();
            _listener = null;
        }

        private void OnMouseDown(Point point, long session)
        {
            if (!_active || session != _session || _queued || _containsPoint(point)) return;
            _queued = true;
            try
            {
                // Never close/rebuild a menu synchronously inside a native mouse hook.
                _enqueue(() =>
                {
                    if (!_active || session != _session) return;
                    _queued = false;
                    _close();
                });
            }
            catch (InvalidOperationException) { Stop(); } // UI handle is shutting down.
        }

        internal static bool ContainsPoint(Point point, IEnumerable<Rectangle> regions)
        {
            foreach (var region in regions)
                if (region.Contains(point)) return true;
            return false;
        }

        internal static IEnumerable<Rectangle> VisibleMenuBounds(ToolStripDropDown menu)
        {
            if (menu.IsDisposed || !menu.Visible) yield break;
            yield return menu.Bounds;
            foreach (ToolStripItem item in menu.Items)
            {
                if (item is not ToolStripDropDownItem folder || !folder.HasDropDownItems || !folder.DropDown.Visible)
                    continue;
                foreach (var bounds in VisibleMenuBounds(folder.DropDown)) yield return bounds;
            }
        }

        internal static bool IsButtonDown(int message) =>
            message is 0x0201 or 0x0204 or 0x0207 or 0x020B; // Left, right, middle, X buttons.

        public void Dispose() => Stop();

        private sealed class MouseDownListener : IDisposable
        {
            private readonly HookCallback _callback;
            private IntPtr _hook;

            internal MouseDownListener(Action<Point> onMouseDown)
            {
                _callback = (code, message, data) =>
                {
                    if (code >= 0 && IsButtonDown(unchecked((int)message.ToInt64())))
                    {
                        try { onMouseDown(new Point(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4))); }
                        catch (Exception ex) { Debug.WriteLine("TaskFolder outside-click listener: " + ex.Message); }
                    }
                    // Observe only: never swallow or inject a click.
                    return CallNextHookEx(_hook, code, message, data);
                };
                _hook = SetWindowsHookEx(14, _callback, GetModuleHandle(null), 0); // WH_MOUSE_LL
                if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            public void Dispose()
            {
                if (_hook == IntPtr.Zero) return;
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }

            private delegate IntPtr HookCallback(int code, IntPtr message, IntPtr data);
            [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr SetWindowsHookEx(int hook, HookCallback callback, IntPtr module, uint threadId);
            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnhookWindowsHookEx(IntPtr hook);
            [DllImport("user32.dll")]
            private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr GetModuleHandle(string module);
        }
    }
}
