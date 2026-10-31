using System;
using System.IO;
using System.Text.Json;

namespace TaskFolder.Services
{
    /// <summary>
    /// Detects the local Dropbox folder path from Dropbox's own info.json,
    /// which Dropbox writes at %APPDATA%\Dropbox\info.json.
    /// </summary>
    public static class DropboxDetector
    {
        /// <summary>
        /// Returns the user's Dropbox root path, or null if Dropbox is not
        /// installed or the path cannot be determined.
        /// </summary>
        public static string GetDropboxPath()
        {
            try
            {
                string infoPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Dropbox", "info.json");

                if (!File.Exists(infoPath))
                    return null;

                string json = File.ReadAllText(infoPath);
                using var doc = JsonDocument.Parse(json);

                // Try "personal" first, then "business"
                foreach (string accountType in new[] { "personal", "business" })
                {
                    if (doc.RootElement.TryGetProperty(accountType, out var account) &&
                        account.TryGetProperty("path", out var pathProp))
                    {
                        string path = pathProp.GetString();
                        if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                            return path;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DropboxDetector: {ex.Message}");
            }

            return null;
        }

        /// <summary>Returns true when Dropbox appears to be installed and configured.</summary>
        public static bool IsInstalled() => GetDropboxPath() != null;
    }
}
