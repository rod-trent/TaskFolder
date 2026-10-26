using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using TaskFolder.Services;
using TaskFolder.Views;
using TaskFolder.Utilities;

internal static class WidgetControlsFixture
{
    internal static void Check(SettingsForm form, SettingsService settings, string language, string theme)
    {
        T Field<T>(string name) => (T)typeof(SettingsForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
        void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
        var enabled = Field<CheckBox>("_chkTaskbarButton");
        var hover = Field<CheckBox>("_chkTaskbarHover");
        var size = Field<NumericUpDown>("_widgetSize");
        var delay = Field<NumericUpDown>("_menuHideDelay");
        int previousSize = settings.Settings.WidgetSizePercent, previousDelay = settings.Settings.WidgetMenuHideDelayMs;
        Assert(size.Value == previousSize && delay.Value == previousDelay, "Widget controls did not load saved preferences.");
        enabled.Checked = false;
        Assert(!size.Enabled && !delay.Enabled && !hover.Enabled, "Disabled widget left active controls.");
        enabled.Checked = true; hover.Checked = false;
        Assert(size.Enabled && !delay.Enabled, "Widget size and hover-only hide delay have incorrect enabled states.");
        hover.Checked = true;
        Assert(delay.Enabled, "Hide delay did not enable with hover.");
        size.Value = previousSize == 180 ? 70 : 180;
        delay.Value = previousDelay == 1100 ? 400 : 1100;
        Assert(settings.Settings.WidgetSizePercent == previousSize && settings.Settings.WidgetMenuHideDelayMs == previousDelay,
            "Editing widget controls changed saved preferences before OK.");
        // Keep the real native ancestor hidden while WinForms lays out and paints
        // the settings child normally. No top-level settings window is shown.
        using var hiddenRoot = new Form { ShowInTaskbar = false };
        form.TopLevel = false;
        SetParent(form.Handle, hiddenRoot.Handle);
        Assert(TaskbarNative.GetParent(form.Handle) == hiddenRoot.Handle, "Settings fixture could not attach to its hidden root.");
        form.Show(); form.PerformLayout();
        Assert(!TaskbarNative.IsWindowVisible(form.Handle), "Settings fixture became visible onscreen.");
        var section = (GroupBox)size.Parent.Parent.Parent;
        using var bitmap = new Bitmap(section.Width, section.Height);
        section.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        string directory = Path.Combine(Path.GetTempPath(), "taskfolder-widget-controls-20261026");
        Directory.CreateDirectory(directory);
        bitmap.Save(Path.Combine(directory, "widget-settings-" + (language == "Русский" ? "ru" : "en") + "-" + theme + ".png"));
        int expectedSize = (int)size.Value, expectedDelay = (int)delay.Value;
        typeof(SettingsForm).GetMethod("BtnOK_Click", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(form, new object[] { null, EventArgs.Empty });
        var reloaded = new SettingsService(settings.DataRoot);
        Assert(reloaded.Settings.WidgetSizePercent == expectedSize && reloaded.Settings.WidgetMenuHideDelayMs == expectedDelay,
            "OK did not persist widget size and hide delay.");
        Assert(!form.Visible, "Widget settings fixture displayed a GUI window.");
        // Restore fixture-only visibility preference for other constructor checks.
        settings.Settings.TaskbarButtonEnabled = false;
        settings.Save();
        Console.WriteLine("PASS hidden widget settings controls/load/enable/edit/save/reload: " + language + " / " + theme);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr window, IntPtr parent);
}
