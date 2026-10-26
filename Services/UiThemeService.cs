using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using TaskFolder.Models;

namespace TaskFolder.Services
{
    internal sealed class UiPalette
    {
        internal Color Surface, Input, Text, Muted, Border, Hover, Pressed, Disabled, Accent;
    }
    internal static class UiThemeService
    {
        internal static event EventHandler Changed;
        private static string _theme = "Light";
        private static UiPalette _palette;
        static UiThemeService() { SystemEvents.UserPreferenceChanged += (_, _) => { _palette = null; Changed?.Invoke(null, EventArgs.Empty); }; }
        internal static void Configure(AppSettings settings)
        { bool changed = _theme != settings.Theme || LocalizationService.CurrentLanguage != settings.Language; _theme = settings.Theme; LocalizationService.CurrentLanguage = settings.Language; if (changed) { _palette = null; Changed?.Invoke(null, EventArgs.Empty); } }
        internal static UiPalette Resolve(string preference, bool systemDark, bool highContrast)
        {
            if (highContrast) return new UiPalette { Surface = SystemColors.Window, Input = SystemColors.Window, Text = SystemColors.WindowText, Muted = SystemColors.WindowText,
                Border = SystemColors.WindowText, Hover = SystemColors.Highlight, Pressed = SystemColors.Highlight, Disabled = SystemColors.GrayText, Accent = SystemColors.Highlight };
            bool dark = preference == "Dark" || preference == "System" && systemDark;
            return dark ? new UiPalette { Surface = Color.FromArgb(31,31,31), Input = Color.FromArgb(42,42,42), Text = Color.FromArgb(245,245,245), Muted = Color.FromArgb(193,193,193),
                Border = Color.FromArgb(100,100,100), Hover = Color.FromArgb(61,61,61), Pressed = Color.FromArgb(75,75,75), Disabled = Color.FromArgb(155,155,155), Accent = Color.FromArgb(100,185,255) }
                : new UiPalette { Surface = Color.FromArgb(250,250,250), Input = Color.FromArgb(243,243,243), Text = Color.FromArgb(31,31,31), Muted = Color.FromArgb(97,97,97),
                    Border = Color.FromArgb(202,202,202), Hover = Color.FromArgb(236,236,236), Pressed = Color.FromArgb(224,224,224), Disabled = Color.FromArgb(120,120,120), Accent = Color.FromArgb(0,103,192) };
        }
        internal static UiPalette Palette
        {
            get { if (_palette != null) return _palette; bool dark = false; if (_theme == "System") try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); dark = key?.GetValue("AppsUseLightTheme") is int value && value == 0; } catch (System.Security.SecurityException) { } catch (UnauthorizedAccessException) { }
                return _palette = Resolve(_theme, dark, SystemInformation.HighContrast); }
        }
        internal static void Apply(Control root)
        {
            var colors = Palette;
            void Visit(Control control)
            {
                bool input = control is TextBoxBase || control is ListControl || control is ListView || control is TreeView || control is NumericUpDown || control is DataGridView;
                control.BackColor = input ? colors.Input : colors.Surface; control.ForeColor = colors.Text;
                if (control is Label || control is ButtonBase || control is GroupBox || control is Form) control.Text = LocalizationService.Relocalize(control.Text);
                if (!string.IsNullOrEmpty(control.AccessibleName)) control.AccessibleName = LocalizationService.Relocalize(control.AccessibleName);
                if (control is TextBox editor) editor.PlaceholderText = LocalizationService.Relocalize(editor.PlaceholderText);
                if (control is LinkLabel link) { link.LinkColor = colors.Accent; link.ActiveLinkColor = colors.Accent; link.VisitedLinkColor = colors.Accent; }
                if (control is ListView list) foreach (ColumnHeader column in list.Columns) column.Text = LocalizationService.Relocalize(column.Text);
                if (control is DataGridView grid) { grid.BackgroundColor = colors.Surface; grid.DefaultCellStyle.BackColor = colors.Input; grid.DefaultCellStyle.ForeColor = colors.Text; grid.ColumnHeadersDefaultCellStyle.BackColor = colors.Surface; grid.ColumnHeadersDefaultCellStyle.ForeColor = colors.Text; foreach (DataGridViewColumn column in grid.Columns) column.HeaderText = LocalizationService.Relocalize(column.HeaderText); }
                if (control is ToolStrip strip) { foreach (ToolStripItem item in strip.Items) { if (item.Tag == null) item.Text = LocalizationService.Relocalize(item.Text); item.BackColor = colors.Surface; item.ForeColor = colors.Text; if (item is ToolStripMenuItem menu && menu.HasDropDownItems) Visit(menu.DropDown); } }
                foreach (Control child in control.Controls) Visit(child); control.Invalidate();
            }
            Visit(root);
        }
    }
}
