using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TaskFolder.Utilities
{
    /// <summary>
    /// Shared Windows 11-inspired visual system for every tray menu surface.
    /// Uses only WinForms/GDI so text remains ClearType-rendered and DPI-aware.
    /// </summary>
    internal static class Windows11MenuStyle
    {
        internal static readonly Color Surface = Color.FromArgb(249, 249, 249);
        internal static readonly Color InputSurface = Color.FromArgb(243, 243, 243);
        internal static readonly Color Border = Color.FromArgb(214, 214, 214);
        internal static readonly Color Hover = Color.FromArgb(232, 232, 232);
        internal static readonly Color Pressed = Color.FromArgb(224, 224, 224);
        internal static readonly Color Text = Color.FromArgb(31, 31, 31);
        internal static readonly Color MutedText = Color.FromArgb(97, 97, 97);
        internal static readonly Color DisabledText = Color.FromArgb(150, 150, 150);
        internal static readonly Color Accent = Color.FromArgb(0, 103, 192);

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
            RoundedEdges = true;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.Clear(Windows11MenuStyle.Surface);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using GraphicsPath path = Windows11MenuStyle.RoundedRectangle(bounds, Scale(e.ToolStrip, 8));
            using var pen = new Pen(Windows11MenuStyle.Border);
            e.Graphics.DrawPath(pen, path);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            // ToolStrip reuses the item surface while selection moves between rows.
            // Always repaint the normal state first or the previous hover wash can
            // remain visible until another dropdown receives mouse input.
            using (var surfaceBrush = new SolidBrush(Windows11MenuStyle.Surface))
                e.Graphics.FillRectangle(surfaceBrush, new Rectangle(Point.Empty, e.Item.Size));

            if (!e.Item.Selected) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle bounds = new Rectangle(
                Scale(e.ToolStrip, 4),
                Scale(e.ToolStrip, 2),
                e.Item.Width - Scale(e.ToolStrip, 8),
                e.Item.Height - Scale(e.ToolStrip, 4));
            using GraphicsPath path = Windows11MenuStyle.RoundedRectangle(bounds, Scale(e.ToolStrip, 4));
            using var brush = new SolidBrush(e.Item.Pressed
                ? Windows11MenuStyle.Pressed
                : Windows11MenuStyle.Hover);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color color = !e.Item.Enabled
                ? Windows11MenuStyle.DisabledText
                : Windows11MenuStyle.Text;
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

        private static int Scale(ToolStrip owner, int logicalPixels)
        {
            return Math.Max(1, (int)Math.Round(logicalPixels * owner.DeviceDpi / 96f));
        }
    }

    internal sealed class Windows11ContextMenuStrip : ContextMenuStrip
    {
        private readonly Font _ownedFont;

        internal Windows11ContextMenuStrip(Windows11MenuRenderer renderer, Font font)
        {
            _ownedFont = font;
            // ToolStripManager is required for ToolStripDropDown instances created by
            // nested ToolStripMenuItems; a renderer assigned only to the root menu
            // does not reliably propagate through the whole menu tree.
            ToolStripManager.Renderer = renderer;
            RenderMode = ToolStripRenderMode.ManagerRenderMode;
            Font = font;
            BackColor = Windows11MenuStyle.Surface;
            ForeColor = Windows11MenuStyle.Text;
            DropShadowEnabled = true;
            ShowImageMargin = true;
            Padding = new Padding(4, 6, 4, 6);
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

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                _ownedFont?.Dispose();
        }
    }

    internal sealed class Windows11SearchBox : UserControl
    {
        private readonly TextBox _textBox;
        private bool _hovered;

        internal event EventHandler SearchTextChanged;

        internal Windows11SearchBox(Font font)
        {
            DoubleBuffered = true;
            BackColor = Windows11MenuStyle.Surface;
            Font = font;
            TabStop = false;
            AccessibleName = "Search shortcuts";
            AccessibleRole = AccessibleRole.Text;

            _textBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Windows11MenuStyle.InputSurface,
                ForeColor = Windows11MenuStyle.Text,
                Font = font,
                PlaceholderText = "Search shortcuts",
                AccessibleName = "Search shortcuts"
            };
            _textBox.TextChanged += (_, e) =>
            {
                SearchTextChanged?.Invoke(this, e);
                Invalidate();
            };
            _textBox.GotFocus += (_, _) => Invalidate();
            _textBox.LostFocus += (_, _) => Invalidate();
            Controls.Add(_textBox);
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        internal string SearchText => _textBox.Text;

        internal void FocusEditor() => _textBox.Focus();

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
