using System;
using System.IO;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;
using TaskFolder.Views;
using TaskFolder.Utilities;

internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles();
        if (args.Length == 1 && args[0] == "menu-typography") { MenuTypographyFixture.Run(); return; }
        string root = Path.Combine(Path.GetTempPath(), "TaskFolder-usability-qa", "completion-20261026", "ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"Shortcuts","Power","Empty")); Directory.CreateDirectory(Path.Combine(root,"Profiles","Work"));
        foreach (string name in new[] { "Alpha", "Beta", "Редактор 작업" }) File.WriteAllText(Path.Combine(root,"Shortcuts",name+".url"),"[InternetShortcut]\nURL=https://example.invalid/"+Uri.EscapeDataString(name)+"\n");
        File.WriteAllText(Path.Combine(root,"Shortcuts","Power","Nested.url"),"[InternetShortcut]\nURL=https://example.invalid/nested\n");
        var migration = new StateMigrationService(root); if (!migration.Commit(migration.Preview()).Success) throw new Exception("Fixture setup failed.");
        var settings = new SettingsService(root); settings.Settings.Language="Русский"; settings.Settings.Theme="Dark"; settings.Settings.TaskbarButtonEnabled=false; settings.Save(); UiThemeService.Configure(settings.Settings);
        settings.SettingsChanged += (_,_) => UiThemeService.Configure(settings.Settings);
        using var manager = new ShortcutManager(settings,path=>new ShortcutItem { Name=Path.GetFileNameWithoutExtension(path), ShortcutFilePath=path, TargetPath="https://example.invalid/fixture" },(_,_)=>null,true);
        manager.ShortcutsChanged += (_,_) => manager.ReloadShortcuts();
        using var hotkey = new HotkeyService();
        if (args.Length == 1 && args[0].StartsWith("menu-lifetime", StringComparison.Ordinal))
        {
            MenuLifetimeFixture.Run(root, settings, manager, hotkey, args[0].EndsWith("-auto", StringComparison.Ordinal), args[0].Contains("widget", StringComparison.Ordinal));
            return;
        }
        string archive=Path.Combine(root,"fixture.zip"); new BackupService(settings).Export(archive);
        if (args.Length == 1 && args[0] == "constructors")
        {
            foreach (string language in new[] { "English", "Русский" }) foreach (string theme in new[] { "Light", "Dark" })
            {
                settings.Settings.Language=language; settings.Settings.Theme=theme; settings.Save(); UiThemeService.Configure(settings.Settings);
                using var organizerCheck = new ShortcutManagerForm(settings,manager);
                using var addCheck = new AddItemDialog(settings);
                using var archiveCheck = new ImportPreviewForm(settings,archive);
                using var recoveryCheck = new RecoveryForm(settings,manager);
                using var settingsCheck = new SettingsForm(manager,settings,hotkey);
                WidgetControlsFixture.Check(settingsCheck, settings, language, theme);
                using var renameCheck = new RenameDialog("Synthetic name");
                using var iconCheck = new IconPickerDialog();
                using var repairCheck = new RepairTargetDialog(new ShortcutItem { Name="Synthetic",ShortcutFilePath="fixture.url",TargetPath="https://example.invalid/fixture" });
                Console.WriteLine("PASS native form construction: "+language+" / "+theme);
            }
            return;
        }
        using var launcher = new ThemedForm { Text="TaskFolder isolated QA · fixture only", Width=620, Height=420, StartPosition=FormStartPosition.CenterScreen };
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill, Padding=new Padding(20), FlowDirection=FlowDirection.TopDown, WrapContents=false, AutoScroll=true}; launcher.Controls.Add(buttons);
        ShortcutManagerForm organizer=null;
        void Button(string text,Action action) { var button=new Button { Text=text,AutoSize=true,MinimumSize=new System.Drawing.Size(340,32) }; button.Click+=(_,_)=>action(); buttons.Controls.Add(button); }
        Button("Open manager",()=>{if(organizer?.IsDisposed!=false){organizer=new ShortcutManagerForm(settings,manager);organizer.Show(launcher);}else organizer.Activate();});
        Button("Open Add dialog",()=>{using var form=new AddItemDialog(settings);form.ShowDialog(launcher);});
        Button("Open Settings",()=>{using var form=new SettingsForm(manager,settings,hotkey);form.ShowDialog(launcher);});
        Button("Open Recovery",()=>{using var form=new RecoveryForm(settings,manager);form.ShowDialog(launcher);});
        Button("Open archive preview",()=>{using var form=new ImportPreviewForm(settings,archive);form.ShowDialog(launcher);});
        Button("Toggle light / dark",()=>{settings.Settings.Theme=settings.Settings.Theme=="Dark"?"Light":"Dark";settings.Save();});
        Button("Toggle RU / EN",()=>{settings.Settings.Language=settings.Settings.Language=="Русский"?"English":"Русский";settings.Save();});
        Button("External fixture refresh",()=>{File.WriteAllText(Path.Combine(root,"Profiles","Work","Outside.url"),"[InternetShortcut]\nURL=https://example.invalid/outside\n");});
        launcher.FormClosed+=(_,_)=>organizer?.Dispose();
        Console.WriteLine("UI fixture root: "+root); Console.WriteLine("Only fixture data is writable. No actual launch callback or production application context is created.");
        Application.Run(launcher);
    }
}
