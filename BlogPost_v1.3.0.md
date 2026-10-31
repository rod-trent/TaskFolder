# TaskFolder v1.3.0: Dark Mode, Notes, Sync, and a Lot More

*October 2026*

---

When I shipped v1.2.0 a few weeks ago, I thought I was mostly done. The app had everything I personally needed: fuzzy search, categories, profiles, sticky notes, pinned shortcuts, profile sync. The list was long.

Then I used it every day for a while. And the list got longer again.

v1.3.0 is the result. It's got 14 new features, a proper test suite, and a pile of polish fixes that have been bothering me since the first screenshot someone sent me of the Settings dialog looking like it was designed in 2003.

Let me walk through what changed and why.

---

## Dark Mode — Actually Dark

v1.2.0 shipped a dark mode renderer for the tray menu. It worked. The menu was dark. And then you opened Settings and got a blindingly white WinForms dialog, because WinForms doesn't have a system-wide dark mode toggle — every control has to be styled manually.

v1.3.0 finishes the job.

When dark mode is enabled, the Settings dialog now applies a recursive styling pass over every control — `GroupBox`, `Label`, `CheckBox`, `RadioButton`, `TextBox`, `ComboBox`, `NumericUpDown`, `Button` — using the same color palette as the tray menu renderer (`#1E1E2E` background, `#CDD6F4` text, `#2D2D41` for input fields). Buttons get `FlatStyle.Flat` so their borders are visible against the dark surface.

The title bar goes dark too, via `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` — the same API Windows 11's own apps use to request a dark non-client area from the compositor.

The scrollbar gets `SetWindowTheme("DarkMode_Explorer")`, which tells Windows to render the scroll thumb and track in dark style.

The end result is a Settings dialog that looks like it belongs in a dark-mode environment, from title bar to scrollbar. No half-measures.

---

## Mouse Button Hotkey

The global keyboard hotkey (Ctrl+Alt+T by default) has been in since v1.1.0. What's new is the mouse button variant.

In Settings, you can now assign Middle Click, Back, or Forward mouse buttons as an alternative trigger for the tray menu. Click "Set Button..." and then press the button — a low-level mouse hook (`WH_MOUSE_LL` via `SetWindowsHookEx`) captures it globally, so it works from anywhere, even when another app has focus.

Why mouse buttons? Because if you're already using the keyboard hotkey for something else, and you have a multi-button mouse (which most people do), you've got a free shortcut sitting right there on your thumb.

---

## Notes: Folders, Rich Text, and a Better Dialog

Sticky Notes picked up three significant improvements.

**Rich text.** The note editor is now a `RichTextBox` instead of a plain `TextBox`. There's a small toolbar at the top with Bold, Italic, and Bullet buttons. Notes are saved as RTF; existing plain-text notes load gracefully (the code checks for `{\rtf` at the start of the file).

**Folder organization.** Notes now live in a dedicated `Shortcuts\Notes\` folder — separate from your regular shortcuts. Subfolders inside `Notes\` are supported; a note saved to `Notes\Work\` appears under a "Work" sub-submenu inside the "Notes" submenu in the tray.

The key design decision here: all notes, regardless of subfolder, share a single "Notes" root in the tray menu. There's no "Work" category at the top level alongside your apps. Everything note-related is under Notes. Subfolders nest one level deeper within that root. It keeps the tray menu organized and makes the hierarchy self-explanatory.

**Better Add Note dialog.** The Add Note dialog previously asked only for a note title. Now it also has a Folder combo box — existing subfolders are listed as options, and you can type a new folder name directly. The dialog opens near the tray menu (at the cursor position when you clicked "Add Note..."), clamped to the screen's working area so it never goes off-screen.

---

## OneDrive Auto-Detection

v1.2.0 introduced a "Use Dropbox" button that auto-detected the local Dropbox root from Dropbox's `info.json`. v1.3.0 adds the same thing for OneDrive.

`OneDriveDetector` reads `HKCU\Software\Microsoft\OneDrive → UserFolder` from the registry — the key the OneDrive client writes when it sets up sync. If the registry read fails, it falls back to the `%OneDrive%` environment variable, which the OneDrive client also sets. Either way, clicking "Use OneDrive" fills the sync path in one click.

The sync model itself hasn't changed: TaskFolder doesn't manage the file transfer. It just stores shortcuts in whatever folder you point it at. OneDrive and Dropbox handle the actual syncing. This is deliberately simple — it means you don't depend on TaskFolder being online, and there's no conflict resolution to implement.

---

## Import / Export

You can now back up and restore your shortcuts with two new tray menu items: **Export Shortcuts...** and **Import Shortcuts...**.

Export bundles the active profile's shortcuts folder into a `.zip` archive using `System.IO.Compression.ZipFile`. Import extracts a previously exported archive, asks for confirmation before overwriting anything, and reloads the menu immediately. Both operations handle errors with a user-facing dialog rather than silently failing.

Practical uses: migrating to a new machine before setting up sync, sharing a shortcut set with a colleague, or just keeping a backup before doing something destructive.

---

## Drag-to-Reorder

v1.1.0 added Move Up / Move Down for reordering shortcuts. It worked, but it wasn't satisfying for large lists.

v1.3.0 adds true drag reorder. Hold the mouse button on any shortcut in the tray menu and drag it up or down. There's a 6-pixel Y-delta threshold before drag mode activates, so normal clicks don't accidentally trigger a reorder. The sort order is persisted to `metadata.json` immediately after dropping.

The implementation uses `ToolStrip.GetItemAt(Point)` for hit testing on mouse move events. The tray menu is a `ContextMenuStrip` (which inherits from `ToolStrip`), so this works without subclassing.

---

## Enter-to-Launch

This one is simple and useful. Type in the search box and press Enter — the top-matching shortcut launches immediately. No need to reach for the mouse after you've already started typing.

The implementation adds a `KeyDown` handler to the `ToolStripTextBox`: if the key is `Enter` and there's at least one shortcut visible in the filtered list, it clicks the first one. The menu closes as a normal consequence of the click.

---

## Icon Size Toggle

In Settings → Appearance, you can now choose between 16 px (the previous fixed size, now the default) and 32 px icons in the tray menu.

The icon size applies to `menu.ImageScalingSize` and to the bitmap scaling in `BuildShortcutItem()`. At 32 px, shortcuts with large icons look noticeably crisper — useful if you're running a high-DPI display and the 16 px icons feel small.

---

## Update Checker

TaskFolder now checks for newer releases on startup (with a 3-second delay so it doesn't slow perceived startup time). It calls the GitHub Releases API, compares the latest tag against the current version using `Version.TryParse`, and fires a notification if something newer is available.

The check can be disabled in Settings → Notifications ("Check GitHub for a newer release on startup"). It uses a `System.Threading.Timer` that fires once and disposes itself — no background polling, no persistent HTTP connections.

---

## Test Suite

v1.3.0 ships with a `TaskFolder.Tests` project containing 27 xUnit tests:

- **11 tests** for `FuzzySearch.Score()` — covers exact matches, case-insensitivity, non-matches, empty inputs, long strings
- **9 tests** for `SettingsService` — covers default values, round-trip serialization, profile name persistence
- **7 tests** for `UpdateChecker` — covers the private `IsNewer()` method via reflection: higher minor/patch/major, same version, older version, pre-release suffixes, malformed tags

These aren't exhaustive, but they cover the three most logic-heavy components in the codebase. The COM-heavy parts (shortcut loading, icon extraction) are harder to test without real `.lnk` files and a Windows desktop session, so they're left for integration testing.

---

## The Polish Fixes

A few things that were quietly wrong:

**App icon on dialogs.** WinForms forms don't inherit the application icon automatically — each one needs its `Icon` property set explicitly. All 7 forms now call `System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath)` in their constructor, so every window shows the TaskFolder icon in its title bar instead of the generic WinForms default.

**Horizontal scrollbar in Settings.** The Settings scroll panel was 2 pixels too narrow. Once the vertical scrollbar claimed its ~17px, the 440px GroupBoxes overflowed by a couple of pixels and triggered a horizontal scrollbar. Fixed by widening the form by 20px.

**"Cl" button.** The "Clear" button in the Profile Sync section was 30px wide. "Clear" needs at least 44px to render without truncation. Fixed.

**Add Note dialog position.** The Add Note dialog was opening at the top-left corner of the screen (WinForms `CenterScreen` default when `StartPosition` isn't overridden correctly). It now opens at the cursor position, clamped to the screen's working area.

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

Your existing shortcuts, settings, and metadata migrate automatically. The only structural change is that the `Shortcuts\Notes\` folder is new — if you have existing notes, move their `.note` files there to bring them under the Notes submenu.

---

## What's Next

The roadmap items that keep coming up in my own usage:

- **Additional theme presets** — dark is done; a warmer palette or an accent-color picker could be useful
- **Scheduled launches** — run a shortcut at a set time or on system wake

If you have ideas or run into issues, [GitHub Issues](https://github.com/rod-trent/TaskFolder/issues) is the right place.

---

*TaskFolder is open source under the MIT License.*

*[@rod-trent](https://github.com/rod-trent) — [GitHub](https://github.com/rod-trent/TaskFolder)*
