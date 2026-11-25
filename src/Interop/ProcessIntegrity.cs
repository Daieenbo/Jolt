using System;
using System.Runtime.InteropServices;

namespace Voltide.Interop
{
    /// <summary>
    /// Reads the integrity level of the current process.
    ///
    /// Why this exists: Windows denies Shell_NotifyIcon registration from an
    /// unprivileged Low-IL process (the shell returns ERROR_ACCESS_DENIED and
    /// WinForms drops the failure silently), so a Low-IL process can never show a
    /// tray icon. Executables started from a sandboxed workspace folder get a Low
    /// token even when Explorer launches them, which looks exactly like "the app
    /// runs but the tray stays empty". The entry point checks this up front and
    /// explains it instead of running without any UI.
    /// </summary>
    internal static class ProcessIntegrity
    {
        private const int TokenQuery = 0x0008;
        private const int TokenIntegrityLevel = 25;

        // SECURITY_MANDATORY_LOW_RID. Untrusted (0x0000) is below it and is equally
        // unable to talk to the shell, so anything at or under Low is rejected.
        private const int LowRid = 0x1000;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        public static bool IsLow()
        {
            try { return Level() <= LowRid; }
            catch { return false; } // never block the app because a probe failed
        }

        private static int Level()
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token)) return 0x2000;
            try
            {
                int length;
                GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out length);
                if (length <= 0) return 0x2000;

                IntPtr buffer = Marshal.AllocHGlobal(length);
                try
                {
                    int written;
                    if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out written)) return 0x2000;

                    // TOKEN_MANDATORY_LABEL starts with SID_AND_ATTRIBUTES:
                    // the buffer holds a pointer to the label SID, and the integrity
                    // RID is that SID's last sub-authority.
                    IntPtr sid = Marshal.ReadIntPtr(buffer);
                    int count = Marshal.ReadByte(sid, 1);
                    return Marshal.ReadInt32(sid, 8 + 4 * (count - 1));
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
    }
}
