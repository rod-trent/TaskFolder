using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskFolder.Services
{
    /// <summary>
    /// Installs a WH_MOUSE_LL global hook and fires MouseHotkeyPressed when the
    /// configured mouse button is pressed anywhere on the desktop.
    /// </summary>
    public sealed class MouseHotkeyService : IDisposable
    {
        // ── Win32 ────────────────────────────────────────────────────────────────

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private const int WH_MOUSE_LL  = 14;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_XBUTTONDOWN = 0x020B;

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public System.Drawing.Point pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // ── State ────────────────────────────────────────────────────────────────

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelMouseProc _proc; // must hold a reference to prevent GC
        private string _targetButton = "Middle";

        public event EventHandler MouseHotkeyPressed;

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>
        /// Installs the hook for the given button ("Middle", "XButton1", or "XButton2").
        /// Safe to call multiple times; re-registers when button changes.
        /// </summary>
        public void Register(string button)
        {
            _targetButton = button ?? "Middle";
            if (_hookId != IntPtr.Zero) return; // already hooked

            _proc = HookCallback;
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            using var mod  = proc.MainModule;
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(mod.ModuleName), 0);

            if (_hookId == IntPtr.Zero)
                System.Diagnostics.Debug.WriteLine(
                    $"MouseHotkeyService: SetWindowsHookEx failed (error {Marshal.GetLastWin32Error()})");
        }

        public void Unregister()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        public void Dispose() => Unregister();

        // ── Hook callback (runs on the UI message-loop thread) ───────────────────

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsTargetButton((int)wParam, lParam))
                // Invoke on the UI thread via the Windows message queue so the
                // handler can safely touch WinForms controls.
                MouseHotkeyPressed?.Invoke(this, EventArgs.Empty);

            // Always pass through — do not consume the click.
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private bool IsTargetButton(int msg, IntPtr lParam)
        {
            if (_targetButton == "Middle" && msg == WM_MBUTTONDOWN)
                return true;

            if (msg == WM_XBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                int which = (int)((info.mouseData >> 16) & 0xFFFF);
                if (_targetButton == "XButton1" && which == 1) return true;
                if (_targetButton == "XButton2" && which == 2) return true;
            }

            return false;
        }

        // ── Friendly names ───────────────────────────────────────────────────────

        /// <summary>Returns a short human-readable label for the button identifier.</summary>
        public static string FriendlyName(string button) => button switch
        {
            "Middle"   => "Middle Button",
            "XButton1" => "Mouse Button 4 (Back)",
            "XButton2" => "Mouse Button 5 (Forward)",
            _          => button ?? "None"
        };
    }
}
