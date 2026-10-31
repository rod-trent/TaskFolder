using System;

namespace TaskFolder.Services
{
    /// <summary>
    /// Lightweight fuzzy string scorer used by the tray-menu search box.
    /// </summary>
    public static class FuzzySearch
    {
        /// <summary>
        /// Returns a positive score when <paramref name="query"/> fuzzy-matches
        /// <paramref name="target"/>, or 0 when there is no match at all.
        /// Higher scores are better matches.
        ///
        /// Scoring tiers (descending):
        ///   2000 — exact match
        ///   1000 — target contains query as a substring
        ///    10+ — every character of query appears in target in order
        ///      0 — not all query characters found → no match
        /// </summary>
        public static int Score(string target, string query)
        {
            if (string.IsNullOrEmpty(query)) return 1;
            if (string.IsNullOrEmpty(target)) return 0;

            string t = target.ToLowerInvariant();
            string q = query.ToLowerInvariant();

            if (t == q)
                return 2000;

            int substringIdx = t.IndexOf(q, StringComparison.Ordinal);
            if (substringIdx >= 0)
                return 1000 + (t.Length - substringIdx);

            // Character-order fuzzy match
            int ti = 0, qi = 0, score = 0, lastMatch = -1;
            while (ti < t.Length && qi < q.Length)
            {
                if (t[ti] == q[qi])
                {
                    // Consecutive run bonus
                    score += (lastMatch == ti - 1) ? 10 : 2;
                    // Word-start bonus
                    if (ti == 0 || t[ti - 1] == ' ' || t[ti - 1] == '-' || t[ti - 1] == '_')
                        score += 5;
                    lastMatch = ti;
                    qi++;
                }
                ti++;
            }

            return qi == q.Length ? score : 0;
        }

        /// <summary>Returns true when <paramref name="query"/> fuzzy-matches <paramref name="target"/>.</summary>
        public static bool Matches(string target, string query) => Score(target, query) > 0;
    }
}
