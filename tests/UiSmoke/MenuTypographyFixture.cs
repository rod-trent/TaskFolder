using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using TaskFolder;
using TaskFolder.Utilities;

internal static class MenuTypographyFixture
{
    internal static void Run()
    {
        int checks = 0;
        void Assert(bool condition, string reason) { if (!condition) throw new Exception(reason); checks++; }
        Type itemType = typeof(TaskFolderApplicationContext).GetNestedType("SizedToolStripMenuItem", BindingFlags.NonPublic);
        var rescale = typeof(ToolStrip).GetMethod("RescaleConstantsForDpi", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (float configuredSize in new[] { 9f, 12f, 18f })
        {
            var renderer = new Windows11MenuRenderer();
            using var root = new Windows11ContextMenuStrip(renderer, Windows11MenuStyle.CreateFont(configuredSize));
            var replacementFonts = new List<Font>();
            ToolStripMenuItem Item() => (ToolStripMenuItem)Activator.CreateInstance(itemType,
                BindingFlags.Instance | BindingFlags.Public, null,
                new object[] { "Music / Музыка 작업", null, null, 84, renderer, configuredSize }, null);
            try
            {
                var folder = Item(); root.Items.Add(folder);
                var nested = Item(); folder.DropDownItems.Add(nested);
                var leaf = Item(); nested.DropDownItems.Add(leaf);
                _ = root.Handle; _ = folder.DropDown.Handle; _ = nested.DropDown.Handle;
                int previousDpi = 96;
                foreach (int dpi in new[] { 144, 192, 96, 192 })
                {
                    // Exercise WinForms' DPI callback with a changed effective root
                    // font, without moving a window or changing any display setting.
                    var effective = Windows11MenuStyle.CreateFont(configuredSize * dpi / 96f);
                    replacementFonts.Add(effective);
                    root.Font = effective;
                    rescale.Invoke(root, new object[] { previousDpi, dpi });
                    previousDpi = dpi;
                    foreach (var item in new[] { folder, nested, leaf })
                    {
                        Assert(item.Font.Equals(root.Font),
                            $"Menu typography differs after DPI callback {dpi}: root={root.Font.SizeInPoints}, item={item.Font.SizeInPoints}, configured={configuredSize}");
                        Assert(item.GetPreferredSize(Size.Empty).Height == folder.GetPreferredSize(Size.Empty).Height,
                            "Nested menu rows differ from root row height.");
                        Assert(TextRenderer.MeasureText(item.Text, item.Font) == TextRenderer.MeasureText(folder.Text, folder.Font),
                            "Identical nested labels use a different text measurement.");
                    }
                    Assert(folder.DropDown.Font.Equals(root.Font) && nested.DropDown.Font.Equals(root.Font),
                        "Dropdown containers do not inherit the effective root font.");
                    // Lazy-created branches must inherit the current font too.
                    var lazy = Item(); root.Items.Add(lazy);
                    var lazyLeaf = Item(); lazy.DropDownItems.Add(lazyLeaf);
                    Assert(lazyLeaf.Font.Equals(root.Font), "A submenu created after a DPI change used the saved unscaled size.");
                    root.Items.Remove(lazy); lazy.Dispose();
                    Assert(!root.Visible && !folder.DropDown.Visible && !nested.DropDown.Visible,
                        "Typography regression checks displayed a menu.");
                }
            }
            finally
            {
                root.Dispose();
                foreach (var font in replacementFonts) font.Dispose();
            }
        }
        Console.WriteLine($"PASS menu typography: {checks} checks; configured 9/12/18 pt; DPI callbacks 144/192/96/192; root, two nested levels and lazy submenus. No visible windows or display changes.");
    }
}
