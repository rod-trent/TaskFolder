using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TaskFolder.Services
{
    /// <summary>
    /// Checks the GitHub Releases API for a newer version and fires UpdateAvailable
    /// when one is found. Runs entirely on a background thread; safe to fire-and-forget.
    /// </summary>
    public static class UpdateChecker
    {
        private const string ApiUrl =
            "https://api.github.com/repos/rod-trent/TaskFolder/releases/latest";

        private const string CurrentVersion = "v1.3.0";

        public static event Action<string, string> UpdateAvailable; // (tagName, htmlUrl)

        public static void CheckAsync()
        {
            Task.Run(async () =>
            {
                try
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.UserAgent.ParseAdd(
                        $"TaskFolder/{CurrentVersion.TrimStart('v')} (+https://github.com/rod-trent/TaskFolder)");
                    client.Timeout = TimeSpan.FromSeconds(10);

                    string json = await client.GetStringAsync(ApiUrl);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (!root.TryGetProperty("tag_name", out var tagEl)) return;
                    if (!root.TryGetProperty("html_url", out var urlEl)) return;

                    string latestTag = tagEl.GetString() ?? string.Empty;
                    string htmlUrl   = urlEl.GetString() ?? string.Empty;

                    if (IsNewer(latestTag, CurrentVersion))
                        UpdateAvailable?.Invoke(latestTag, htmlUrl);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateChecker: {ex.Message}");
                }
            });
        }

        // Simple semver-style comparison: "v1.3.0" > "v1.2.0"
        private static bool IsNewer(string latest, string current)
        {
            static Version Parse(string tag)
            {
                var s = tag.TrimStart('v', 'V').Split('-')[0]; // strip pre-release suffix
                return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
            }
            return Parse(latest) > Parse(current);
        }
    }
}
