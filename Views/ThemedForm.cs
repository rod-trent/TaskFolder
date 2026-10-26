using System;
using System.Windows.Forms;
using TaskFolder.Services;

namespace TaskFolder.Views
{
    public class ThemedForm : Form
    {
        public ThemedForm() { UiThemeService.Changed += ThemeChanged; AutoScaleMode = AutoScaleMode.Dpi; }
        protected override void OnShown(EventArgs args) { UiThemeService.Apply(this); base.OnShown(args); }
        private void ThemeChanged(object sender, EventArgs args)
        { if (IsDisposed || !IsHandleCreated) return; if (InvokeRequired) BeginInvoke(new Action(() => { if (!IsDisposed) UiThemeService.Apply(this); })); else UiThemeService.Apply(this); }
        protected override void Dispose(bool disposing) { if (disposing) UiThemeService.Changed -= ThemeChanged; base.Dispose(disposing); }
    }
}
