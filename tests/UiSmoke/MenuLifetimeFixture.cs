using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using TaskFolder;
using TaskFolder.Services;
using TaskFolder.Utilities;
using TaskFolder.Views;

internal static class MenuLifetimeFixture
{
    internal static void Run(string root, SettingsService settings, ShortcutManager manager, HotkeyService hotkey, bool automatic, bool widgetOwner)
    {
        string trace = Path.Combine(root, "menu-events.txt");
        using var form = new Form { Text = "TaskFolder menu lifetime QA", ClientSize = new Size(680, 360), StartPosition = FormStartPosition.CenterScreen };
        using var menu = new Windows11ContextMenuStrip(new Windows11MenuRenderer(), Windows11MenuStyle.CreateFont(10));
        using var owner = new WidgetMenuOwnerForm();
        using var steps = new Timer { Interval = 300 };
        var status = new Label { Dock = DockStyle.Bottom, Height = 100, Text = "Ready: fixture only; saved user data is not loaded." };
        int settingsShown = 0;
        void ObserveDialog(SettingsForm dialog)
        {
            dialog.Shown += (_, _) =>
            {
                settingsShown++;
                if (!dialog.Visible) throw new Exception("Settings Shown did not produce a visible dialog.");
                if (automatic)
                {
                    var close = new Timer { Interval = 150 };
                    close.Tick += (_, _) => { close.Dispose(); dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };
                    close.Start();
                }
            };
        }
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown };
        form.Controls.Add(panel); form.Controls.Add(status);
        void Trace(string text) { status.Text = text; File.AppendAllText(trace, DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine); }
        Type itemType = typeof(TaskFolderApplicationContext).GetNestedType("SizedToolStripMenuItem", BindingFlags.NonPublic);
        ToolStripMenuItem Item(string name, EventHandler action = null) => (ToolStripMenuItem)Activator.CreateInstance(itemType,
            BindingFlags.Instance | BindingFlags.Public, null, new object[] { name, null, action, 36, new Windows11MenuRenderer(), 10f }, null);
        var search = new Windows11SearchBox(menu.Font) { Size = new Size(280, 40) };
        menu.Items.Add(new ToolStripControlHost(search) { AutoSize = false, Size = search.Size });
        foreach (string name in new[] { "Folder Alpha", "Folder Beta", "Folder Gamma" })
        {
            var folder = Item(name);
            folder.DropDownItems.Add(Item("Synthetic child"));
            folder.DropDown.Opened += (_, _) => Trace(name + " opened; root visible=" + menu.Visible);
            folder.DropDown.Closed += (_, args) => Trace(name + " closed: " + args.CloseReason + "; root visible=" + menu.Visible);
            menu.Items.Add(folder);
        }
        menu.Items.Add(Item("Settings...", (_, _) =>
        {
            MenuMotion.OpenDialogAfterMenu(menu, () =>
            {
                Trace("Settings callback entered");
                using var dialog = new SettingsForm(manager, settings, hotkey);
                ObserveDialog(dialog);
                dialog.Shown += (_, _) => Trace("Settings shown");
                dialog.ShowDialog();
                Trace("Settings closed");
                if (automatic) steps.Start();
            });
        }));
        menu.Opened += (_, _) => { Trace("Root opened"); menu.BeginInvoke(new Action(search.SelectAllAndFocus)); };
        menu.Closing += (_, args) => Trace("Root closing: " + args.CloseReason + "; cancelled=" + args.Cancel);
        menu.Closed += (_, args) => { if (widgetOwner) owner.Hide(); Trace("Root closed: " + args.CloseReason); };
        var open = new Button { Text = "Open TaskFolder test menu", AutoSize = true, Height = 42 };
        open.Click += (_, _) =>
        {
            if (!widgetOwner) { menu.Show(open, new Point(0, open.Height)); return; }
            owner.Location = open.PointToScreen(new Point(0, open.Height));
            owner.Show();
            Trace("Widget owner foreground accepted=" + SetForegroundWindow(owner.Handle));
            menu.Show(owner, Point.Empty);
        };
        panel.Controls.Add(open);
        var direct = new Button { Text = "Open Settings directly", AutoSize = true, Height = 42 };
        direct.Click += (_, _) => { using var dialog = new SettingsForm(manager, settings, hotkey); ObserveDialog(dialog); dialog.ShowDialog(form); Trace("Direct Settings closed"); };
        panel.Controls.Add(direct);
        Console.WriteLine("MENU_LIFETIME_FIXTURE: " + root);
        Exception failure = null;
        int phase = 0;
        if (automatic)
        {
            steps.Tick += (_, _) =>
            {
                try
                {
                    switch (phase++)
                    {
                        case 0: open.PerformClick(); break;
                        case 1:
                            if (!menu.Visible) throw new Exception("Root disappeared after the opening fade.");
                            menu.Close(ToolStripDropDownCloseReason.AppFocusChange);
                            if (!menu.Visible) throw new Exception("Unchanged foreground incorrectly dismissed the root menu.");
                            break;
                        case 2: ((ToolStripMenuItem)menu.Items[1]).ShowDropDown(); break;
                        case 3:
                            ((ToolStripMenuItem)menu.Items[1]).HideDropDown();
                            ((ToolStripMenuItem)menu.Items[2]).ShowDropDown(); break;
                        case 4:
                            ((ToolStripMenuItem)menu.Items[2]).HideDropDown();
                            ((ToolStripMenuItem)menu.Items[3]).ShowDropDown(); break;
                        case 5:
                            if (!menu.Visible) throw new Exception("Root disappeared while switching submenus.");
                            steps.Stop(); ((ToolStripMenuItem)menu.Items[4]).PerformClick();
                            break;
                        case 6:
                            if (settingsShown != 1) throw new Exception("Settings menu item did not show its dialog.");
                            open.PerformClick(); break;
                        case 7: if (!menu.Visible) throw new Exception("Root could not be reopened."); menu.Close(ToolStripDropDownCloseReason.Keyboard); break;
                        case 8: steps.Stop(); direct.PerformClick(); steps.Start(); break;
                        case 9:
                            if (settingsShown != 2) throw new Exception("Direct Settings entry did not show its dialog.");
                            open.PerformClick(); break;
                        case 10:
                            menu.Close(ToolStripDropDownCloseReason.AppClicked);
                            if (menu.Visible) throw new Exception("Outside-click close reason was incorrectly cancelled.");
                            break;
                        case 11:
                            Trace("PASS native menu lifecycle: stable opening, unchanged-foreground close guard, three submenu switches, Settings item, reopen, keyboard/outside dismissal reasons and direct Settings.");
                            Console.WriteLine(status.Text); form.Close(); break;
                    }
                }
                catch (Exception error) { failure = error; Trace("FAIL: " + error.Message); steps.Stop(); form.Close(); }
            };
            form.Shown += (_, _) => steps.Start();
        }
        Application.Run(form);
        MenuFadeWindow.CancelAll();
        if (failure != null) throw failure;
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
