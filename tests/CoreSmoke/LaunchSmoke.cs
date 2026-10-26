using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

internal static class LaunchSmoke
{
    internal static void Run(Action<bool, string> check)
    {
        var calls = new List<ProcessStartInfo>();
        int statistics = 0;
        var service = new ShortcutLaunchService(_ => statistics++, calls.Add, _ => true);
        var pwa = new ShortcutItem { ShortcutFilePath = @"C:\fixture\PWA.lnk", TargetPath = @"C:\fixture\browser.exe",
            Arguments = "--app-id=one --profile-directory=\"Profile 1\"", WorkingDirectory = @"C:\fixture\work" };
        var launched = service.Launch(pwa);
        check(launched.Dispatched && launched.StatisticsResult.Success && statistics == 1 && calls.Count == 1,
            "Shell dispatch and launch accounting did not remain separate.");
        check(calls[0].FileName == pwa.ShortcutFilePath && calls[0].UseShellExecute && calls[0].Arguments == string.Empty,
            "Existing PWA link was replaced by direct executable dispatch.");
        var otherPwa = new ShortcutItem { ShortcutFilePath = @"C:\fixture\Other PWA.lnk", TargetPath = pwa.TargetPath, Arguments = "--app-id=two" };
        service.Launch(otherPwa);
        check(calls[1].FileName == otherPwa.ShortcutFilePath, "Two browser PWAs lost their individual Shell links.");
        var url = new ShortcutItem { ShortcutFilePath = @"C:\fixture\Link.url", TargetPath = "https://example.invalid/fixture" };
        service.Launch(url);
        check(calls[2].FileName == url.TargetPath && calls[2].UseShellExecute, "URL dispatch contract changed.");
        var fallback = new ShortcutLaunchService(_ => statistics++, calls.Add, _ => false);
        fallback.Launch(pwa);
        check(calls[3].FileName == pwa.TargetPath && calls[3].Arguments == pwa.Arguments && calls[3].WorkingDirectory == pwa.WorkingDirectory,
            "Fallback definition lost arguments or working directory.");
        int priorStatistics = statistics;
        var rejected = new ShortcutLaunchService(_ => statistics++, _ => throw new InvalidOperationException("Fake dispatch rejected"), _ => true).Launch(pwa);
        check(!rejected.Dispatched && rejected.DispatchError != null && rejected.StatisticsResult == null && statistics == priorStatistics,
            "Rejected dispatch was counted as a launch.");
        int dispatches = 0;
        var statsFailed = new ShortcutLaunchService(_ => throw new StatePersistenceException(
            OperationResult.Fail(OperationStatus.Conflict, "fixture.stale")), _ => dispatches++, _ => true).Launch(pwa);
        check(statsFailed.Dispatched && statsFailed.DispatchError == null && !statsFailed.StatisticsResult.Success && dispatches == 1,
            "Statistics failure incorrectly reported or retried Shell dispatch.");
        const string definition = "[InternetShortcut]\r\nURL=https://example.invalid/old\r\nIconFile=C:\\fixture\\custom.ico\r\nIconIndex=3\r\n";
        var repaired = ShortcutDefinitionEditor.RetargetUrl(Encoding.UTF8.GetBytes(definition), "https://example.invalid/new");
        check(Encoding.UTF8.GetString(repaired) == definition.Replace("/old", "/new"), "URL repair changed fields beyond its target.");
        var unicode = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(definition)).ToArray();
        var repairedUnicode = ShortcutDefinitionEditor.RetargetUrl(unicode, "https://example.invalid/new");
        check(repairedUnicode.Take(2).SequenceEqual(Encoding.Unicode.GetPreamble()) &&
            Encoding.Unicode.GetString(repairedUnicode, 2, repairedUnicode.Length - 2) == definition.Replace("/old", "/new"),
            "URL repair lost Unicode encoding or saved icon fields.");
        bool invalidUrlRejected = false;
        try { ShortcutDefinitionEditor.RetargetUrl(Encoding.UTF8.GetBytes(definition), "https://example.invalid/\nIconIndex=99"); }
        catch (ArgumentException) { invalidUrlRejected = true; }
        check(invalidUrlRejected, "URL repair accepted a multi-line definition injection.");
        Console.WriteLine("PASS launch dispatch definitions and statistics errors (fake dispatch only)");
    }
}
