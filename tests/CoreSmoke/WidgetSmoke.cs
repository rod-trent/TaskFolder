using System;
using System.Drawing;
using System.Text.Json;
using System.IO;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Utilities;

internal static class WidgetSmoke
{
    internal static void Run(Action<bool,string> check)
    {
        foreach (var boundary in new[] { (99,100), (100,100), (300,300), (1500,1500), (1501,1500) })
        { var pointer = new WidgetInteraction(); pointer.Configure(false, boundary.Item1); pointer.Enter(1000); check(pointer.HoverDelay == boundary.Item2 && !pointer.HoverDue(1000 + boundary.Item2 - 1, true, false) && pointer.HoverDue(1000 + boundary.Item2, true, false), "Hover boundary/clamp failed."); check(!pointer.HoverDue(10000, true, false), "Hover repeated without leave."); pointer.Leave(); pointer.Enter(20000); check(!pointer.HoverDue(20000 + boundary.Item2, false, false) && !pointer.HoverDue(20000 + boundary.Item2, true, true), "Disabled/menu-open hover fired."); }
        var locked = new WidgetInteraction(); locked.Configure(true, 300); locked.Press(new Point(-800, 20)); check(!locked.Move(new Point(-500, 200), new Size(4,4)) && locked.Release(true) == WidgetRelease.Click, "Locked widget cannot click or still drags.");
        locked.Configure(false, 300); locked.Press(new Point(-800,20)); check(locked.Move(new Point(-500,200), new Size(4,4)), "Unlocked drag blocked."); locked.Configure(true,300); check(!locked.Pressed && !locked.Dragging && locked.Release(true) == WidgetRelease.None, "Lock during press committed/clicked unexpectedly.");
        locked.Configure(false,300); locked.Press(Point.Empty); locked.Move(new Point(50,50),new Size(4,4)); locked.Configure(false,500); check(locked.Release(true) == WidgetRelease.None, "Delay change during press did not cancel drag.");
        var placement = WidgetPlacement.Restore(new Rectangle(-1920,-300,1920,1080),new Size(44,44),1,1); check(placement.X == -44 && placement.Y == 736, "Negative monitor placement changed.");
        check(WidgetPlacement.Clamp(new Point(-9999,9999),new Size(88,88),new Rectangle(-1920,0,1920,1080)) == new Point(-1920,992), "Scaled widget clamp failed.");
        var area = new Rectangle(-1920, -300, 1920, 1080);
        var size = new Size(44, 44);
        var free = WidgetPlacement.Clamp(new Point(-1500, 350), size, area);
        check(free == new Point(-1500, 350), "Desktop drag must retain both axes instead of snapping to a taskbar.");
        var fractions = WidgetPlacement.Normalize(area, new Rectangle(free, size));
        check(WidgetPlacement.Restore(area, size, fractions.X, fractions.Y) == free, "Desktop position did not round-trip on a negative-origin monitor.");
        check(area.Contains(new Rectangle(WidgetPlacement.Restore(area, size, double.NaN, double.PositiveInfinity), size)), "Corrupt desktop position escaped monitor bounds.");
        check(WidgetPlacement.Clamp(new Point(200, 200), size, new Rectangle(10, 20, 20, 20)) == new Point(10, 20), "Small desktop area produced invalid clamp bounds.");
        var old = JsonSerializer.Deserialize<AppSettings>("{\"WidgetPositionX\":42,\"WidgetTaskbarPosition\":0.6}"); check(!old.WidgetPositionLocked && old.WidgetHoverDelayMs == 300 && old.WidgetPositionX == 42 && old.WidgetTaskbarPosition == .6, "Legacy settings lost defaults/position.");
        old.WidgetPositionLocked = true; old.WidgetHoverDelayMs=1500; var saved=JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(old)); check(saved.WidgetPositionLocked && saved.WidgetHoverDelayMs==1500 && saved.WidgetTaskbarPosition==.6, "Widget preferences did not persist.");
        old.WidgetDisplayName = "DISPLAY2"; old.WidgetPositionX = fractions.X; old.WidgetPositionY = fractions.Y;
        saved = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(old));
        check(saved.WidgetDisplayName == "DISPLAY2" && WidgetPlacement.Restore(area, size, saved.WidgetPositionX.Value, saved.WidgetPositionY.Value) == free && saved.WidgetTaskbarPosition == .6, "Desktop persistence lost a coordinate or changed the legacy taskbar offset.");
        CheckWidgetSettings(check);
        CheckMenuDismissal(check);
        Console.WriteLine("PASS widget: hover boundaries 99/100/299/300/1499/1500, one-shot/disabled/menu-open, lock click/drag, preference change during press, negative/scaled coordinates and settings restoration (pure pointer lane)");
    }

    private static void CheckWidgetSettings(Action<bool, string> check)
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"WidgetHoverDelayMs\":450}");
        check(old.WidgetSizePercent == 100 && old.WidgetMenuHideDelayMs == 600 && old.WidgetHoverDelayMs == 450,
            "Older settings must keep existing widget size and receive a default hide delay.");
        string root = Path.Combine(Path.GetTempPath(), "taskfolder-widget-controls-20261026", "settings-" + Guid.NewGuid().ToString("N"));
        var service = new SettingsService(root);
        service.Settings.WidgetSizePercent = 170;
        service.Settings.WidgetMenuHideDelayMs = 900;
        service.Save();
        var reloaded = new SettingsService(root);
        check(reloaded.Settings.WidgetSizePercent == 170 && reloaded.Settings.WidgetMenuHideDelayMs == 900,
            "Widget controls must survive legacy settings save and reload.");
        var migration = new StateMigrationService(root);
        check(migration.Commit(migration.Preview()).Success, "Widget preferences fixture could not migrate.");
        reloaded = new SettingsService(root);
        check(reloaded.UsesStateV2 && reloaded.Settings.WidgetSizePercent == 170 && reloaded.Settings.WidgetMenuHideDelayMs == 900,
            "Widget controls must survive migration to state v2.");
        reloaded.Settings.WidgetSizePercent = 50;
        reloaded.Settings.WidgetMenuHideDelayMs = 2000;
        reloaded.Save();
        var persisted = new SettingsService(root);
        check(persisted.Settings.WidgetSizePercent == 50 && persisted.Settings.WidgetMenuHideDelayMs == 2000,
            "Widget controls must survive state v2 save and reload.");
        foreach (var invalid in new[] { (49, 600), (201, 600), (100, 199), (100, 2001) })
        {
            var state = persisted.StateSnapshot;
            state.Settings.WidgetSizePercent = invalid.Item1;
            state.Settings.WidgetMenuHideDelayMs = invalid.Item2;
            bool rejected = false;
            try { new LauncherStateStore(root).Validate(state); }
            catch (InvalidDataException) { rejected = true; }
            check(rejected, "Invalid widget size/hide delay must be rejected by state validation.");
        }
    }

    private static void CheckMenuDismissal(Action<bool, string> check)
    {
        var widget = new Rectangle(10, 200, 44, 44);
        var menus = new[] { new Rectangle(10, 10, 150, 180), new Rectangle(160, 30, 120, 100) };
        bool visible = true;
        int closed = 0;
        using var dismissal = new HoverMenuDismissal(point => widget.Contains(point) || OutsideClickMenuDismissal.ContainsPoint(point, menus),
            () => visible, () => closed++);
        var outside = new Point(500, 500);
        dismissal.Start(600);
        dismissal.Poll(1000, outside); dismissal.Poll(1599, outside);
        check(closed == 0, "Leaving must not hide the menu before the delay.");
        dismissal.Poll(1600, outside); dismissal.Poll(2200, outside);
        check(closed == 1 && !dismissal.Active, "The leave timer must close once and stop.");
        dismissal.Start(600);
        dismissal.Poll(3000, outside); dismissal.Poll(3500, new Point(200, 50));
        dismissal.Poll(4000, outside); dismissal.Poll(4500, new Point(20, 220));
        dismissal.Poll(5000, outside); dismissal.Poll(5599, outside);
        check(closed == 1, "Reentering a submenu or the widget must reset the entire leave delay.");
        dismissal.Poll(5600, outside);
        check(closed == 2, "A fresh full leave delay must still hide the menu.");
        dismissal.Start(600);
        dismissal.Poll(6000, outside); dismissal.Poll(7000, outside, mouseDown: true);
        dismissal.Poll(7500, outside); dismissal.Poll(8099, outside);
        check(closed == 2, "Holding a mouse button must suspend and reset the hide countdown.");
        dismissal.Poll(8100, outside);
        check(closed == 3, "Releasing the mouse restores delayed dismissal.");
        foreach (int message in new[] { 0x0100, 0x0102, 0x0104, 0x010D, 0x0204, 0x0205 })
        {
            dismissal.Start(600); dismissal.Poll(9000, outside);
            var input = Message.Create(IntPtr.Zero, message, IntPtr.Zero, IntPtr.Zero);
            check(!dismissal.PreFilterMessage(ref input) && !dismissal.Active, "Keyboard, IME and context-menu input must cancel auto-hide without swallowing input.");
            dismissal.Poll(10000, outside);
        }
        check(closed == 3, "Keyboard/context actions must not leave a stale close callback.");
        dismissal.Start(600); dismissal.Poll(11000, outside);
        visible = false; dismissal.Poll(12000, outside);
        check(!dismissal.Active && closed == 3, "Closing the menu externally must stop the pending leave timer.");
        visible = true; dismissal.Start(600); dismissal.Poll(13000, outside);
        dismissal.Stop(); dismissal.Start(600); dismissal.Poll(14000, outside); dismissal.Poll(14599, outside);
        check(closed == 3, "Reopening must not inherit a previous session's deadline.");
        dismissal.Poll(14600, outside);
        check(closed == 4, "A reopened hover session still dismisses after its own delay.");
    }
}
