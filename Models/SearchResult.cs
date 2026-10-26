using System;

namespace TaskFolder.Models
{
    internal enum SearchMatchKind { Exact, Prefix, Tokens, Substring, Layout, Fuzzy }
    internal sealed class SearchResult
    {
        internal Guid Id;
        internal ShortcutItem Item;
        internal string DisplayName, Breadcrumb;
        internal SearchMatchKind MatchKind;
        internal int Score;
    }
    internal sealed class SearchSelection
    {
        private string _query;
        private Guid _profile;
        internal SearchResult[] Results { get; private set; } = Array.Empty<SearchResult>();
        internal Guid? SelectedResultId { get; private set; }
        internal void Update(string query, Guid profile, SearchResult[] results)
        {
            bool typed = _query != query; bool sameProfile = _profile == profile;
            Results = results; _query = query; _profile = profile;
            if (typed) SelectedResultId = results.Length == 0 ? null : results[0].Id;
            else if (!sameProfile || !results.AnyId(SelectedResultId)) SelectedResultId = null;
        }
        internal void Move(int direction)
        { if (Results.Length == 0) { SelectedResultId = null; return; } int index = Array.FindIndex(Results, result => result.Id == SelectedResultId);
            SelectedResultId = Results[Math.Clamp(index < 0 ? 0 : index + direction, 0, Results.Length - 1)].Id; }
        internal SearchResult Selected => Array.Find(Results, result => result.Id == SelectedResultId);
    }
    internal static class SearchResultExtensions
    { internal static bool AnyId(this SearchResult[] results, Guid? id) => id.HasValue && Array.Exists(results, result => result.Id == id.Value); }
}
