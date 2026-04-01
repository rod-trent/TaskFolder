using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TaskFolder.Services
{
    /// <summary>
    /// Registers a system-wide hotkey and fires HotkeyPressed when it is triggered.
    /// Uses a hidden NativeWindow as the message sink (NotifyIcon has no HWND).
    /// </summary>
    public sealed class HotkeyService : IDisposable
    {
        // Win32 constants
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0xBEEF; // arbitrary unique ID

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly HotkeyWindow _window;
        private bool _registered;

        public event EventHandler HotkeyPressed;

        public HotkeyService()
        {
            _window = new HotkeyWindow(this);
            _window.CreateHandle(new CreateParams());
        }

        /// <summary>
        /// Registers the hotkey. modifiersStr is e.g. "Ctrl+Alt", keyStr is e.g. "T".
        /// Returns true on success.
        /// </summary>
        public bool Register(string modifiersStr, string keyStr)
        {
            Unregister();

            if (!TryParseModifiers(modifiersStr, out uint mods)) return false;
            if (!Enum.TryParse<Keys>(keyStr, true, out Keys key)) return false;

            _registered = RegisterHotKey(_window.Handle, HOTKEY_ID, mods | MOD_NOREPEAT, (uint)key);
            if (!_registered)
                System.Diagnostics.Debug.WriteLine($"HotkeyService: RegisterHotKey failed (error {Marshal.GetLastWin32Error()})");

            return _registered;
        }

        public void Unregister()
        {
            if (_registered)
            {
                UnregisterHotKey(_window.Handle, HOTKEY_ID);
                _registered = false;
            }
        }

        private static bool TryParseModifiers(string s, out uint mods)
        {
            mods = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            foreach (string part in s.Split('+'))
            {
                switch (part.Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": mods |= MOD_CONTROL; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "win":
                    case "windows": mods |= MOD_WIN; break;
                    default: return false;
                }
            }
            return mods != 0;
        }

        internal void OnHotkeyMessage()
        {
            HotkeyPressed?.Invoke(this, EventArgs.Empty);
        }

        public void Dispose()
        {
            Unregister();
            _window.DestroyHandle();
        }

        // ── Hidden message sink window ──────────────────────────────────────────

        private sealed class HotkeyWindow : NativeWindow
        {
            private readonly HotkeyService _owner;

            public HotkeyWindow(HotkeyService owner)
            {
                _owner = owner;
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
                {
                    _owner.OnHotkeyMessage();
                    return;
                }
                base.WndProc(ref m);
            }
        }
    }
}
