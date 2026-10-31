using System.Drawing;
using System.Windows.Forms;

namespace TaskFolder.Views
{
    /// <summary>
    /// Dark-themed ToolStripRenderer for the tray menu.
    /// </summary>
    internal sealed class DarkModeRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color BgColor       = Color.FromArgb(30, 30, 46);
        private static readonly Color SelectColor   = Color.FromArgb(50, 100, 200);
        private static readonly Color TextColor     = Color.FromArgb(205, 214, 244);
        private static readonly Color MutedColor    = Color.FromArgb(108, 112, 134);
        private static readonly Color BorderColor   = Color.FromArgb(50, 50, 70);
        private static readonly Color MarginColor   = Color.FromArgb(24, 24, 37);

        public DarkModeRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.FillRectangle(new SolidBrush(BgColor), e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            var g = e.Graphics;
            var rect = new Rectangle(2, 0, item.Width - 4, item.Height - 1);

            if (item.Selected && item.Enabled)
                g.FillRectangle(new SolidBrush(SelectColor), rect);
            else
                g.FillRectangle(new SolidBrush(BgColor), new Rectangle(0, 0, item.Width, item.Height));
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? TextColor : MutedColor;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var g = e.Graphics;
            int y = e.Item.Height / 2;
            g.DrawLine(new Pen(BorderColor), 28, y, e.Item.Width - 4, y);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            e.Graphics.FillRectangle(new SolidBrush(MarginColor), e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            g.DrawRectangle(new Pen(BorderColor), r);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item.Selected ? Color.White : MutedColor;
            base.OnRenderArrow(e);
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected        => Color.FromArgb(50, 100, 200);
            public override Color MenuItemBorder          => Color.FromArgb(70, 120, 220);
            public override Color MenuBorder              => Color.FromArgb(50, 50, 70);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(50, 100, 200);
            public override Color MenuItemSelectedGradientEnd   => Color.FromArgb(50, 100, 200);
            public override Color ToolStripDropDownBackground   => Color.FromArgb(30, 30, 46);
            public override Color ImageMarginGradientBegin      => Color.FromArgb(24, 24, 37);
            public override Color ImageMarginGradientMiddle     => Color.FromArgb(24, 24, 37);
            public override Color ImageMarginGradientEnd        => Color.FromArgb(24, 24, 37);
        }
    }
}
