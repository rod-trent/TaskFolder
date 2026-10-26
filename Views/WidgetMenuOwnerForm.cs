using System.Drawing;
using System.Windows.Forms;

namespace TaskFolder.Views
{
    // Independent popup owner: menus must not be owned/clipped by the foreign taskbar child.
    internal sealed class WidgetMenuOwnerForm : Form
    {
        internal WidgetMenuOwnerForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1);
            Opacity = 0;
            Text = "TaskFolder menu owner";
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle = (cp.ExStyle | 0x80) & ~0x40000;
                return cp;
            }
        }
    }
}
