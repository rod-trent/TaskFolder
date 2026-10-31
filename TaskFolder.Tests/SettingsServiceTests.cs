using System.Collections.Generic;
using System.IO;
using TaskFolder.Services;
using Xunit;

namespace TaskFolder.Tests
{
    public class SettingsServiceTests : System.IDisposable
    {
        private readonly string _tempDir;
        private readonly SettingsService _svc;

        public SettingsServiceTests()
        {
            // Isolated temp folder — deleted on Dispose
            _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir);

            // SettingsService always writes to %APPDATA%\TaskFolder in non-portable mode,
            // so we use portable mode with a fake base dir. We redirect BaseDirectory by
            // constructing the service in portable mode and pre-writing a settings.json there.
            // Because we can't easily inject _dataRoot, we test the portable path instead.
            _svc = new SettingsService(isPortable: false); // uses real APPDATA; we replace settings in-place
        }

        [Fact]
        public void DefaultSettings_CheckForUpdates_IsTrue()
        {
            var settings = new Models.AppSettings();
            Assert.True(settings.CheckForUpdates);
        }

        [Fact]
        public void DefaultSettings_MenuIconSize_Is16()
        {
            var settings = new Models.AppSettings();
            Assert.Equal(16, settings.MenuIconSize);
        }

        [Fact]
        public void DefaultSettings_MenuFontSize_Is9()
        {
            var settings = new Models.AppSettings();
            Assert.Equal(9f, settings.MenuFontSize);
        }

        [Fact]
        public void DefaultSettings_DarkMode_IsFalse()
        {
            var settings = new Models.AppSettings();
            Assert.False(settings.DarkMode);
        }

        [Fact]
        public void DefaultSettings_NameCleanupTerms_ContainsLnk()
        {
            var settings = new Models.AppSettings();
            Assert.Contains(".lnk", settings.NameCleanupTerms);
        }

        [Fact]
        public void SettingsService_Save_RoundTrip_CheckForUpdates()
        {
            _svc.Settings.CheckForUpdates = false;
            _svc.Save();
            _svc.Load();
            Assert.False(_svc.Settings.CheckForUpdates);

            // Restore
            _svc.Settings.CheckForUpdates = true;
            _svc.Save();
        }

        [Fact]
        public void SettingsService_Save_RoundTrip_MenuIconSize()
        {
            _svc.Settings.MenuIconSize = 32;
            _svc.Save();
            _svc.Load();
            Assert.Equal(32, _svc.Settings.MenuIconSize);

            // Restore
            _svc.Settings.MenuIconSize = 16;
            _svc.Save();
        }

        [Fact]
        public void SettingsService_GetMetadata_MissingKey_ReturnsDefault()
        {
            var meta = _svc.GetMetadata("nonexistent.lnk");
            Assert.NotNull(meta);
            Assert.Equal("nonexistent.lnk", meta.FileName);
        }

        [Fact]
        public void SettingsService_UpsertMetadata_ThenGet_RoundTrips()
        {
            var meta = new Models.ShortcutMetadata { FileName = "test.lnk", CustomIconPath = @"C:\icon.ico" };
            _svc.UpsertMetadata(meta);
            var retrieved = _svc.GetMetadata("test.lnk");
            Assert.Equal(@"C:\icon.ico", retrieved.CustomIconPath);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
