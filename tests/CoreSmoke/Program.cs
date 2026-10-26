using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using TaskFolder.Models;
using TaskFolder.Services;

internal static class Program
{
    private static int _assertions;
    private static string _scratchRoot;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !new[] { "baseline", "migration", "operations", "favorites", "organizer", "adding", "search", "backup", "presentation", "widget", "menu-motion", "all" }.Contains(args[0]))
        {
            Console.Error.WriteLine("Usage: CoreSmoke baseline|migration|operations|favorites|organizer|adding|search|backup|presentation|widget|menu-motion|all");
            return 2;
        }

        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            _scratchRoot = Path.Combine(Path.GetTempPath(), "TaskFolder-usability-qa",
                "core-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_scratchRoot);
            Console.WriteLine("Fixture root: " + _scratchRoot);

            if (args[0] == "baseline" || args[0] == "all")
            {
                Run("explicit data roots and defaults", TestDataRoots);
                Run("legacy settings and metadata round-trip", TestLegacyRoundTrip);
                Run("malformed legacy input remains untouched", TestMalformedInput);
                Run("profile roots and empty Unicode folders", TestProfilesAndFolders);
                Run("fake link definitions preserve launch inputs", TestShortcutTransport);
            }
            if (args[0] == "migration" || args[0] == "all") MigrationSmoke.Run(_scratchRoot, Check);
            if (args[0] == "operations" || args[0] == "all")
            {
                OperationsSmoke.Run(_scratchRoot, Check);
                LaunchSmoke.Run(Check);
                AdapterSmoke.Run(_scratchRoot, Check);
            }
            if (args[0] == "favorites" || args[0] == "all") FavoritesSmoke.Run(_scratchRoot, Check);
            if (args[0] == "organizer" || args[0] == "all") OrganizerSmoke.Run(_scratchRoot, Check);
            if (args[0] == "adding" || args[0] == "all") AddingSmoke.Run(_scratchRoot, Check);
            if (args[0] == "search" || args[0] == "all") SearchSmoke.Run(Check);
            if (args[0] == "backup" || args[0] == "all") BackupSmoke.Run(_scratchRoot, Check);
            if (args[0] == "presentation" || args[0] == "all") PresentationSmoke.Run(Check);
            if (args[0] == "widget" || args[0] == "all") WidgetSmoke.Run(Check);
            if (args[0] == "menu-motion" || args[0] == "all") MenuMotionSmoke.Run(Check);
            Console.WriteLine($"PASS {args[0]}: {_assertions} assertions (S0–S9 source/fixture lanes; native and GUI evidence reported separately).");
            Console.WriteLine("Native Shell parsing/launch, GUI, taskbar and installed data were not exercised.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex);
            return 1;
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            // Retain fixture evidence. No cleanup can remove pre-existing/user files.
        }
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine("PASS " + name);
    }

    private static string Fixture(string name)
    {
        string path = Path.Combine(_scratchRoot, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TestDataRoots()
    {
        string relativeRoot = "core-smoke-invalid-" + Guid.NewGuid().ToString("N");
        foreach (string root in new[] { null, "", "  ", relativeRoot, @"C:relative", @"\root-relative" })
        {
            bool rejected = false;
            try { _ = new SettingsService(root); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Invalid/relative data root was accepted.");
        }
        Check(!Directory.Exists(relativeRoot), "Rejected relative root created a directory.");

        string rootPath = Path.Combine(Fixture("roots"), "new-data");
        var settings = new SettingsService(rootPath);
        Check(settings.DataRoot == Path.GetFullPath(rootPath), "Explicit data root changed.");
        Check(!settings.IsPortable, "Explicit normal root became portable.");
        Check(settings.Settings.ActiveProfileName == "Default", "Missing settings lost default profile.");
        Check(settings.Settings.ShowNotifications && settings.Settings.TaskbarButtonEnabled &&
            settings.Settings.TaskbarHoverEnabled, "Missing settings changed baseline feature defaults.");
        Check(settings.Settings.MenuItemHeight == 36 && settings.Settings.MenuFontSize == 10.0f,
            "Missing settings changed menu size defaults.");
        Check(settings.GetMetadata("absent.lnk").LaunchCount == 0 &&
            settings.GetMetadata("absent.lnk").SortOrder == int.MaxValue,
            "Missing metadata did not use baseline defaults.");
        Check(!Directory.EnumerateFiles(rootPath).Any(), "Loading missing JSON unexpectedly saved defaults.");

        string portablePath = Path.Combine(Fixture("roots"), "portable-data");
        var portable = new SettingsService(portablePath, isPortable: true);
        Check(portable.IsPortable && portable.DataRoot == Path.GetFullPath(portablePath),
            "Portable flag overrode explicit fixture location.");
        portable.Settings.WidgetDisplayName = "Fixture portable";
        portable.Save();
        Check(File.Exists(Path.Combine(portablePath, "settings.json")),
            "Portable explicit root was not used for saving.");
    }

    private static void TestLegacyRoundTrip()
    {
        string root = Fixture("legacy-roundtrip");
        var expectedSettings = new AppSettings
        {
            ShowNotifications = false,
            StartWithWindows = true,
            TaskbarButtonEnabled = false,
            TaskbarHoverEnabled = false,
            WidgetDisplayName = "Работа 작업",
            WidgetPositionX = -800.25,
            WidgetPositionY = 400.5,
            WidgetTaskbarPosition = 0.625,
            ActiveProfileName = "Работа 작업",
            HotkeyModifiers = "Ctrl+Shift",
            HotkeyKey = "K",
            HotkeyEnabled = true,
            MenuItemHeight = 44,
            MenuFontSize = 12.5f,
            MenuHeaderText = "Legacy heading",
            MenuHeaderLogoPath = Path.Combine(root, "logo.png"),
            PortableMode = true
        };
        var expectedMetadata = new ShortcutMetadata
        {
            FileName = "Редактор 편집기.lnk",
            LaunchCount = 37,
            LastUsed = new DateTime(2026, 10, 20, 16, 25, 30, DateTimeKind.Unspecified),
            SortOrder = 4,
            CustomIconPath = Path.Combine(root, "custom.ico"),
            CustomIconIndex = 7
        };
        string settingsJson = JsonSerializer.Serialize(expectedSettings);
        string metadataJson = JsonSerializer.Serialize(new[] { expectedMetadata });
        File.WriteAllText(Path.Combine(root, "settings.json"), settingsJson);
        File.WriteAllText(Path.Combine(root, "metadata.json"), metadataJson);

        var settings = new SettingsService(root);
        AssertJsonEqual(settingsJson, JsonSerializer.Serialize(settings.Settings), "Loaded settings");
        AssertJsonEqual(JsonSerializer.Serialize(expectedMetadata),
            JsonSerializer.Serialize(settings.GetMetadata(expectedMetadata.FileName)), "Loaded metadata");
        settings.Save();
        AssertJsonEqual(settingsJson, File.ReadAllText(Path.Combine(root, "settings.json")), "Saved settings");
        AssertJsonEqual(metadataJson, File.ReadAllText(Path.Combine(root, "metadata.json")), "Saved metadata");

        var reopened = new SettingsService(root);
        AssertJsonEqual(settingsJson, JsonSerializer.Serialize(reopened.Settings), "Reopened settings");
        AssertJsonEqual(JsonSerializer.Serialize(expectedMetadata),
            JsonSerializer.Serialize(reopened.GetMetadata(expectedMetadata.FileName)), "Reopened metadata");
        Check(!File.Exists(Path.Combine(root, "settings.json.tmp")) &&
            !File.Exists(Path.Combine(root, "metadata.json.tmp")), "Successful legacy save left temporary files.");
    }

    private static void TestMalformedInput()
    {
        string root = Fixture("malformed-legacy");
        const string malformedSettings = "{ not settings JSON";
        const string malformedMetadata = "[ not metadata JSON";
        File.WriteAllText(Path.Combine(root, "settings.json"), malformedSettings);
        File.WriteAllText(Path.Combine(root, "metadata.json"), malformedMetadata);
        var settings = new SettingsService(root);
        Check(settings.Settings.ActiveProfileName == "Default", "Malformed legacy settings changed baseline fallback.");
        Check(settings.GetMetadata("missing.lnk").LaunchCount == 0, "Malformed metadata fallback changed.");
        Check(File.ReadAllText(Path.Combine(root, "settings.json")) == malformedSettings &&
            File.ReadAllText(Path.Combine(root, "metadata.json")) == malformedMetadata,
            "Reading malformed legacy input overwrote its evidence.");
    }

    private static void TestProfilesAndFolders()
    {
        string root = Fixture("profiles");
        string defaultRoot = Path.Combine(root, "Shortcuts");
        string nestedFolder = Path.Combine("Музыка 음악", "Пусто 빈 폴더");
        Directory.CreateDirectory(Path.Combine(defaultRoot, nestedFolder));
        var settings = new SettingsService(root);
        using var manager = new ShortcutManager(settings,
            path => throw new InvalidOperationException("Empty folder unexpectedly loaded a file."),
            (path, index) => throw new InvalidOperationException("Empty folder unexpectedly loaded an icon."),
            watchForChanges: false);
        Check(manager.ShortcutsFolder == defaultRoot, "Default profile root moved.");
        Check(manager.ResolveShortcutsFolder("default") == defaultRoot &&
            manager.ResolveShortcutsFolder(null) == defaultRoot, "Legacy Default aliases changed.");
        var folders = manager.GetAllFolders();
        Check(folders.Count == 2 && folders.Contains("Музыка 음악") && folders.Contains(nestedFolder),
            "Intermediate/empty Unicode folders were omitted.");
        Check(manager.GetAllShortcuts().Count == 0, "Empty folder produced a shortcut.");
        AssertWatcherDisabled(manager);

        string profileRoot = Path.Combine(root, "Profiles", "Работа 작업");
        Check(manager.ResolveShortcutsFolder("Работа 작업") == profileRoot, "Named profile root changed.");
        Directory.CreateDirectory(Path.Combine(profileRoot, "Empty"));
        int changes = 0;
        manager.ShortcutsChanged += (sender, args) => changes++;
        manager.SetShortcutsFolder(profileRoot);
        Check(manager.ShortcutsFolder == profileRoot && manager.GetAllFolders().SequenceEqual(new[] { "Empty" }),
            "Profile switch retained folders from another profile.");
        Check(changes == 1, "Profile switch did not emit one immediate refresh.");
        AssertWatcherDisabled(manager);

        settings.Settings.ActiveProfileName = "Работа 작업";
        settings.Save();
        var reopened = new SettingsService(root);
        using var activeManager = new ShortcutManager(reopened, path => null, (path, index) => null, false);
        Check(activeManager.ShortcutsFolder == profileRoot, "Restart did not resolve saved active profile.");
        AssertWatcherDisabled(activeManager);
    }

    private static void TestShortcutTransport()
    {
        string root = Fixture("shortcut-transport");
        string shortcutRoot = Path.Combine(root, "Shortcuts");
        string nestedRoot = Path.Combine(shortcutRoot, "Веб 웹");
        Directory.CreateDirectory(nestedRoot);
        string target = Path.Combine(root, "browser-fixture.exe");
        File.WriteAllText(target, "synthetic target; never executed");
        string iconPath = Path.Combine(root, "fixture.ico");
        File.WriteAllText(iconPath, "synthetic icon; injected reader only");
        string firstPath = Path.Combine(shortcutRoot, "PWA one.lnk");
        string secondPath = Path.Combine(nestedRoot, "PWA two.lnk");
        string urlPath = Path.Combine(shortcutRoot, "Ссылка 링크.url");
        string exePath = Path.Combine(shortcutRoot, "Fixture.exe");
        foreach (string path in new[] { firstPath, secondPath, urlPath, exePath })
            File.WriteAllText(path, "synthetic definition; fake reader only");
        File.WriteAllText(Path.Combine(shortcutRoot, "ignore.txt"), "not a shortcut");
        byte[] firstBytes = File.ReadAllBytes(firstPath);
        byte[] secondBytes = File.ReadAllBytes(secondPath);

        var settings = new SettingsService(root);
        settings.SaveMetadata(new ShortcutMetadata
        {
            FileName = Path.GetFileName(firstPath), LaunchCount = 9, SortOrder = 2,
            CustomIconPath = iconPath, CustomIconIndex = 3,
            LastUsed = new DateTime(2026, 10, 1, 11, 12, 13)
        });
        var reads = new List<string>();
        var icons = new List<(string Path, int Index)>();
        Func<string, ShortcutItem> reader = path =>
        {
            reads.Add(path);
            string arguments = path == firstPath ? "--app-id=one --profile-directory=\"Profile 1\"" :
                path == secondPath ? "--app-id=two --profile-directory=\"Profile 1\"" : "";
            return new ShortcutItem
            {
                Name = Path.GetFileNameWithoutExtension(path), ShortcutFilePath = path,
                TargetPath = path == urlPath ? "https://example.invalid/fixture" : target,
                Arguments = arguments, WorkingDirectory = root,
                Description = "fixture definition", DateCreated = new DateTime(2026, 10, 1)
            };
        };
        using var manager = new ShortcutManager(settings, reader,
            (path, index) => { icons.Add((path, index)); return null; }, false);
        var items = manager.GetAllShortcuts();
        Check(items.Count == 4 && reads.Count == 4 && !reads.Any(path => path.EndsWith(".txt")),
            "Supported definitions were omitted or unsupported content was read.");
        var first = items.Single(item => item.ShortcutFilePath == firstPath);
        var second = items.Single(item => item.ShortcutFilePath == secondPath);
        Check(first.TargetPath == second.TargetPath && first.Arguments != second.Arguments,
            "Two PWA definitions sharing an executable were collapsed.");
        Check(first.Arguments == "--app-id=one --profile-directory=\"Profile 1\"" &&
            second.Arguments == "--app-id=two --profile-directory=\"Profile 1\"" &&
            first.WorkingDirectory == root && second.WorkingDirectory == root,
            "Manager changed fake-reader arguments/working directory.");
        Check(second.Category == "Веб 웹" && first.Category == null, "Folder category mapping changed.");
        Check(first.LaunchCount == 9 && first.SortOrder == 2 &&
            first.LastUsed == new DateTime(2026, 10, 1, 11, 12, 13), "Legacy metadata was not applied.");
        Check(icons.Count == 1 && icons[0] == (iconPath, 3), "Custom icon bypassed injected reader on load.");
        Check(items.Single(item => item.ShortcutFilePath == urlPath).TargetPath == "https://example.invalid/fixture",
            "URL fixture definition was dropped by file-target validation.");
        manager.SetCustomIcon(first, iconPath, 8);
        Check(icons.Count == 2 && icons[1] == (iconPath, 8), "Custom icon update bypassed injected reader.");
        Check(new SettingsService(root).GetMetadata(Path.GetFileName(firstPath)).CustomIconIndex == 8,
            "Custom icon metadata update was not saved in fixture root.");
        manager.ReloadShortcuts();
        Check(manager.GetAllShortcuts().Count == 4 && icons.Count == 3 && icons[2] == (iconPath, 8),
            "Reload did not retain fixture definitions/custom icon.");
        Check(firstBytes.SequenceEqual(File.ReadAllBytes(firstPath)) &&
            secondBytes.SequenceEqual(File.ReadAllBytes(secondPath)), "Loading/settings changed source link bytes.");
        AssertWatcherDisabled(manager);
    }

    private static void AssertWatcherDisabled(ShortcutManager manager)
    {
        var watcherField = typeof(ShortcutManager).GetField("_fileWatcher", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(watcherField != null && watcherField.GetValue(manager) == null,
            "Isolated manager created a filesystem watcher.");
    }

    private static void AssertJsonEqual(string expected, string actual, string context)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        Check(JsonEquals(expectedDocument.RootElement, actualDocument.RootElement), context + " lost/changed fields.");
    }

    private static bool JsonEquals(JsonElement expected, JsonElement actual)
    {
        if (expected.ValueKind != actual.ValueKind) return false;
        if (expected.ValueKind == JsonValueKind.Object)
        {
            var properties = expected.EnumerateObject().ToArray();
            return properties.Length == actual.EnumerateObject().Count() && properties.All(property =>
                actual.TryGetProperty(property.Name, out var value) && JsonEquals(property.Value, value));
        }
        if (expected.ValueKind == JsonValueKind.Array)
        {
            var values = expected.EnumerateArray().ToArray();
            var otherValues = actual.EnumerateArray().ToArray();
            return values.Length == otherValues.Length && values.Where((value, index) => !JsonEquals(value, otherValues[index])).Count() == 0;
        }
        if (expected.ValueKind == JsonValueKind.String) return expected.GetString() == actual.GetString();
        if (expected.ValueKind == JsonValueKind.Number) return expected.GetDecimal() == actual.GetDecimal();
        return expected.ValueKind == actual.ValueKind;
    }
}
