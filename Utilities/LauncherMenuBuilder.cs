using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using TaskFolder.Models;
using TaskFolder.Services;

namespace TaskFolder.Utilities
{
    internal static class LauncherMenuBuilder
    {
        internal static IReadOnlyList<ToolStripItem> BuildFavorites(LauncherMenuSnapshot snapshot,
            Func<ShortcutItem, ToolStripMenuItem> buildShortcut, Func<string, ToolStripMenuItem> createItem, string language)
        {
            var rows = new List<ToolStripItem>();
            if (snapshot.Pins.Count == 0) return rows;
            var title = createItem(LocalizationService.Get("Menu_Favorites", language));
            title.Enabled = false;
            title.Tag = "category";
            rows.Add(title);
            foreach (var pin in snapshot.DirectPins) rows.Add(buildShortcut(pin));
            if (snapshot.Pins.Count > LauncherMenuSnapshot.DirectPinLimit)
            {
                var all = createItem(LocalizationService.Get("Menu_AllFavorites", language));
                all.Tag = "category";
                foreach (var pin in snapshot.Pins) all.DropDownItems.Add(buildShortcut(pin));
                rows.Add(all);
            }
            rows.Add(new ToolStripSeparator { Tag = "shortcuts-sep" });
            return rows;
        }

        internal static bool IsContextKey(Keys keys) => keys == Keys.Apps || keys == (Keys.Shift | Keys.F10);
        internal static ShortcutItem ResolveActivation(LauncherMenuSnapshot snapshot, ShortcutItem source) => snapshot.IsV2
            ? snapshot.AllShortcuts.SingleOrDefault(item => item.Id == source.Id && item.ProfileId == source.ProfileId)
            : snapshot.AllShortcuts.FirstOrDefault(item => string.Equals(item.ShortcutFilePath, source.ShortcutFilePath, StringComparison.OrdinalIgnoreCase));
        internal static string ProfileText(LauncherMenuSnapshot snapshot, string language) =>
            string.Format(CultureInfo.CurrentCulture, LocalizationService.Get("Menu_Profile", language),
                (snapshot.ProfileName ?? "Default").Replace("&", "&&"));
    }
}
