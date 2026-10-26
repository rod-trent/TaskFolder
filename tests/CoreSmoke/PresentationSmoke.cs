using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using TaskFolder;
using TaskFolder.Models;
using TaskFolder.Services;

internal static class PresentationSmoke
{
    internal static void Run(Action<bool, string> check)
    {
        var en = LocalizationService.Entries("en").ToDictionary(p => p.Key, p => p.Value);
        var ru = LocalizationService.Entries("ru").ToDictionary(p => p.Key, p => p.Value);
        check(en.Keys.ToHashSet().SetEquals(ru.Keys), "RU/EN resource key sets differ.");
        foreach (var entry in en)
        {
            var english = Regex.Matches(entry.Value, @"\{(\d+)(?:[^}]*)\}").Select(m => int.Parse(m.Groups[1].Value)).OrderBy(n => n).ToArray();
            var russian = Regex.Matches(ru[entry.Key], @"\{(\d+)(?:[^}]*)\}").Select(m => int.Parse(m.Groups[1].Value)).OrderBy(n => n).ToArray();
            check(english.SequenceEqual(russian) && ru[entry.Key].Length > 0, "Resource parameters/translation missing: " + entry.Key);
            object[] values = Enumerable.Range(0, english.Length == 0 ? 0 : english.Max() + 1).Select(i => (object)i).ToArray();
            _ = string.Format(CultureInfo.GetCultureInfo("en"), entry.Value, values); _ = string.Format(CultureInfo.GetCultureInfo("ru"), ru[entry.Key], values);
        }
        foreach (var sample in new[] { (0L,"ярлыков"), (1L,"ярлык"), (2L,"ярлыка"), (5L,"ярлыков"), (11L,"ярлыков"), (21L,"ярлык"), (22L,"ярлыка"), (101L,"ярлык") })
            check(LocalizationService.Plural("ShortcutCount_", sample.Item1, "Русский").EndsWith(sample.Item2), "Russian plural failed: " + sample.Item1);
        check(LocalizationService.Plural("ShortcutCount_", 1, "English") == "1 shortcut" && LocalizationService.Plural("ShortcutCount_", 2, "English") == "2 shortcuts", "English plural failed.");
        var light = UiThemeService.Resolve("Light", true, false); var dark = UiThemeService.Resolve("Dark", false, false);
        check(light.Text.GetBrightness() < light.Surface.GetBrightness() && dark.Text.GetBrightness() > dark.Surface.GetBrightness(), "Palette foreground/background is unreadable.");
        check(UiThemeService.Resolve("System", true, false).Surface == dark.Surface && UiThemeService.Resolve("System", false, false).Surface == light.Surface, "System preference mapping failed.");
        var contrast = UiThemeService.Resolve("Dark", true, true); check(contrast.Surface == SystemColors.Window && contrast.Text == SystemColors.WindowText, "Custom palette overrides high contrast.");
        string previous = LocalizationService.CurrentLanguage;
        try { LocalizationService.CurrentLanguage = "Русский";
            check(LocalizationService.Text("Search shortcuts") == "Поиск ярлыков", "Runtime resource lookup failed.");
            using var menu = TaskFolderApplicationContext.BuildPowerMenu((text, handler) => { var item = new ToolStripMenuItem(text); if (handler != null) item.Click += handler; return item; }, _ => throw new Exception("No action should run."));
            check(menu.Text == "Питание" && menu.DropDownItems[0].Text == "Выйти из учётной записи", "RU power labels were not localized.");
            using var content = new ContextMenuStrip();
            var userFolder = new ToolStripMenuItem("Power") { Tag = "category" }; content.Items.Add(userFolder);
            UiThemeService.Apply(content); check(userFolder.Text == "Power", "Theme/language refresh translated a user-owned folder name.");
            LocalizationService.CurrentLanguage = "English"; check(LocalizationService.Relocalize("Поиск ярлыков") == "Search shortcuts", "Open-window language change cannot recover the English resource."); }
        finally { LocalizationService.CurrentLanguage = previous; }
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { Language = "Русский", Theme = "Dark", WidgetPositionX = 34 }));
        check(restored.Language == "Русский" && restored.Theme == "Dark" && restored.WidgetPositionX == 34 && new AppSettings().Language == "System", "Presentation serialization/defaults lost compatibility.");
        Console.WriteLine("PASS presentation: " + en.Count + " RU/EN keys, matching format arguments, plurals, palette/system/high-contrast priority, RU power labels and persisted preferences; visual/DPI/IME checks remain separate");
    }
}
