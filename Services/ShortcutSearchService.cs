using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TaskFolder.Models;

namespace TaskFolder.Services
{
    internal static class ShortcutSearchService
    {
        private static readonly Regex Separators = new Regex(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);
        private const string En = "`qwertyuiop[]asdfghjkl;'zxcvbnm,.", Ru = "ёйцукенгшщзхъфывапролджэячсмитьбю";
        private static string Normalize(string value) => Separators.Replace((value ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant(), " ").Trim();
        internal static SearchResult[] Search(string query, LauncherMenuSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length > 256) return Array.Empty<SearchResult>();
            string normalized = Normalize(query); if (normalized.Length == 0) return Array.Empty<SearchResult>();
            string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string alternate = query.Any(c => c >= '\u1100' && c <= '\u11ff' || c >= '\u3130' && c <= '\u318f' || c >= '\uac00' && c <= '\ud7af') ? normalized : Normalize(MapLayout(query));
            var results = new List<SearchResult>();
            foreach (var item in snapshot.AllShortcuts)
            {
                string name = Normalize(item.GetDisplayName()); var names = new[] { name }.Concat(item.Aliases.Select(Normalize)).ToArray();
                int quality = names.Any(n => n == normalized) ? 0 : names.Any(n => n.StartsWith(normalized, StringComparison.Ordinal)) ? 1 :
                    tokens.All(token => names.Any(n => n.Contains(token, StringComparison.Ordinal))) ? 2 : names.Any(n => n.Contains(normalized, StringComparison.Ordinal)) ? 3 : 99;
                if (quality == 99 && alternate != normalized && alternate.Length >= 3 && alternate.Split(' ').All(token => names.Any(n => n.Contains(token, StringComparison.Ordinal)))) quality = 4;
                if (quality == 99 && normalized.Length >= 4 && tokens.Length == 1 && names.SelectMany(n => n.Split(' ')).Any(n => Near(normalized, n))) quality = 5;
                if (quality < 99) results.Add(new SearchResult { Id = item.Id, Item = item, DisplayName = item.GetDisplayName(), Breadcrumb = snapshot.ProfileName + " / " + (item.Category ?? ""), MatchKind = (SearchMatchKind)quality, Score = quality });
            }
            return results.OrderBy(result => result.Score).ThenByDescending(result => result.Item.IsPinned).ThenByDescending(result => result.Item.LastUsed ?? DateTime.MinValue)
                .ThenByDescending(result => result.Item.LaunchCount).ThenBy(result => result.DisplayName, StringComparer.OrdinalIgnoreCase).ThenBy(result => result.Id).ToArray();
        }
        internal static string MapLayout(string text)
        {
            bool cyrillic = text.Any(c => Ru.Contains(char.ToLowerInvariant(c))); string source = cyrillic ? Ru : En, target = cyrillic ? En : Ru;
            return new string(text.Select(c => { int index = source.IndexOf(char.ToLowerInvariant(c)); return index < 0 ? c : target[index]; }).ToArray());
        }
        private static bool Near(string left, string right)
        {
            int limit = left.Length >= 8 ? 2 : 1;
            if (left.Length > 128 || right.Length > 128 || Math.Abs(left.Length - right.Length) > limit) return false;
            var row = Enumerable.Range(0, right.Length + 1).ToArray();
            for (int i = 1; i <= left.Length; i++) { int previous = row[0]; row[0] = i; int minimum = i;
                for (int j = 1; j <= right.Length; j++) { int old = row[j]; row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (left[i - 1] == right[j - 1] ? 0 : 1)); previous = old; minimum = Math.Min(minimum, row[j]); }
                if (minimum > limit) return false; }
            return row[right.Length] <= limit;
        }
    }
}
