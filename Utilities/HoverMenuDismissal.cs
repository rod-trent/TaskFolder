using System;
using System.Drawing;
using System.Windows.Forms;

namespace TaskFolder.Utilities
{
    // Enabled only for a root menu opened by widget hover. Pointer polling covers
    // hosted editors and detached submenu HWNDs without racing MouseLeave events.
    internal sealed class HoverMenuDismissal : IMessageFilter, IDisposable
    {
        private readonly Timer _timer = new Timer { Interval = 50 };
        private readonly Func<Point, bool> _containsPoint;
        private readonly Func<bool> _visible;
        private readonly Action _close;
        private long? _leftAt;
        private int _delay;
        internal bool Active { get; private set; }

        internal HoverMenuDismissal(Func<Point, bool> containsPoint, Func<bool> visible, Action close)
        {
            _containsPoint = containsPoint;
            _visible = visible;
            _close = close;
            _timer.Tick += (_, _) => Poll(Environment.TickCount64, Control.MousePosition, Control.MouseButtons != MouseButtons.None);
        }

        internal void Start(int delayMs)
        {
            Stop();
            _delay = Math.Clamp(delayMs, 200, 2000);
            Active = true;
            Application.AddMessageFilter(this);
            _timer.Start();
        }

        internal void Stop()
        {
            if (Active) Application.RemoveMessageFilter(this);
            Active = false;
            _leftAt = null;
            _timer.Stop();
        }

        internal void Poll(long now, Point point, bool mouseDown = false)
        {
            if (!Active) return;
            if (!_visible()) { Stop(); return; }
            if (mouseDown || _containsPoint(point)) { _leftAt = null; return; }
            _leftAt ??= now;
            if (now - _leftAt.Value < _delay) return;
            Stop(); // No stale timer can close a subsequent menu session.
            _close();
        }

        public bool PreFilterMessage(ref Message message)
        {
            // Typing, IME and keyboard navigation opt into normal menu lifetime.
            // Right-click also keeps shortcut context actions free of a leave timer.
            if (message.Msg is 0x0100 or 0x0102 or 0x0104 or 0x010D or 0x0204 or 0x0205)
                Stop();
            return false;
        }

        public void Dispose() { Stop(); _timer.Dispose(); }
    }
}
