# Floating TaskFolder widget prototype

**Superseded:** the active desktop implementation is documented in
[desktop-widget.md](desktop-widget.md). The floating-window experiment
below remains as historical evidence and is not the current launch architecture.

The native taskbar-button experiment is replaced in the active application path
by `TaskbarWidgetForm`: an independent topmost tool window. It uses TaskFolder's
existing icon and Windows11MenuStyle colors, border, hover, and pressed states.
Its logical size is 52 × 44 px; drawing, hit area, and rounded region scale together
with DPI. A six-dot grip indicates dragging. No new dependencies are required.

The widget has `ShowInTaskbar=false`, explicitly sets `WS_EX_TOOLWINDOW`, and
removes `WS_EX_APPWINDOW`. It does not register a native taskbar button and does
not start the previous UI Automation detector. No thumbnail, Explorer, registry,
or security settings are modified. Historical prototype files remain in source
but are not instantiated by `Program.cs`.

## Run

1. Exit the running TaskFolder through **Exit TaskFolder** in its menu. The existing
   single-instance guard prevents the new prototype from running beside an older copy.
2. Run `artifacts/widget-prototype/Start-Prototype.cmd`.
3. The widget initially appears above the bottom-right corner of the primary
   monitor's work area. Hold the left mouse button and drag it to a free area of
   the taskbar or anywhere else on the desktop. Release to save the location.
4. Click to open the existing menu, or hover for 300 ms. A drag does not launch the
   menu; leaving before the delay cancels hover. Returning from the menu or closing
   it while still over the widget does not create an immediate reopening loop.
5. Right-click the widget to open Settings. Under **Floating widget**, toggle
   **Open menu on hover after 300 ms** and save with **OK**. Click remains available
   with hover disabled. **Show floating TaskFolder widget** can hide the widget;
   use the tray menu to reenable it. Cancel leaves the preferences unchanged.
6. Exit the prototype through its menu, then relaunch the same launcher to check
   the saved location. Exit it and launch the installed app to return to that copy.

The launcher uses `--portable`; settings and shortcuts live beside this prototype
and do not overwrite the installed app's `%APPDATA%/TaskFolder` data. Earlier
prototype folders also retain their own data. Keep this prototype folder in one
place if you want its saved position to persist between trials. If an earlier
native-button prototype was pinned, it can be unpinned manually; the new widget
does not reserve a pinned-app slot or automatically alter existing pins.

## Persistence and interaction

On a completed drag, the app saves the display device name plus normalized X/Y
fractions in the existing settings service. Coordinates use the monitor's full
bounds, so placement on its taskbar is permitted. Locations are clamped to keep
the widget visible; restoring after a display disconnect falls back to the primary
display. DPI changes resize the widget; display changes restore a visible position.
The original position can be restored when a disconnected display returns.

The previous `TaskbarButtonEnabled` and `TaskbarHoverEnabled` preference keys now
control the widget and retain existing on/off choices. `WidgetDisplayName`,
`WidgetPositionX`, and `WidgetPositionY` store the location. Hover is suspended
while Settings or the menu is open. Mouse capture handles drag/release; capture
loss cancels the pending click. Enter and Space open the menu when the widget
has keyboard focus. The tray, hotkey, search, profiles and shortcut tree are reused.

## Verification on 15 October 2026

- Release build: passed, zero warnings and errors.
- Widget regression project: built with zero warnings and errors.
- Headless regression executable: **33 cases passed** for timing, leave/cancel,
  click versus drag, lost capture, placement/clamping, DPI-sized geometry, legacy
  preference compatibility, and JSON serialization of position/hover state.
- Hidden production-window render: **7 additional cases passed**, checking that
  no visible window was shown, taskbar registration flags, topmost setting,
  pre-display DPI sizing, accessible name/role, and absence of settings writes.
- Offscreen output: `.qa/widget-prototype/widget-offscreen.png`, 104 × 88 pixels
  at 192 DPI (200%). This is a hidden-window GDI render, not a desktop screenshot.
- No Defender exceptions were added; the new test executable ran normally.
- No visible application window was opened or controlled for verification.

Commands:

```powershell
dotnet build TaskFolder.csproj -c Release
dotnet build tests/WidgetPrototypeSmoke/WidgetPrototypeSmoke.csproj -c Release
tests/WidgetPrototypeSmoke/bin/Release/net8.0-windows/WidgetPrototypeSmoke.exe --render bin/Release/net8.0-windows/TaskFolder.dll
```

Native acceptance still required: place the widget on a free taskbar area, change
focus to other apps and back, click/hover and traverse the menu, confirm no
TaskFolder thumbnail appears, confirm other applications' thumbnails still work,
drag without opening the menu, restart and verify the saved location, turn hover
off/on, and check mixed-DPI monitors, display disconnect/reconnect, taskbar auto-hide,
and fullscreen applications. Windows does not reserve space for this overlay;
choose a location that does not cover another taskbar control.

The inline visual review has disposition **recapture**: a native desktop capture
and real interaction are still missing. The offscreen image has been inspected;
it does not establish live z-order, focus, drag or taskbar behavior. The existing
design tokens were reused; DESIGN.md and its stale sidecar were not rewritten.

## Changed source

- `Views/TaskbarWidgetForm.cs`: tool window, direct input, drag, DPI/display handling,
  position commit, accessible role and shared-style drawing.
- `Utilities/WidgetInteraction.cs`: pure hover/gesture state and placement geometry.
- `Program.cs`: widget lifecycle, shared menu anchoring, save-on-drag integration.
- `Models/AppSettings.cs`: display and normalized position fields.
- `Views/SettingsForm.cs`: widget controls and updated instructions.
- `tests/WidgetPrototypeSmoke/`: interaction, persistence, hidden HWND and render checks.
- `docs/widget-prototype.md`: implementation boundaries and native acceptance.

References:

- https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.control.capture
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.win32.systemevents
- https://github.com/dotnet/winforms/blob/v8.0.0/src/System.Windows.Forms/src/System/Windows/Forms/Control.cs
