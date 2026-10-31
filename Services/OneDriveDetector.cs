using System;
using Microsoft.Win32;

namespace TaskFolder.Services
{
    /// <summary>
    /// Detects the local OneDrive folder path from the Windows registry,
    /// where the OneDrive client writes its sync root under
    /// HKCU\Software\Microsoft\OneDrive (value: UserFolder).
    /// </summary>
    public static class OneDriveDetector
    {
        /// <summary>
        /// Returns the user's OneDrive root path, or null if OneDrive is not
        /// installed or the path cannot be determined.
        /// </summary>
        public static string GetOneDrivePath()
        {
            try
            {
                // Personal OneDrive
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive");
                if (key != null)
                {
                    string path = key.GetValue("UserFolder") as string;
                    if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
                        return path;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OneDriveDetector: {ex.Message}");
            }

            // Fallback: environment variable set by OneDrive client
            try
            {
                string path = Environment.GetEnvironmentVariable("OneDrive");
                if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
                    return path;
            }
            catch { }

            return null;
        }

        /// <summary>Returns true when OneDrive appears to be installed and configured.</summary>
        public static bool IsInstalled() => GetOneDrivePath() != null;
    }
}
