using System;
using System.Drawing;

namespace TaskFolder.Utilities
{
    internal enum WidgetRelease { None, Click, Drag }

    // Pure pointer state shared by the real widget and deterministic regression cases.
    internal sealed class WidgetInteraction
    {
        private bool _inside;
        private bool _hoverConsumed;
        private bool _blockedUntilExit;
        private long _entered;
        private Point _pressOrigin;
        private bool _locked;
        private int _hoverDelay = 300;
        internal bool Pressed { get; private set; }
        internal bool Dragging { get; private set; }
        internal int HoverDelay => _hoverDelay;

        internal void Configure(bool positionLocked, int hoverDelayMs)
        {
            int delay = Math.Clamp(hoverDelayMs, 100, 1500);
            if ((_locked != positionLocked || _hoverDelay != delay) && Pressed) CancelPress();
            _locked = positionLocked; _hoverDelay = delay;
        }

        internal void Enter(long now)
        {
            if (_inside) return;
            _inside = true;
            _entered = now;
            _hoverConsumed = false;
        }

        internal void Leave(bool physicallyOutside = true)
        {
            _inside = false;
            _hoverConsumed = false;
            if (physicallyOutside) _blockedUntilExit = false;
        }

        internal void ConsumeHover() => _hoverConsumed = true;
        internal void BlockUntilExit() { _blockedUntilExit = true; ConsumeHover(); }

        internal bool HoverDue(long now, bool enabled, bool menuOpen)
        {
            if (!_inside || Pressed || _hoverConsumed || _blockedUntilExit || !enabled || menuOpen || now - _entered < _hoverDelay)
                return false;
            _hoverConsumed = true;
            return true;
        }

        internal void Press(Point screenPoint)
        {
            Pressed = true;
            Dragging = false;
            _pressOrigin = screenPoint;
            ConsumeHover();
        }

        internal bool Move(Point screenPoint, Size dragSize)
        {
            if (!_locked && Pressed && (Math.Abs((long)screenPoint.X - _pressOrigin.X) > dragSize.Width / 2 ||
                Math.Abs((long)screenPoint.Y - _pressOrigin.Y) > dragSize.Height / 2))
                Dragging = true;
            return Dragging;
        }

        internal WidgetRelease Release(bool inside)
        {
            if (!Pressed) return WidgetRelease.None;
            var result = Dragging ? WidgetRelease.Drag : inside ? WidgetRelease.Click : WidgetRelease.None;
            CancelPress();
            return result;
        }

        internal void CancelPress()
        {
            Pressed = false;
            Dragging = false;
            ConsumeHover();
        }
    }

    internal static class WidgetPlacement
    {
        internal static Point Clamp(Point point, Size widgetSize, Rectangle display)
        {
            return new Point(Math.Clamp(point.X, display.Left, Math.Max(display.Left, display.Right - widgetSize.Width)),
                Math.Clamp(point.Y, display.Top, Math.Max(display.Top, display.Bottom - widgetSize.Height)));
        }

        internal static Point Restore(Rectangle display, Size widgetSize, double x, double y)
        {
            double normalizedX = double.IsFinite(x) ? Math.Clamp(x, 0, 1) : 0.5;
            double normalizedY = double.IsFinite(y) ? Math.Clamp(y, 0, 1) : 0.5;
            return new Point(display.Left + (int)Math.Round(Math.Max(0, display.Width - widgetSize.Width) * normalizedX),
                display.Top + (int)Math.Round(Math.Max(0, display.Height - widgetSize.Height) * normalizedY));
        }

        internal static (double X, double Y) Normalize(Rectangle display, Rectangle widget)
        {
            return (Math.Clamp((double)(widget.Left - display.Left) / Math.Max(1, display.Width - widget.Width), 0, 1),
                Math.Clamp((double)(widget.Top - display.Top) / Math.Max(1, display.Height - widget.Height), 0, 1));
        }
    }
}
