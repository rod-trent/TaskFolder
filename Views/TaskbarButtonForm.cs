using System;
using System.Drawing;
using System.Windows.Forms;
using TaskFolder.Utilities;

namespace TaskFolder.Views
{
    // A real minimized window gives the Shell a normal, user-pinnable taskbar button.
    // Restoring it opens the launcher instead of displaying an empty desktop window.
    internal sealed class TaskbarButtonForm : Form
    {
        internal event EventHandler OpenRequested;
        internal event EventHandler ExitRequested;
        private bool _allowClose;
        private bool _openQueued;
        private readonly bool _portable;

        internal TaskbarButtonForm(Icon icon, bool portable)
        {
            _portable = portable;
            Text = "TaskFolder";
            Icon = icon;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1);
            Opacity = 0;
            WindowState = FormWindowState.Minimized;
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { WindowsShellLink.ConfigureTaskbarWindow(Handle, _portable); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException ||
                ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"Taskbar pin metadata: {ex.Message}");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0013 || // WM_QUERYOPEN: also covers Shell restore paths without SC_RESTORE.
                (m.Msg == 0x0112 && (m.WParam.ToInt64() & 0xFFF0) == 0xF120))
            {
                if (!_openQueued)
                {
                    _openQueued = true;
                    BeginInvoke(new Action(() =>
                    {
                        _openQueued = false;
                        OpenRequested?.Invoke(this, EventArgs.Empty);
                    }));
                }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
            base.OnFormClosing(e);
        }

        internal void Shutdown()
        {
            _allowClose = true;
            Close();
            Dispose();
        }
    }
}
