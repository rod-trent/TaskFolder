using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskFolder.Views
{
    /// <summary>
    /// A small modal dialog that waits for the user to press any non-primary mouse
    /// button, then reports which button was pressed.
    /// </summary>
    public sealed class MouseButtonCaptureDialog : Form
    {
        // ── Win32 ────────────────────────────────────────────────────────────────

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public System.Drawing.Point pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const int WH_MOUSE_LL   = 14;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;

        // ── State ────────────────────────────────────────────────────────────────

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelMouseProc _proc;

        /// <summary>The captured button identifier, set when the dialog closes OK.</summary>
        public string CapturedButton { get; private set; }

        // ── UI ───────────────────────────────────────────────────────────────────

        private readonly Label _lblStatus;

        public MouseButtonCaptureDialog()
        {
            Text            = "Set Mouse Hotkey";
            Size            = new System.Drawing.Size(340, 160);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;
            MinimizeBox     = false;
            StartPosition   = FormStartPosition.CenterParent;
            ShowInTaskbar   = false;

            _lblStatus = new Label
            {
                Text      = "Click any mouse button\n(middle, back, or forward)\nto use it as the hotkey.",
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Dock      = DockStyle.Fill,
                Font      = new System.Drawing.Font("Segoe UI", 10f)
            };
            Controls.Add(_lblStatus);

            var btnCancel = new Button
            {
                Text         = "Cancel",
                DialogResult = DialogResult.Cancel,
                Dock         = DockStyle.Bottom,
                Height       = 30
            };
            Controls.Add(btnCancel);
            CancelButton = btnCancel;

            Load   += (_, _) => InstallHook();
            FormClosed += (_, _) => RemoveHook();
        }

        private void InstallHook()
        {
            _proc = HookCallback;
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            using var mod  = proc.MainModule;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(mod.ModuleName), 0);
        }

        private void RemoveHook()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                string captured = null;

                if (msg == WM_MBUTTONDOWN)
                {
                    captured = "Middle";
                }
                else if (msg == WM_XBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    int which = (int)((info.mouseData >> 16) & 0xFFFF);
                    captured = which == 1 ? "XButton1" : "XButton2";
                }

                if (captured != null)
                {
                    CapturedButton = captured;
                    // Marshal back to UI thread before closing the form
                    BeginInvoke(() =>
                    {
                        DialogResult = DialogResult.OK;
                        Close();
                    });
                    // Suppress this click so it doesn't land on whatever is under the dialog
                    return (IntPtr)1;
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
}
