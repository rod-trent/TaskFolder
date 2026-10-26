using System;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using TaskFolder.Models;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string description)
    {
        if (!value) throw new Exception("FAIL: " + description);
        _checks++;
    }

    // No windows, cursor movement, user configuration writes, or app launch.
    [MTAThread]
    private static void Main()
    {
        var assembly = typeof(AppSettings).Assembly;
        var dwellType = assembly.GetType("TaskFolder.Services.TaskbarHoverDwell", true);
        object dwell = Activator.CreateInstance(dwellType, nonPublic: true);
        var update = dwellType.GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
        bool Tick(Rectangle target, long time) => (bool)update.Invoke(dwell, new object[] { target, time });
        var button = new Rectangle(500, 1040, 48, 40);
        var secondMonitor = new Rectangle(-800, 1400, 48, 60);
        Check(!Tick(Rectangle.Empty, 0), "empty target never opens");
        Check(!Tick(button, 100), "enter starts dwell");
        Check(!Tick(button, 399), "299 ms is too early");
        Check(Tick(button, 400), "300 ms opens");
        Check(!Tick(button, 700), "staying on button does not reopen");
        Check(!Tick(button, 5000), "long stay still does not reopen");
        Check(!Tick(Rectangle.Empty, 5001), "leaving rearms");
        Check(!Tick(button, 5002), "reenter starts a fresh delay");
        Check(!Tick(Rectangle.Empty, 5200), "leaving before deadline cancels");
        Check(!Tick(button, 6000), "new entry starts over");
        Check(!Tick(secondMonitor, 6200), "moving button/monitor resets dwell");
        Check(!Tick(secondMonitor, 6499), "new button gets the full delay");
        Check(Tick(secondMonitor, 6500), "negative monitor coordinates work");
        Check(!Tick(Rectangle.Empty, 6501), "disable/drag/menu clears dwell");
        Check(!Tick(secondMonitor, 7000), "reenable requires new dwell");
        Check(Tick(secondMonitor, 7300), "reenable opens after 300 ms");

        var oldSettings = JsonSerializer.Deserialize<AppSettings>("{\"HotkeyKey\":\"F9\"}");
        Check(oldSettings.TaskbarButtonEnabled && oldSettings.TaskbarHoverEnabled,
            "existing settings opt into prototype defaults");
        Check(oldSettings.HotkeyKey == "F9", "existing preferences are preserved");
        oldSettings.TaskbarHoverEnabled = false;
        var saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(oldSettings));
        Check(saved.TaskbarButtonEnabled && !saved.TaskbarHoverEnabled, "hover off survives serialization");
        saved.TaskbarButtonEnabled = false;
        Check(!JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(saved)).TaskbarButtonEnabled,
            "taskbar button off survives serialization");

        // Exercises real COM activation, interface GUIDs, and walker vtable; does not inspect desktop UI.
        var automationType = assembly.GetType("TaskFolder.Utilities.TaskbarAutomation", true);
        var matchesMethod = automationType.GetMethod("MatchesIdentity", BindingFlags.Static | BindingFlags.NonPublic);
        bool Matches(string id, string label) => (bool)matchesMethod.Invoke(null, new object[] { id, label });
        Check(Matches("Appid: TaskFolder.TaskFolder", "TaskFolder - 1 running window"),
            "space after Appid and decorated label are supported");
        Check(Matches("Appid:TaskFolder.TaskFolder", "localized label"), "compact Appid still works");
        Check(Matches("  APPID:   TASKFOLDER.TASKFOLDER  ", null), "Appid whitespace and casing are normalized");
        Check(Matches("TaskFolder.TaskFolder", null), "raw exact app identity is accepted");
        Check(Matches(null, "TaskFolder"), "missing Appid retains exact-name fallback");
        Check(!Matches("Appid: Another.App", "TaskFolder"), "different explicit identity cannot use name fallback");
        Check(!Matches("Appid: TaskFolder.TaskFolder.Other", "TaskFolder"), "identity prefix does not match another app");
        Check(!Matches(null, "TaskFolder - another app"), "ambiguous label alone is rejected");
        using (var automation = (IDisposable)Activator.CreateInstance(automationType, nonPublic: true))
            Check(automation != null, "native UI Automation client and control walker initialize");

        var hoverType = assembly.GetType("TaskFolder.Services.TaskbarHoverService", true);
        var service = (IDisposable)Activator.CreateInstance(hoverType, nonPublic: true);
        Check(!(bool)hoverType.GetProperty("Enabled", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(service), "worker starts disabled");
        service.Dispose();
        service.Dispose();
        Check(true, "worker shutdown is idempotent");
        Console.WriteLine($"TASKBAR_PROTOTYPE_SMOKE_OK: {_checks} checks; timing, persistence, COM initialization, shutdown. Real taskbar interaction is not tested.");
    }
}
