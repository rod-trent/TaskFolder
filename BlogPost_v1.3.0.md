# TaskFolder v1.3.0: A Season of Building

*October 2026*

---

When I shipped v1.1.0 back in April, I thought the app was mostly done. It had a search box, categories, profiles, a global hotkey, per-shortcut context menus, launch statistics. The list was already longer than I'd planned.

Then I used it every day. The list got longer again.

I shipped v1.2.0 in early October with a round of features I'd accumulated over the summer. Then I kept going — a few things I still wanted, a few bugs I couldn't leave alone, a few ideas that came from actually living with the app. By the time I stepped back, it was clearly v1.3.0 territory. This post covers what got built across both.

---

## v1.2.0: The Foundation Round

### Pinned Shortcuts

The most-requested thing after v1.1.0 was a way to keep certain shortcuts at the top of the list regardless of search or sort order. Right-click any shortcut → **Pin to Top**. Pinned items float above everything else, marked with 📌, and persist across restarts. Simple, but it changes how the app feels day-to-day.

### Run as Administrator

Some tools just need elevation. Right-click any shortcut and choose **Run as Administrator**, or middle-click the item directly. The process launches with a UAC prompt and the usual elevated token. No wrapper scripts needed.

### Pre-Launch Scripts

Attach a batch or PowerShell script to any shortcut via the right-click menu. The script runs before the app launches, with a 30-second timeout. Useful for things like mounting a drive, starting a VPN, or setting environment variables before an IDE comes up.

### Sticky Notes

**Add Note...** in the tray menu creates a `.note` file and opens it as an always-on-top, resizable sticky note editor — yellow background, save-on-close prompt. Notes live alongside your shortcuts in the active profile's folder.

### Profile Sync

A new Settings section lets you point TaskFolder's shortcuts folder at OneDrive, Dropbox, or any network path. The app doesn't do the syncing — it just stores its files wherever you tell it, and your cloud client handles the rest. Dropbox's local path is auto-detected from Dropbox's own `info.json`, so if Dropbox is installed, one click fills the field.

### Fuzzy Search

The search box switched from simple substring matching to fuzzy scoring. Type `viv` and you get `Vivaldi`. Type `cde` and you get `VS Code`. The scoring weights contiguous runs and prefix matches, so the right result usually floats to the top without having to type the full name.

---

## v1.3.0: Continuing the Streak

After shipping v1.2.0 I immediately saw things I wanted to keep going on. Some were unfinished edges of what I'd just built. Others were new ideas that came from having more features to build on.

### Dark Mode — Actually Dark

v1.2.0 shipped a dark mode renderer for the tray menu. It worked. The menu was dark. And then you opened Settings and got a blindingly white WinForms dialog.

v1.3.0 finishes the job. When dark mode is enabled, the Settings dialog applies a recursive styling pass over every control — `GroupBox`, `Label`, `CheckBox`, `RadioButton`, `TextBox`, `ComboBox`, `NumericUpDown`, `Button` — using the same palette as the tray menu (`#1E1E2E` background, `#CDD6F4` text, `#2D2D41` for input fields). Buttons get `FlatStyle.Flat` so their borders are visible against the dark surface.

The title bar goes dark via `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` — the same API Windows 11's own apps use to request a dark non-client area from the compositor. The scrollbar gets `SetWindowTheme("DarkMode_Explorer")`, which tells Windows to render the scroll thumb and track in dark style.

No half-measures.

### Mouse Button Hotkey

The global keyboard hotkey has been in since v1.1.0. What's new is the mouse button variant. In Settings, you can now assign Middle Click, Back, or Forward as an alternative trigger for the tray menu. Click "Set Button..." and press the button — a low-level mouse hook (`WH_MOUSE_LL` via `SetWindowsHookEx`) captures it globally, so it works from any app.

### Notes: Folders, Rich Text, and a Better Dialog

Notes picked up three things at once.

**Rich text.** The note editor is now a `RichTextBox` with a small toolbar for Bold, Italic, and Bullet. Notes are saved as RTF; existing plain-text notes load gracefully.

**Folder organization.** Notes now live in a dedicated `Shortcuts\Notes\` folder, separate from regular shortcuts. Subfolders inside `Notes\` are supported. A note in `Notes\Work\` appears under a "Work" sub-submenu inside "Notes" in the tray. All notes, regardless of subfolder, live under that single "Notes" root — no subfolder names bleeding up to the top-level menu.

**Better dialog.** The Add Note dialog now includes a Folder combo box, pre-populated with your existing subfolders. And it opens near the cursor instead of at the top-left corner of the screen.

### OneDrive Auto-Detection

v1.2.0 added one-click Dropbox detection. v1.3.0 adds the same for OneDrive. `OneDriveDetector` reads `HKCU\Software\Microsoft\OneDrive → UserFolder` from the registry, with a fallback to the `%OneDrive%` environment variable. Clicking "Use OneDrive" in Settings fills the sync path immediately if OneDrive is installed.

### Import / Export

Two new tray menu items: **Export Shortcuts...** and **Import Shortcuts...**. Export bundles the active profile into a `.zip` using `System.IO.Compression.ZipFile`. Import extracts it, confirms before overwriting, and reloads immediately. Good for migrating machines, sharing a shortcut set, or keeping a backup before doing something drastic.

### Drag-to-Reorder

v1.1.0 had Move Up / Move Down. v1.3.0 adds true drag reorder. Hold the mouse on any shortcut in the tray menu and drag it. There's a 6-pixel threshold before drag mode activates so normal clicks don't accidentally trigger a reorder. The sort order is written to `metadata.json` on drop.

### Enter-to-Launch

Type in the search box and press Enter. The top-matching shortcut launches. The menu closes as it normally would. One less reach for the mouse.

### Icon Size Toggle

Settings → Appearance now has a 16 px / 32 px icon size toggle. At 32 px, shortcuts with high-resolution icons look noticeably crisper on high-DPI displays.

### Update Checker

TaskFolder now checks the GitHub Releases API on startup (with a 3-second delay). If a newer release is available, it fires a notification. The check can be disabled in Settings → Notifications. It runs once per session via a self-disposing timer — no background polling.

### Test Suite

v1.3.0 ships with 27 xUnit tests across three components:

- **11 tests** for `FuzzySearch.Score()` — exact matches, case-insensitivity, non-matches, edge cases
- **9 tests** for `SettingsService` — defaults, round-trip serialization, profile names
- **7 tests** for `UpdateChecker` — version comparison logic via reflection on the private `IsNewer()` method

The COM-heavy parts (shortcut loading, icon extraction) need real `.lnk` files and a Windows desktop session to test, so they're left for integration testing.

---

## The Polish Fixes

A handful of things that were quietly wrong across both releases:

**App icon on dialogs.** WinForms forms don't inherit the application icon. All 7 dialogs now call `System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)` in their constructor.

**Hotkey menu on multi-monitor setups.** The tray menu now appears on whichever monitor the cursor is on, not always the primary.

**Hotkey menu DPI scaling.** Added `PerMonitorV2` DPI awareness so the menu renders correctly on monitors with mismatched DPI settings.

**Right-click submenu stability.** Opening the per-shortcut context menu no longer collapses the main tray menu.

**Horizontal scrollbar in Settings.** The scroll panel was 2 pixels too narrow. Once the vertical scrollbar claimed its ~17px, the GroupBoxes overflowed by a couple of pixels. Fixed by widening the form.

**"Cl" button.** The Clear button in Profile Sync was 30 px wide. Fixed to 44 px.

---

## Getting the Update

**Installer:** Download `TaskFolder-Setup-1.3.0.exe` from the [Releases page](https://github.com/rod-trent/TaskFolder/releases).

**Build from source:**

```bash
git clone https://github.com/rod-trent/TaskFolder.git
cd TaskFolder
dotnet publish TaskFolder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

**Requirements:** Windows 10 or 11, no separate .NET runtime needed (self-contained build).

Existing shortcuts, settings, and metadata carry over automatically. The one structural change: notes now live in `Shortcuts\Notes\`. If you have existing `.note` files in your shortcuts folder, move them there to bring them under the Notes submenu.

---

## What's Next

The things that keep coming up in my own usage:

- **Additional theme presets** — a warmer palette or an accent-color picker
- **Scheduled launches** — run a shortcut at a set time or on system wake

If you have ideas or run into issues, [GitHub Issues](https://github.com/rod-trent/TaskFolder/issues) is the right place.

---

*TaskFolder is open source under the MIT License.*

*[@rod-trent](https://github.com/rod-trent) — [GitHub](https://github.com/rod-trent/TaskFolder)*
