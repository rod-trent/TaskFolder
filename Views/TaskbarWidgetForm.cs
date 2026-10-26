using L = TaskFolder.Services.LocalizationService;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Utilities;

namespace TaskFolder.Views
{
    // Desktop child window. The legacy class name does not imply taskbar hosting.
    internal sealed class TaskbarWidgetForm : Form
    {
        private readonly AppSettings _settings;
        private readonly Bitmap _icon;
        private readonly Timer _hoverTimer = new Timer { Interval = 30 };
        private readonly ToolTip _tip = new ToolTip();
        private readonly WidgetInteraction _pointer = new WidgetInteraction();
        private Point _dragOffset;
        private bool _hoverEnabled;
        private bool _menuOpen;
        private bool _releasingCapture;
        private bool _ready;
        private bool _attached;
        private IntPtr _nativeHandle;
        private TaskbarHost _host;
        private readonly Func<Size, Bitmap> _allocateSurface;
        private readonly Action<IntPtr, Bitmap> _presentSurface;
        private Bitmap _surface;
        private Bitmap _presentedSurface;
        private (Size Size, int Dpi, int Background, bool Focused, int SizePercent)? _surfaceKey;
        private bool _renderFailed;

        internal TaskbarHost Host => _host;
        internal IntPtr NativeHandle => _nativeHandle;
        internal Rectangle ScreenBounds => TaskbarNative.ScreenBounds(_nativeHandle);
        internal int RenderDpi => _host.Dpi;
        internal bool IsDragging => _pointer.Pressed;
        internal bool HasRenderFailure => _renderFailed;

        internal event EventHandler OpenRequested;
        internal event EventHandler HoverOpenRequested;
        internal event EventHandler InteractionStarted;
        internal event EventHandler PositionCommitted;
        internal event EventHandler SettingsRequested;
        internal event Action<Exception> HostingFailed;

        internal TaskbarWidgetForm(Icon icon, AppSettings settings, TaskbarHost host)
            : this(icon, settings, host, null, null) { }

        internal TaskbarWidgetForm(Icon icon, AppSettings settings, TaskbarHost host,
            Func<Size, Bitmap> allocateSurface, Action<IntPtr, Bitmap> presentSurface)
        {
            _allocateSurface = allocateSurface;
            _presentSurface = presentSurface ?? TaskbarNative.PresentLayered;
            _settings = settings;
            _pointer.Configure(settings.WidgetPositionLocked, settings.WidgetHoverDelayMs);
            _host = host;
            using var drawingIcon = new Icon(icon, 128, 128);
            _icon = drawingIcon.ToBitmap();
            Text = "TaskFolder";
            AccessibleName = "TaskFolder";
            AccessibleDescription = "Click to open shortcuts. Drag to move the widget.";
            AccessibleRole = AccessibleRole.PushButton;
            ShowInTaskbar = false;
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            // This owner-drawn surface scales its size and drawing together in DPI callbacks.
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(44, 44);
            TopMost = false;
            BackColor = Color.Black; // Background paint is suppressed; the alpha surface is authoritative.
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            _tip.SetToolTip(this, "TaskFolder: click to open, drag to move. Right-click for Settings.");
            _hoverTimer.Tick += (_, _) =>
            {
                ApplyPreferences();
                if (!ScreenBounds.Contains(MousePosition) || TaskbarNative.WindowFromPoint(MousePosition) != _nativeHandle)
                { PointerLeft(); return; }
                if (_pointer.HoverDue(Environment.TickCount64, _hoverEnabled, _menuOpen))
                {
                    _hoverTimer.Stop();
                    HoverOpenRequested?.Invoke(this, EventArgs.Empty);
                }
            };
        }

        protected override bool ShowWithoutActivation => true;
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _nativeHandle = Handle;
            TaskbarNative.Attach(_nativeHandle, _host.Handle);
            _attached = true;
            ResizeForDpi();
            RestorePosition();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _presentedSurface = null;
            _nativeHandle = IntPtr.Zero;
            _attached = false;
            base.OnHandleDestroyed(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle = (cp.ExStyle | TaskbarNative.ToolWindowStyle | TaskbarNative.LayeredWindowStyle)
                    & ~TaskbarNative.AppWindowStyle;
                if (_host != null)
                {
                    cp.Style = (cp.Style & ~TaskbarNative.PopupStyle) | TaskbarNative.ChildStyle;
                    cp.Parent = _host.Handle;
                }
                return cp;
            }
        }

        internal bool HoverEnabled
        {
            get => _hoverEnabled;
            set
            {
                if (_hoverEnabled == value) return;
                _hoverEnabled = value;
                _hoverTimer.Stop();
                if (ScreenBounds.Contains(MousePosition)) _pointer.BlockUntilExit();
                else _pointer.Leave();
            }
        }

        internal void SetMenuOpen(bool open)
        {
            _menuOpen = open;
            _tip.Active = !open;
            _hoverTimer.Stop();
            if (ScreenBounds.Contains(MousePosition)) _pointer.BlockUntilExit();
            else _pointer.Leave();
            Invalidate();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ResizeForDpi();
            RestorePosition();
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(value);
            // A non-top-level Form need not raise Shown. Remember the first explicit
            // Show even under a hidden host; later native hides must remain recoverable.
            if (value) _ready = true;
        }

        private int Scale(int value) => (int)Math.Round(value * RenderDpi / 96.0 * Math.Clamp(_settings.WidgetSizePercent, 50, 200) / 100.0);
        private void ResizeForDpi(bool forcePresentation = false)
        {
            if (_renderFailed) return;
            try
            {
                Size desired = TaskbarLayout.WidgetSize(_host.ClientSize, RenderDpi, _settings.WidgetSizePercent);
                TaskbarLayout.ValidateSurface(desired, RenderDpi);
                if (ClientSize != desired) ClientSize = desired;
                if (_attached) PresentSurface(GetSurface(), forcePresentation);
            }
            catch (Exception ex) when (TaskbarNative.IsGraphicsFailure(ex)) { RenderingFailed(ex); }
        }

        private void RestorePosition(bool show = false)
        {
            if (!_attached || _renderFailed) return;
            var area = DesktopNative.PlacementArea(_host, _settings.WidgetDisplayName);
            Point target = WidgetPlacement.Restore(area, Size,
                _settings.WidgetPositionX ?? 0.9, _settings.WidgetPositionY ?? 0.85);
            // The watcher must not reposition or redraw an unchanged window every tick.
            if (ScreenBounds != new Rectangle(target, Size) || show &&
                ((TaskbarNative.GetWindowLong(_nativeHandle, -16) & 0x10000000) == 0 || DesktopNative.HasSiblingAbove(_nativeHandle)))
                TaskbarNative.MoveChild(_nativeHandle, TaskbarNative.ToClient(_host.Handle, target), Size, show);
        }

        private void CommitPosition()
        {
            if (!_attached || !TaskbarNative.IsWindow(_host.Handle)) return;
            var display = Screen.FromRectangle(ScreenBounds);
            _settings.WidgetDisplayName = display.DeviceName;
            var area = DesktopNative.PlacementArea(_host, display.DeviceName);
            var position = WidgetPlacement.Normalize(area, ScreenBounds);
            _settings.WidgetPositionX = position.X;
            _settings.WidgetPositionY = position.Y;
            PositionCommitted?.Invoke(this, EventArgs.Empty);
        }

        internal void RefreshHost(TaskbarHost host)
        {
            bool changed = _host.Dpi != host.Dpi || _host.ClientSize != host.ClientSize || _host.Bounds != host.Bounds ||
                _host.Handle != host.Handle || _host.ProcessId != host.ProcessId;
            bool hidden = _nativeHandle != IntPtr.Zero &&
                (TaskbarNative.GetWindowLong(_nativeHandle, -16) & 0x10000000) == 0;
            _host = host;
            if (_pointer.Pressed) return;
            ResizeForDpi(changed || hidden);
            // Explorer may hide the desktop child without destroying or reparenting it.
            // Restore visibility as well as sibling order without activating a window.
            // _ready prevents a pre-Show host refresh from displaying the child early.
            RestorePosition(show: _ready);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (!_attached || !TaskbarNative.IsWindow(_host.Handle)) return;
            try
            {
                _host = new TaskbarHost(_host.Handle);
                ResizeForDpi(true);
                if (_pointer.Pressed) _dragOffset = new Point(MousePosition.X - ScreenBounds.Left, MousePosition.Y - ScreenBounds.Top);
                else if (_ready) RestorePosition();
            }
            catch (Exception ex) when (TaskbarNative.IsGraphicsFailure(ex)) { RenderingFailed(ex); }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _pointer.Enter(Environment.TickCount64);
            if (_menuOpen) _pointer.ConsumeHover();
            if (_hoverEnabled && !_menuOpen && !_pointer.Pressed) _hoverTimer.Start();
            Invalidate();
        }

        private void PointerLeft()
        {
            _hoverTimer.Stop();
            // A menu can cover the widget without the physical cursor leaving its bounds.
            _pointer.Leave(physicallyOutside: !ScreenBounds.Contains(MousePosition));
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            PointerLeft();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            ApplyPreferences();
            _hoverTimer.Stop();
            _pointer.ConsumeHover();
            _tip.Active = false;
            if (e.Button != MouseButtons.Left) return;
            InteractionStarted?.Invoke(this, EventArgs.Empty);
            _pointer.Press(MousePosition);
            _dragOffset = e.Location;
            Capture = true;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            ApplyPreferences();
            if (!_pointer.Move(MousePosition, SystemInformation.DragSize)) return;
            Cursor = Cursors.SizeAll;
            try
            {
                // One desktop host spans the displays; moving no longer reparents to a taskbar.
                var area = DesktopNative.PlacementArea(_host, null, MousePosition);
                Point target = WidgetPlacement.Clamp(
                    new Point(MousePosition.X - _dragOffset.X, MousePosition.Y - _dragOffset.Y), Size, area);
                TaskbarNative.MoveChild(_nativeHandle, TaskbarNative.ToClient(_host.Handle, target), Size);
            }
            catch (Exception ex) when (TaskbarNative.IsGraphicsFailure(ex))
            {
                _pointer.CancelPress();
                Capture = false;
                Cursor = Cursors.Hand;
                RenderingFailed(ex);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            ApplyPreferences();
            _hoverTimer.Stop();
            if (e.Button == MouseButtons.Right)
            {
                if (ClientRectangle.Contains(e.Location)) SettingsRequested?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            var result = _pointer.Release(ScreenBounds.Contains(MousePosition));
            _releasingCapture = true;
            Capture = false;
            _releasingCapture = false;
            Cursor = Cursors.Hand;
            _tip.Active = !_menuOpen;
            Invalidate();
            if (result == WidgetRelease.Drag) CommitPosition();
            else if (result == WidgetRelease.Click) OpenRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (_releasingCapture || !_pointer.Pressed) return;
            _hoverTimer.Stop();
            bool moved = _pointer.Dragging;
            _pointer.CancelPress();
            Cursor = Cursors.Hand;
            if (moved && _ready && !_settings.WidgetPositionLocked) CommitPosition();
            Invalidate();
        }

        internal void ApplyPreferences()
        {
            bool pressed = _pointer.Pressed, dragging = _pointer.Dragging;
            _pointer.Configure(_settings.WidgetPositionLocked, _settings.WidgetHoverDelayMs);
            if (pressed && !_pointer.Pressed)
            {
                _releasingCapture = true; Capture = false; _releasingCapture = false; Cursor = Cursors.Hand;
                if (dragging && _ready) RestorePosition();
            }
            AccessibleDescription = _settings.WidgetPositionLocked ? L.Text("Position locked. Click to open shortcuts.") : L.Text("Click to open shortcuts. Drag to move the widget.");
            _tip.SetToolTip(this, _settings.WidgetPositionLocked ? L.Text("TaskFolder: position locked. Click to open; right-click for Settings.") : L.Text("TaskFolder: click to open, drag to move. Right-click for Settings."));
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Keep the ApplicationContext's widget reference valid. Exit lives in the shared menu.
            if (e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Enter || keyData == Keys.Space)
            {
                _pointer.ConsumeHover();
                OpenRequested?.Invoke(this, EventArgs.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Color SurfaceBackground => _pointer.Pressed ? Windows11MenuStyle.Pressed :
            _menuOpen || ScreenBounds.Contains(MousePosition) ? Windows11MenuStyle.Hover : Windows11MenuStyle.Surface;

        private Bitmap GetSurface()
        {
            var key = (ClientSize, RenderDpi, SurfaceBackground.ToArgb(), Focused, _settings.WidgetSizePercent);
            if (_surface != null && _surfaceKey == key) return _surface;
            Bitmap replacement = TaskbarNative.CreateSurface(ClientSize, RenderDpi, DrawSurface, _allocateSurface);
            var previous = _surface;
            _surface = replacement;
            _surfaceKey = key;
            previous?.Dispose();
            return replacement;
        }

        private void DrawSurface(Graphics graphics)
        {
            // Alpha zero makes layered pixels click-through. The minimum nonzero alpha keeps
            // the outer corners of the target usable without a visible rectangular backdrop.
            graphics.Clear(Color.FromArgb(1, 0, 0, 0));
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int borderWidth = Math.Max(1, Scale(1));
            int inset = (borderWidth + 1) / 2;
            var borderBounds = new Rectangle(inset, inset, Math.Max(1, Width - inset * 2 - 1),
                Math.Max(1, Height - inset * 2 - 1));
            using var borderPath = Windows11MenuStyle.RoundedRectangle(borderBounds,
                Math.Max(1, Math.Min(Scale(8), Math.Min(borderBounds.Width, borderBounds.Height) / 2)));
            using var fill = new SolidBrush(SurfaceBackground);
            graphics.FillPath(fill, borderPath);
            using var border = new Pen(Focused || _menuOpen ? Windows11MenuStyle.Accent : Windows11MenuStyle.Border, borderWidth);
            graphics.DrawPath(border, borderPath);
            // The approved artwork has transparent padding: a 32 px canvas yields a ~24 px visible glyph.
            int iconSize = Math.Max(1, Math.Min(Scale(32), Math.Min(Width - Scale(8), Height - Scale(8))));
            graphics.DrawImage(_icon, new Rectangle((Width - iconSize) / 2, (Height - iconSize) / 2, iconSize, iconSize));
        }

        private void PresentSurface(Bitmap surface, bool force = false)
        {
            if (!_attached || _renderFailed || !force && ReferenceEquals(surface, _presentedSurface)) return;
            _presentSurface(_nativeHandle, surface);
            _presentedSurface = surface;
        }

        internal void InvalidatePresentation()
        {
            _presentedSurface = null;
            Invalidate();
        }

        private void RenderingFailed(Exception error)
        {
            if (_renderFailed) return;
            _renderFailed = true;
            _hoverTimer.Stop();
            _surface?.Dispose();
            _surface = null;
            _surfaceKey = null;
            _presentedSurface = null;
            HostingFailed?.Invoke(error);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_renderFailed) return;
            try
            {
                var surface = GetSurface();
                // Also supports offscreen DrawToBitmap without introducing an opaque GDI fill.
                e.Graphics.CompositingMode = CompositingMode.SourceCopy;
                e.Graphics.DrawImageUnscaled(surface, Point.Empty);
                PresentSurface(surface);
            }
            catch (Exception ex) when (TaskbarNative.IsGraphicsFailure(ex)) { RenderingFailed(ex); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hoverTimer.Dispose();
                _tip.Dispose();
                _icon.Dispose();
                _surface?.Dispose();
                _surface = null;
                _presentedSurface = null;
            }
            base.Dispose(disposing);
        }
    }
}
