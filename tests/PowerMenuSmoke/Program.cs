using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using TaskFolder;
using TaskFolder.Services;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var calls = new List<string>();
        var service = new WindowsPowerService(
            flags => calls.Add("exit:" + flags),
            hibernate => calls.Add("suspend:" + hibernate));
        using var menu = TaskFolderApplicationContext.BuildPowerMenu(
            (text, click) => new ToolStripMenuItem(text, null, click), service.Execute);
        Check(menu.Text == "Power", "English submenu title");
        Check(menu.DropDownItems.Cast<ToolStripItem>().Select(item => item.Text).SequenceEqual(new[]
        {
            "Sign Out", "Sleep", "Hibernate", "Shut Down", "Restart"
        }), "all five requested labels in order");
        Check(calls.Count == 0, "constructing menu does not execute a power action");

        string[] expectedCalls = { "exit:0", "suspend:False", "suspend:True", "exit:8", "exit:2" };
        for (int i = 0; i < menu.DropDownItems.Count; i++)
        {
            menu.DropDownItems[i].PerformClick();
            Check(calls.Count == i + 1 && calls[i] == expectedCalls[i],
                "click dispatches only its Windows action: " + menu.DropDownItems[i].Text);
        }
        // Exit flags above deliberately exclude FORCE and FORCEIFHUNG.
        menu.DropDownItems[1].PerformClick();
        Check(calls.Count == 6 && calls[5] == "suspend:False", "sleep remains callable after resuming");
        Expect<ArgumentOutOfRangeException>(() => service.Execute((WindowsPowerAction)999),
            "unknown action rejected");
        Check(calls.Count == 6, "invalid action makes no native request");

        var failure = new Win32Exception(5);
        var failing = new WindowsPowerService(_ => throw failure, _ => throw failure);
        foreach (var action in Enum.GetValues<WindowsPowerAction>())
        {
            try { failing.Execute(action); throw new Exception("Expected native failure"); }
            catch (Win32Exception ex)
            {
                Check(ReferenceEquals(ex, failure), "native error reaches UI handler: " + action);
            }
        }

        Console.WriteLine($"POWER_MENU_SMOKE_OK: {_checks} checks; fake native actions; no GUI shown or power/session changes.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        _checks++;
        Console.WriteLine("PASS: " + message);
    }

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Check(true, message); return; }
        throw new Exception("FAIL: " + message);
    }
}
