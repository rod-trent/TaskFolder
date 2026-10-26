using L = TaskFolder.Services.LocalizationService;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskFolder.Utilities
{
    internal interface IWindows11AnimatedMenuItem
    {
        float HoverProgress { get; }
    }

    /// <summary>
    /// Shared Windows 11-inspired visual system for every tray menu surface.
    /// Uses only WinForms/GDI so text remains ClearType-rendered and DPI-aware.
    /// </summary>
    internal static class Windows11MenuStyle
    {
        internal static Color Surface => Services.UiThemeService.Palette.Surface;
        internal static Color InputSurface => Services.UiThemeService.Palette.Input;
        internal static Color Border => Services.UiThemeService.Palette.Border;
        internal static Color Hover => Services.UiThemeService.Palette.Hover;
        internal static Color Pressed => Services.UiThemeService.Palette.Pressed;
        internal static Color Text => Services.UiThemeService.Palette.Text;
        internal static Color MutedText => Services.UiThemeService.Palette.Muted;
        internal static Color DisabledText => Services.UiThemeService.Palette.Disabled;
        internal static Color Accent => Services.UiThemeService.Palette.Accent;

        internal static Font CreateFont(float sizeInPoints, FontStyle style = FontStyle.Regular)
        {
            Font font = new Font("Segoe UI Variable Text", sizeInPoints, style, GraphicsUnit.Point);
            if (!font.Name.Equals("Segoe UI Variable Text", StringComparison.OrdinalIgnoreCase))
            {
                font.Dispose();
                font = new Font("Segoe UI", sizeInPoints, style, GraphicsUnit.Point);
            }
            return font;
        }

        internal static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var path = new GraphicsPath();
            int diameter = Math.Max(2, radius * 2);
            var arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class Windows11ColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Windows11MenuStyle.Surface;
        public override Color ImageMarginGradientBegin => Windows11MenuStyle.Surface;
        public override Color ImageMarginGradientMiddle => Windows11MenuStyle.Surface;
        public override Color ImageMarginGradientEnd => Windows11MenuStyle.Surface;
        public override Color MenuBorder => Windows11MenuStyle.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Windows11MenuStyle.Hover;
        public override Color MenuItemSelectedGradientBegin => Windows11MenuStyle.Hover;
        public override Color MenuItemSelectedGradientEnd => Windows11MenuStyle.Hover;
        public override Color MenuItemPressedGradientBegin => Windows11MenuStyle.Pressed;
        public override Color MenuItemPressedGradientMiddle => Windows11MenuStyle.Pressed;
        public override Color MenuItemPressedGradientEnd => Windows11MenuStyle.Pressed;
        public override Color SeparatorDark => Windows11MenuStyle.Border;
        public override Color SeparatorLight => Windows11MenuStyle.Surface;
        public override Color CheckBackground => Windows11MenuStyle.Hover;
        public override Color CheckSelectedBackground => Windows11MenuStyle.Hover;
        public override Color CheckPressedBackground => Windows11MenuStyle.Pressed;
    }

    internal sealed class Windows11MenuRenderer : ToolStripProfessionalRenderer
    {
        internal Windows11MenuRenderer() : base(new Windows11ColorTable())
        {
            // The drop-down owns one rounded outline. ProfessionalRenderer's
            // extra edge treatment would make a second, square-looking frame.
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Windows11MenuStyle.Surface);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip.Width < 2 || e.ToolStrip.Height < 2) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using GraphicsPath path = Windows11MenuStyle.RoundedRectangle(bounds, Scale(e.ToolStrip, 8));
            using var pen = new Pen(Windows11MenuStyle.Border);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // WinForms' Professional renderer normally paints a separate icon
            // gutter. Keep its layout for aligned icons, without the old stripe.
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            // ToolStrip reuses the item surface while selection moves between rows.
            // Always repaint the normal state first or the previous hover wash can
            // remain visible until another dropdown receives mouse input.
            using (var surfaceBrush = new SolidBrush(Windows11MenuStyle.Surface))
                e.Graphics.FillRectangle(surfaceBrush, new Rectangle(Point.Empty, e.Item.Size));

            float progress = e.Item is IWindows11AnimatedMenuItem animated
                ? animated.HoverProgress
                : e.Item.Selected ? 1f : 0f;
            if (!e.Item.Enabled || progress <= 0f) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int animatedInset = Scale(e.ToolStrip, 4) +
                (int)Math.Round(Scale(e.ToolStrip, 2) * (1f - progress));
            Rectangle bounds = new Rectangle(
                animatedInset,
                Scale(e.ToolStrip, 2),
                e.Item.Width - (animatedInset * 2),
                e.Item.Height - Scale(e.ToolStrip, 4));
            using GraphicsPath path = Windows11MenuStyle.RoundedRectangle(bounds, Scale(e.ToolStrip, 4));
            Color target = e.Item.Pressed
                ? Windows11MenuStyle.Pressed
                : Windows11MenuStyle.Hover;
            using var brush = new SolidBrush(Blend(
                Windows11MenuStyle.Surface,
                target,
                e.Item.Pressed ? 1f : progress));
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color color = !e.Item.Enabled
                ? Windows11MenuStyle.DisabledText
                : SystemInformation.HighContrast && e.Item.Selected ? SystemColors.HighlightText : Windows11MenuStyle.Text;
            Rectangle textBounds = e.TextRectangle;
            textBounds.Y = 0;
            textBounds.Height = e.Item.Height;
            TextFormatFlags flags = (e.TextFormat & ~(TextFormatFlags.Top | TextFormatFlags.Bottom))
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textBounds, color, flags);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int left = Scale(e.ToolStrip, 12);
            int right = e.Item.Width - Scale(e.ToolStrip, 12);
            int y = e.Item.Height / 2;
            using var pen = new Pen(Windows11MenuStyle.Border);
            e.Graphics.DrawLine(pen, left, y, right, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Enabled
                ? Windows11MenuStyle.MutedText
                : Windows11MenuStyle.DisabledText;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle bounds = e.ImageRectangle;
            if (bounds.IsEmpty) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.ToolStrip.DeviceDpi / 96f;
            int size = Math.Max(12, (int)Math.Round(16 * scale));
            var box = new Rectangle(
                bounds.Left + (bounds.Width - size) / 2,
                bounds.Top + (bounds.Height - size) / 2,
                size, size);
            using GraphicsPath path = Windows11MenuStyle.RoundedRectangle(box, Math.Max(2, (int)Math.Round(3 * scale)));
            using var fill = new SolidBrush(Windows11MenuStyle.Hover);
            using var outline = new Pen(Windows11MenuStyle.Border);
            using var tick = new Pen(Windows11MenuStyle.Text, Math.Max(1.5f, 1.6f * scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(outline, path);
            e.Graphics.DrawLines(tick, new[]
            {
                new PointF(box.Left + size * 0.25f, box.Top + size * 0.50f),
                new PointF(box.Left + size * 0.43f, box.Top + size * 0.68f),
                new PointF(box.Left + size * 0.76f, box.Top + size * 0.32f)
            });
        }

        private static int Scale(ToolStrip owner, int logicalPixels)
        {
            return Math.Max(1, (int)Math.Round(logicalPixels * owner.DeviceDpi / 96f));
        }

        private static Color Blend(Color from, Color to, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            return Color.FromArgb(
                (int)Math.Round(from.R + ((to.R - from.R) * amount)),
                (int)Math.Round(from.G + ((to.G - from.G) * amount)),
                (int)Math.Round(from.B + ((to.B - from.B) * amount)));
        }
    }

    internal static class Windows11MenuIcons
    {
        internal static Bitmap CreateFolderIcon(int size)
        {
            size = Math.Max(12, size);
            var bitmap = new Bitmap(size, size);
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;

            float scale = size / 16f;
            using var fill = new SolidBrush(Color.FromArgb(255, 194, 73));
            using var edge = new Pen(Color.FromArgb(188, 126, 18), Math.Max(1f, scale));
            using var path = new GraphicsPath();
            path.AddLine(2f * scale, 4.5f * scale, 6f * scale, 4.5f * scale);
            path.AddLine(6f * scale, 4.5f * scale, 7.3f * scale, 6f * scale);
            path.AddLine(7.3f * scale, 6f * scale, 14f * scale, 6f * scale);
            path.AddLine(14f * scale, 6f * scale, 14f * scale, 13f * scale);
            path.AddLine(14f * scale, 13f * scale, 2f * scale, 13f * scale);
            path.CloseFigure();
            graphics.FillPath(fill, path);
            graphics.DrawPath(edge, path);
            return bitmap;
        }
    }

    internal sealed class Windows11ContextMenuStrip : ContextMenuStrip
    {
        private readonly Font _ownedFont;
        private readonly OutsideClickMenuDismissal _outsideDismissal;
        private readonly HoverMenuDismissal _hoverDismissal;
        private Func<Point, bool> _hoverAnchor;
        private readonly Timer _openAnimation = new Timer { Interval = 15 };
        private long _openedAt;
        private MenuFadeWindow _fadeOut;
        private double _visualOpacity = 1;
        private bool _opacityFailed;
        private IntPtr _foregroundAtOpen;
        internal double VisualOpacity => _visualOpacity;

        internal Windows11ContextMenuStrip(Windows11MenuRenderer renderer, Font font)
        {
            _ownedFont = font;
            // Every root, nested, and shortcut context menu is created through
            // this class. Assign the renderer directly so an external global
            // ToolStripManager setting cannot restore Windows' blue selection.
            Renderer = renderer;
            Font = font;
            BackColor = Windows11MenuStyle.Surface;
            ForeColor = Windows11MenuStyle.Text;
            DropShadowEnabled = true;
            ShowImageMargin = true;
            Padding = new Padding(4, 6, 4, 6);
            _openAnimation.Tick += (_, _) => AdvanceOpening(Environment.TickCount64);
            _outsideDismissal = new OutsideClickMenuDismissal(
                point => OutsideClickMenuDismissal.ContainsPoint(point, OutsideClickMenuDismissal.VisibleMenuBounds(this)),
                action => BeginInvoke(action),
                () => Close(ToolStripDropDownCloseReason.AppClicked));
            _hoverDismissal = new HoverMenuDismissal(
                point => _hoverAnchor?.Invoke(point) == true || OutsideClickMenuDismissal.ContainsPoint(point,
                    OutsideClickMenuDismissal.VisibleMenuBounds(this)),
                () => Visible && !IsDisposed && !Disposing,
                () => Close(ToolStripDropDownCloseReason.CloseCalled));
        }

        internal void StartHoverDismissal(Func<Point, bool> anchorContainsPoint, int delayMs)
        {
            if (OwnerItem != null || !Visible) return;
            _hoverAnchor = anchorContainsPoint;
            _hoverDismissal.Start(delayMs);
        }

        protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
        {
            base.OnOpening(e);
            if (e.Cancel) return;
            MenuFadeWindow.CancelAll();
            _openAnimation.Stop();
            PresentOpacity(MenuMotion.Enabled && !_opacityFailed ? MenuMotion.InitialOpacity : 1);
        }

        protected override void OnOpened(EventArgs e)
        {
            _foregroundAtOpen = MenuMotion.GetForegroundWindow();
            if (_visualOpacity < 1)
            {
                _openedAt = Environment.TickCount64;
                _openAnimation.Start();
            }
            base.OnOpened(e);
            // One listener covers the root and all visible descendants. Nested menus
            // must not treat a click in their parent or sibling as an outside click.
            if (OwnerItem != null || !Visible) return;
            try { _outsideDismissal.Start(); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TaskFolder outside-click hook unavailable: " + ex.Message);
            }
        }

        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            _hoverDismissal.Stop();
            _hoverAnchor = null;
            _openAnimation.Stop();
            PresentOpacity(1);
            _outsideDismissal?.Stop();
            base.OnClosed(e);
            if (Visible || IsDisposed || Disposing) _fadeOut?.Dispose();
            else
            {
                try
                {
                    var fade = _fadeOut;
                    if (fade != null && !fade.IsDisposed)
                        BeginInvoke(new Action(() =>
                        {
                            try { if (!IsDisposed && !Visible) fade.Begin(); else fade.Dispose(); }
                            catch (Exception error) when (TaskbarNative.IsGraphicsFailure(error))
                            { fade.Dispose(); System.Diagnostics.Debug.WriteLine("TaskFolder menu fade skipped: " + error.Message); }
                        }));
                }
                catch (Exception error) when (TaskbarNative.IsGraphicsFailure(error))
                {
                    _fadeOut?.Dispose();
                    System.Diagnostics.Debug.WriteLine("TaskFolder menu fade skipped: " + error.Message);
                }
            }
        }

        internal void AdvanceOpening(long now)
        {
            long elapsed = now - _openedAt;
            if (!Visible || !MenuMotion.Enabled || elapsed >= MenuMotion.OpenDurationMs)
            {
                _openAnimation.Stop();
                PresentOpacity(1);
                return;
            }
            PresentOpacity(MenuMotion.OpacityAt(elapsed, MenuMotion.OpenDurationMs, MenuMotion.InitialOpacity, 1));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PresentOpacity(_visualOpacity);
        }

        internal void PresentOpacity(double opacity)
        {
            _visualOpacity = opacity;
            if (_opacityFailed || !IsHandleCreated) return;
            if (MenuMotion.SetOpacity(Handle, opacity)) return;
            // Effects must never prevent the menu from becoming usable.
            _visualOpacity = 1;
            _opacityFailed = true;
            _openAnimation.Stop();
            UpdateStyles();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                // The GDI renderer draws the single outline inside the rounded
                // region; a native border or client edge doubles that contour.
                const int wsBorder = 0x00800000;
                const int wsExClientEdge = 0x00000200;
                parameters.Style &= ~wsBorder;
                parameters.ExStyle &= ~wsExClientEdge;
                // Keep the style stable for the entire HWND lifetime. WinForms'
                // Opacity setter removes WS_EX_LAYERED at 1.0 and updates styles
                // while the active menu is processing focus/mouse messages.
                if (!_opacityFailed) parameters.ExStyle |= 0x80000;
                return parameters;
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0 || Height <= 0) return;

            int radius = Math.Max(4, (int)Math.Round(8 * DeviceDpi / 96f));
            using GraphicsPath path = Windows11MenuStyle.RoundedRectangle(
                new Rectangle(0, 0, Width, Height), radius);
            Region previous = Region;
            Region = new Region(path);
            previous?.Dispose();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            _hoverDismissal?.Stop();
            // A standalone Alt press normally exits WinForms menu mode. Consume it
            // here so Alt+Shift can switch the input language without dismissing
            // the launcher while the hosted search editor is active.
            if ((keyData & Keys.KeyCode) == Keys.Menu)
                return true;

            return base.ProcessDialogKey(keyData);
        }

        protected override void OnClosing(ToolStripDropDownClosingEventArgs e)
        {
            IntPtr foreground = MenuMotion.GetForegroundWindow();
            if (MenuMotion.KeepRootOpen(e.CloseReason, OwnerItem != null, Visible, _foregroundAtOpen,
                foreground, IsMenuWindow(this, foreground)))
                e.Cancel = true;
            if (e.CloseReason == ToolStripDropDownCloseReason.Keyboard &&
                (ModifierKeys & Keys.Alt) == Keys.Alt)
            {
                e.Cancel = true;
            }

            base.OnClosing(e);
            if (e.Cancel || IsDisposed || Disposing) return;
            _openAnimation.Stop();
            _fadeOut?.Dispose();
            _fadeOut = null;
            // Sibling navigation must not create top-level Form windows while a
            // parent menu remains active. Only the dismissed root leaves an image.
            if (OwnerItem != null || !MenuMotion.Enabled || _visualOpacity <= MenuMotion.InitialOpacity) return;
            try { _fadeOut = MenuFadeWindow.CreateSnapshot(this); }
            catch (Exception error) when (TaskbarNative.IsGraphicsFailure(error))
            {
                System.Diagnostics.Debug.WriteLine("TaskFolder menu fade skipped: " + error.Message);
            }
        }

        private static bool IsMenuWindow(ToolStripDropDown menu, IntPtr window)
        {
            if (window == IntPtr.Zero || !menu.Visible || !menu.IsHandleCreated) return false;
            if (window == menu.Handle || TaskbarNative.GetAncestor(window, 2) == menu.Handle) return true;
            foreach (ToolStripItem item in menu.Items)
                if (item is ToolStripDropDownItem folder && folder.HasDropDownItems &&
                    IsMenuWindow(folder.DropDown, window)) return true;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _openAnimation.Dispose();
                _fadeOut?.Dispose();
                _outsideDismissal?.Dispose();
                _hoverDismissal?.Dispose();
            }
            base.Dispose(disposing);
            if (disposing)
                _ownedFont?.Dispose();
        }
    }

    internal sealed class Windows11SearchBox : UserControl
    {
        private readonly CompositionTextBox _textBox;
        private bool _hovered;

        internal event EventHandler SearchTextChanged;
        internal event EventHandler SearchSubmitted;
        internal event Action<int> SearchNavigation;
        internal event EventHandler EscapeRequested;

        internal Windows11SearchBox(Font font)
        {
            DoubleBuffered = true;
            BackColor = Windows11MenuStyle.Surface;
            Font = font;
            TabStop = false;
            AccessibleName = L.Text("Search shortcuts");
            AccessibleRole = AccessibleRole.Text;

            _textBox = new CompositionTextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Windows11MenuStyle.InputSurface,
                ForeColor = Windows11MenuStyle.Text,
                Font = font,
                PlaceholderText = L.Text("Search shortcuts"),
                AccessibleName = L.Text("Search shortcuts")
            };
            _textBox.TextChanged += (_, e) =>
            {
                SearchTextChanged?.Invoke(this, e);
                Invalidate();
            };
            _textBox.GotFocus += (_, _) => Invalidate();
            _textBox.LostFocus += (_, _) => Invalidate();
            _textBox.KeyDown += (_, e) =>
            {
                if (_textBox.Composing) return;
                if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
                { e.Handled = true; e.SuppressKeyPress = true; SearchNavigation?.Invoke(e.KeyCode == Keys.Up ? -1 : 1); return; }
                if (e.KeyCode == Keys.Escape)
                { e.Handled = true; e.SuppressKeyPress = true; if (_textBox.TextLength > 0) _textBox.Clear(); else EscapeRequested?.Invoke(this, EventArgs.Empty); return; }
                if (e.KeyCode != Keys.Enter) return;

                e.Handled = true;
                e.SuppressKeyPress = true;
                SearchSubmitted?.Invoke(this, EventArgs.Empty);
            };
            Controls.Add(_textBox);
            var clear = new Button { Text = "×", AccessibleName = L.Text("Clear search"), Dock = DockStyle.Right, Width = 30, FlatStyle = FlatStyle.Flat, TabStop = true };
            clear.Click += (_, _) => { _textBox.Clear(); _textBox.Focus(); }; Controls.Add(clear);
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        internal string SearchText => _textBox.Text;

        private sealed class CompositionTextBox : TextBox
        {
            internal bool Composing;
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x010d) Composing = true;
                if (message.Msg == 0x010e) Composing = false;
                base.WndProc(ref message);
            }
        }

        internal void FocusEditor() => _textBox.Focus();

        internal void SelectAllAndFocus()
        {
            _textBox.Focus();
            _textBox.SelectAll();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int left = Scale(34);
            int right = Scale(32);
            _textBox.Location = new Point(left, Math.Max(1, (Height - _textBox.PreferredHeight) / 2));
            _textBox.Width = Math.Max(40, Width - left - right);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            using GraphicsPath path = Windows11MenuStyle.RoundedRectangle(bounds, Scale(8));
            using var fill = new SolidBrush(Windows11MenuStyle.InputSurface);
            using var border = new Pen(_textBox.Focused
                ? Windows11MenuStyle.Accent
                : _hovered ? Color.FromArgb(172, 172, 172) : Windows11MenuStyle.Border,
                _textBox.Focused ? 2f : 1f);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            using var iconPen = new Pen(Windows11MenuStyle.MutedText, Math.Max(1.4f, 1.4f * DeviceDpi / 96f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            int iconX = Scale(13);
            int iconY = Height / 2 - Scale(5);
            e.Graphics.DrawEllipse(iconPen, iconX, iconY, Scale(8), Scale(8));
            e.Graphics.DrawLine(iconPen, iconX + Scale(7), iconY + Scale(7),
                iconX + Scale(12), iconY + Scale(12));

            if (_textBox.TextLength > 0)
            {
                int centerX = Width - Scale(16);
                int centerY = Height / 2;
                e.Graphics.DrawLine(iconPen, centerX - Scale(4), centerY - Scale(4),
                    centerX + Scale(4), centerY + Scale(4));
                e.Graphics.DrawLine(iconPen, centerX + Scale(4), centerY - Scale(4),
                    centerX - Scale(4), centerY + Scale(4));
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int clearWidth = Scale(32);
            Cursor = _textBox.TextLength > 0 && e.X >= Width - clearWidth
                ? Cursors.Hand
                : Cursors.IBeam;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int clearWidth = Scale(32);
            if (_textBox.TextLength > 0 && e.X >= Width - clearWidth)
                _textBox.Clear();
            _textBox.Focus();
            base.OnMouseDown(e);
        }

        private int Scale(int logicalPixels)
        {
            return Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96f));
        }
    }
}
