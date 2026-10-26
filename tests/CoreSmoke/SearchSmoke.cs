using System;
using System.Diagnostics;
using System.Linq;
using TaskFolder.Models;
using TaskFolder.Services;

internal static class SearchSmoke
{
    internal static void Run(Action<bool, string> check)
    {
        ShortcutItem Item(string name, string[] aliases = null, bool pin = false) => new ShortcutItem { Id = Guid.NewGuid(), ProfileId = ProfileState.DefaultId, Name = name,
            Aliases = aliases ?? Array.Empty<string>(), IsPinned = pin, ShortcutFilePath = name + ".lnk" };
        ShortcutItem exact = Item("Редактор 작업"), alias = Item("Music editor", new[] { "сведение 음악", "studio" }), fuzzy = Item("studios", pin: true), layout = Item("блокнот"), direct = Item(",kjryjn");
        var snapshot = new LauncherMenuSnapshot { ProfileId = ProfileState.DefaultId, ProfileName = "Default", AllShortcuts = new[] { fuzzy, alias, exact, layout, direct } };
        check(ShortcutSearchService.Search("РЕДАКТОР 작업", snapshot).First().Id == exact.Id, "Mixed Unicode exact match failed.");
        check(ShortcutSearchService.Search("음악 сведение", snapshot).Single().Id == alias.Id, "Unordered alias words failed.");
        check(ShortcutSearchService.Search("studio", snapshot).First().Id == alias.Id, "Pinned prefix outranked exact alias.");
        var wrong = ShortcutSearchService.Search(",kjryjn", snapshot);
        check(wrong.First().Id == direct.Id && wrong.Any(r => r.Id == layout.Id && r.MatchKind == SearchMatchKind.Layout), "Layout correction outranked direct match or failed.");
        check(ShortcutSearchService.Search("studo", snapshot).Any(r => r.Id == alias.Id && r.MatchKind == SearchMatchKind.Fuzzy), "Bounded typo match missing.");
        check(ShortcutSearchService.Search("zz", snapshot).Length == 0 && ShortcutSearchService.Search("нет такого результата", snapshot).Length == 0, "Short/random query produced fuzzy noise.");
        check(ShortcutSearchService.MapLayout("작업") == "작업", "Keyboard map changed Korean text.");
        var selection = new SearchSelection(); var results = ShortcutSearchService.Search("stud", snapshot); selection.Update("stud", snapshot.ProfileId, results);
        check(selection.SelectedResultId == results[0].Id, "Typing did not select first result."); selection.Move(1); var selected = selection.SelectedResultId;
        selection.Update("stud", snapshot.ProfileId, results.Reverse().ToArray()); check(selection.SelectedResultId == selected, "Background reorder changed stable selection.");
        selection.Update("stud", snapshot.ProfileId, results.Where(r => r.Id != selected).ToArray()); check(selection.SelectedResultId == null && selection.Selected == null, "Removed selection fell back to stale launch.");
        selection.Update("zero", snapshot.ProfileId, Array.Empty<SearchResult>()); selection.Move(1); check(selection.Selected == null, "Empty result has a launch target.");
        selection.Update("stud", snapshot.ProfileId, results); selection.Update("stud", Guid.NewGuid(), results); check(selection.Selected == null, "Profile change retained a stale selected ID.");
        var stable = ShortcutSearchService.Search("stud", snapshot).Select(r => r.Id).ToArray(); check(Enumerable.Range(0, 5).All(_ => ShortcutSearchService.Search("stud", snapshot).Select(r => r.Id).SequenceEqual(stable)), "Ranking is unstable.");
        var large = new LauncherMenuSnapshot { ProfileId = ProfileState.DefaultId, ProfileName = "Benchmark", AllShortcuts = Enumerable.Range(0, 1000).Select(i => Item("Редактор editor 작업 " + i, new[] { "music studio " + i })).ToArray() };
        ShortcutSearchService.Search("editor 작업", large); var samples = new double[40];
        for (int i = 0; i < samples.Length; i++) { var clock = Stopwatch.StartNew(); var found = ShortcutSearchService.Search(i % 2 == 0 ? "editor 작업" : "studio 123", large); clock.Stop(); samples[i] = clock.Elapsed.TotalMilliseconds; }
        Array.Sort(samples); double p95 = samples[(int)Math.Ceiling(samples.Length * .95) - 1]; check(p95 <= 100, "Warm 1000-entry search exceeds 100 ms p95: " + p95);
        Console.WriteLine("PASS search: ranking/layout/aliases/Unicode/stable selection/zero results; 1000-entry warm p95=" + p95.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "ms; process architecture=" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
    }
}
