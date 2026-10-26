using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskFolder.Utilities
{
    internal static class MenuMotion
    {
        internal const int OpenDurationMs = 160;
        internal const int CloseDurationMs = 100;
        internal const double InitialOpacity = 0.02;

        internal static bool Enabled => SystemInformation.IsMenuAnimationEnabled && !SystemInformation.HighContrast &&
            SystemParametersInfo(0x1042, 0, out bool animate, 0) && animate; // SPI_GETCLIENTAREAANIMATION, read only

        internal static double OpacityAt(long elapsed, int duration, double from, double to)
        {
            double t = Math.Clamp((double)elapsed / Math.Max(1, duration), 0, 1);
            double remaining = 1 - t;
            return from + (to - from) * (1 - remaining * remaining * remaining);
        }

        internal static bool KeepRootOpen(ToolStripDropDownCloseReason reason, bool nested, bool visible,
            IntPtr foregroundAtOpen, IntPtr foregroundNow, bool foregroundIsMenu) =>
            reason == ToolStripDropDownCloseReason.AppFocusChange && !nested && visible &&
            (foregroundIsMenu || foregroundAtOpen != IntPtr.Zero && foregroundNow == foregroundAtOpen);

        internal static void OpenDialogAfterMenu(ToolStripDropDown menu, Action showDialog)
        {
            menu.Close(ToolStripDropDownCloseReason.CloseCalled);
            MenuFadeWindow.CancelAll();
            // Let WinForms finish releasing menu mode and its transient owner.
            menu.BeginInvoke(showDialog);
        }

        internal static bool SetOpacity(IntPtr window, double opacity) =>
            SetLayeredWindowAttributes(window, 0, (byte)(Math.Clamp(opacity, 0, 1) * 255), 2);

        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetLayeredWindowAttributes(IntPtr window, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SystemParametersInfo(uint action, uint parameter,
            [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
    }

    // The real menu closes immediately and releases focus/menu mode. Only an inert
    // image fades out; it cannot intercept the outside click or delay a launch.
    internal sealed class MenuFadeWindow : Form
    {
        private static readonly List<MenuFadeWindow> Active = new List<MenuFadeWindow>();
        private readonly Timer _timer = new Timer { Interval = 15 };
        private Bitmap _image;
        private readonly double _initialOpacity;
        private long _started;

        internal static void CancelAll()
        {
            foreach (var fade in Active.ToArray()) fade.Dispose();
        }

        internal static MenuFadeWindow CreateSnapshot(ToolStripDropDown menu)
        {
            // Do not allocate a bitmap for an invisible/oversized/disposal surface.
            if (!menu.Visible || !menu.IsHandleCreated || menu.IsDisposed || menu.Disposing ||
                menu.Width < 1 || menu.Height < 1 || (long)menu.Width * menu.Height > 4 * 1024 * 1024)
                return null;
            Bitmap image = null;
            MenuFadeWindow fade = null;
            try
            {
                image = new Bitmap(menu.Width, menu.Height);
                menu.DrawToBitmap(image, new Rectangle(Point.Empty, menu.Size));
                double opacity = menu is Windows11ContextMenuStrip animated ? animated.VisualOpacity : menu.Opacity;
                fade = new MenuFadeWindow(image, menu.Bounds, opacity, menu.Region);
                image = null; // Ownership transferred to the window.
                return fade;
            }
            catch
            {
                image?.Dispose();
                fade?.Dispose();
                throw;
            }
        }

        internal MenuFadeWindow(Bitmap image, Rectangle bounds, double opacity, Region region)
        {
            _image = image;
            // Keep WS_EX_LAYERED active from the first frame, including a fully
            // opaque source menu, so cross-process click-through is immediate.
            _initialOpacity = Math.Clamp(opacity, MenuMotion.InitialOpacity, 0.999);
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            Opacity = _initialOpacity;
            if (region != null) Region = region.Clone();
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            _timer.Tick += (_, _) => Advance(Environment.TickCount64);
            Active.Add(this);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // NOACTIVATE | TOOLWINDOW | TRANSPARENT; layered transparency makes
                // mouse input pass through even to windows belonging to another app.
                parameters.ExStyle = (parameters.ExStyle | 0x08000000 | 0x80 | 0x20 | 0x80000) & ~0x40000;
                return parameters;
            }
        }

        internal void Begin()
        {
            if (IsDisposed) return; // A newly opened menu may already have cancelled this fade.
            if (!MenuMotion.Enabled) { Dispose(); return; }
            _started = Environment.TickCount64;
            Show();
            _timer.Start();
        }

        internal void Advance(long now)
        {
            long elapsed = now - _started;
            if (!MenuMotion.Enabled || elapsed >= MenuMotion.CloseDurationMs) { Dispose(); return; }
            Opacity = MenuMotion.OpacityAt(elapsed, MenuMotion.CloseDurationMs, _initialOpacity, 0);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_image != null) e.Graphics.DrawImageUnscaled(_image, Point.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            Region region = disposing ? Region : null;
            if (disposing)
            {
                _timer.Dispose();
                _image?.Dispose();
                _image = null;
                Active.Remove(this);
            }
            base.Dispose(disposing);
            region?.Dispose();
        }
    }
}
