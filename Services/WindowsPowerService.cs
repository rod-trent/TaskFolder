using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TaskFolder.Services
{
    internal enum WindowsPowerAction
    {
        SignOut,
        Sleep,
        Hibernate,
        ShutDown,
        Restart
    }

    internal sealed class WindowsPowerService
    {
        private const uint EwxLogoff = 0;
        private const uint EwxPoweroff = 0x00000008;
        private const uint EwxReboot = 0x00000002;
        private const uint PlannedShutdownReason = 0x80000000;
        private readonly Action<uint> _endSession;
        private readonly Action<bool> _suspend;

        internal WindowsPowerService() : this(EndSession, Suspend) { }

        // Tests replace both native boundaries; they never change the actual power/session state.
        internal WindowsPowerService(Action<uint> endSession, Action<bool> suspend)
        {
            _endSession = endSession ?? throw new ArgumentNullException(nameof(endSession));
            _suspend = suspend ?? throw new ArgumentNullException(nameof(suspend));
        }

        internal void Execute(WindowsPowerAction action)
        {
            switch (action)
            {
                case WindowsPowerAction.SignOut: _endSession(EwxLogoff); break;
                case WindowsPowerAction.Sleep: _suspend(false); break;
                case WindowsPowerAction.Hibernate: _suspend(true); break;
                case WindowsPowerAction.ShutDown: _endSession(EwxPoweroff); break;
                case WindowsPowerAction.Restart: _endSession(EwxReboot); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        private static void EndSession(uint flags)
        {
            void Request()
            {
                // No EWX_FORCE/EWX_FORCEIFHUNG: Windows lets applications protect unsaved work.
                if (!ExitWindowsEx(flags, PlannedShutdownReason))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (flags == EwxLogoff) Request();
            else WithShutdownPrivilege(Request);
        }

        private static void Suspend(bool hibernate)
        {
            WithShutdownPrivilege(() =>
            {
                // Preserve normal wake events. Do not enable hibernation or alter power settings.
                if (!SetSuspendState(hibernate, false, false))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            });
        }

        private static void WithShutdownPrivilege(Action action)
        {
            const uint TokenQuery = 0x0008;
            const uint TokenAdjustPrivileges = 0x0020;
            const uint SePrivilegeEnabled = 0x0002;

            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenAdjustPrivileges, out var token))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            using (token)
            {
                if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                var requested = new TokenPrivileges { Count = 1, Luid = luid, Attributes = SePrivilegeEnabled };
                if (!AdjustTokenPrivileges(token, false, ref requested, Marshal.SizeOf<TokenPrivileges>(),
                    out var previous, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                // A successful BOOL can still mean ERROR_NOT_ALL_ASSIGNED (a restricted account).
                int error = Marshal.GetLastWin32Error();
                if (error != 0) throw new Win32Exception(error);

                try { action(); }
                finally
                {
                    if (!AdjustTokenPrivileges(token, false, ref previous, Marshal.SizeOf<TokenPrivileges>(),
                        out _, out _))
                        System.Diagnostics.Debug.WriteLine("Could not restore shutdown privilege: " +
                            Marshal.GetLastWin32Error());
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public uint Count;
            public Luid Luid;
            public uint Attributes;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExitWindowsEx(uint flags, uint reason);

        [DllImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
            [MarshalAs(UnmanagedType.U1)] bool force, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvents);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out SafeAccessTokenHandle token);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool LookupPrivilegeValue(string system, string name, out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token,
            [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges requested,
            int length, out TokenPrivileges previous, out int returnLength);
    }
}
