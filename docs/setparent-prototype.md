# TaskFolder SetParent taskbar prototype

**Historical:** superseded by the [desktop widget](desktop-widget.md). The active
application no longer attaches its widget to a taskbar. The notes below describe
the previous prototype and its historical verification.

The active widget is now a child window of `Shell_TrayWnd` or
`Shell_SecondaryTrayWnd`. It is not a floating TopMost overlay or an ordinary
application button. The taskbar itself is not resized, patched, or restarted.
No code is injected into Explorer, and no external package is required.

## Installer 1.2.5

`installer/TaskFolder-Setup-1.2.5-win-x64.exe` packages the approved light-frame
widget, larger orange icon and current mouse behavior as a per-user x64 install.
The self-contained payload includes .NET/Windows Desktop 8.0.32. It was built
with the cached Inno Setup 6.7.3 compiler using a fresh
`artifacts/installer-1.2.5/publish` folder; the previous installer is retained.
The application and setup executable icons were extracted and matched pixel for
pixel against the current ICO. Required payload files, versions, runtime and
absence of user/test data were checked. The SHA-256 sidecar is beside the EXE;
build evidence is `.qa/installer-1.2.5/verification.json`.

The setup is unsigned. It was not installed or launched for this packaging run;
the existing prototype was left running. Close TaskFolder before installing.
The stable AppId, per-user scope, shortcut identity and user-data preservation
rules are unchanged. The previously blocked smoke executable was not rerun.

## Launch and use

1. Exit any earlier TaskFolder copy through its menu.
2. Run `artifacts/setparent-prototype/Start-Prototype.cmd`.
3. The widget starts at approximately 20% of the taskbar length unless a previous
   embedded location is saved. This prototype does not reserve a native taskbar
   slot: drag it to a free section if it covers another taskbar control.
4. Drag the icon with the left mouse button to move along the panel. Release to save.
   Dragging onto another monitor's taskbar reparents the child there. Movement
   outside taskbars remains constrained to the current panel.
5. Click to open the existing TaskFolder menu; hover opens after 300 ms. Dragging,
   leaving early, modal Settings, and an open menu prevent hover activation.
6. Right-click for Settings → **Taskbar widget (SetParent prototype)**. Hover can
   be turned off independently; click remains available. Changes apply with OK.
7. Exit and relaunch the same prototype to check its saved placement.

The launcher uses `--portable`, so its settings and shortcuts are kept in its own
folder. The installed app and previous prototype folders are not overwritten.
An old pinned native-button shortcut can be removed manually; pinning is not used
by this implementation. The tray remains the fallback when attachment fails.

## Application icon

The approved light-orange glass folder/arrow is the shared application icon.
`Resources/TaskFolderIcon.png` is an exact copy of the approved concept;
`Resources/TaskFolder.ico` contains 16, 20, 24, 32, 40, 48, 64, 96, 128 and 256 px
frames. Run `scripts/New-ApplicationIcon.ps1` to regenerate the ICO from that PNG.
Small frames use 32-bit alpha DIBs for native Shell extraction; 256 px uses PNG.
The project embeds this ICO in the executable, and the existing shared loader
provides it to the tray and widget. Installer definitions already reference it.
Artwork provenance is recorded in `Resources/TaskFolderIcon.provenance.json`.

The rebuilt package passed the 66 hidden widget checks, native Windows loading
of all ten ICO sizes, and extraction of the orange icon from its executable.
The running widget was visually checked in an actual taskbar capture at 144 DPI.
Evidence is `.qa/application-icon/verification.json`, `native-icon-sizes.png`,
and `widget-on-taskbar.png` in the same evidence folder. The installed app was
not replaced as part of this local prototype update.

## Hosting and thumbnail boundary

`TaskbarWidgetForm` uses managed `TopLevel=false`, `ShowInTaskbar=false`, native
`WS_CHILD`, and no `WS_POPUP`/`WS_EX_APPWINDOW`. `TaskbarNative.Attach` calls
`SetParent` and verifies the resulting parent with `GetParent`. Handle creation
and attachment run under a thread DPI context matched to the target taskbar.
The HWND is verified before it is shown. Failure removes the widget and records
an error rather than presenting an unattached ordinary application window.

The child uses `WS_EX_LAYERED` and `UpdateLayeredWindow` with a premultiplied-alpha
bitmap. The orange icon has the earlier light filled frame and thin rounded
border: the shared Surface color at rest, Hover while hovered/menu-open, and
Pressed during a press. The border uses the shared
accent while focused or while the menu is open. The drawing canvas is 32 logical
pixels, compensating for the artwork's padding to match the visible size of
neighboring taskbar icons. It is rendered from a 128 px ICO frame for DPI clarity.
Pixels outside the rounded frame have the minimum nonzero alpha (1/255),
retaining the whole 44×44 logical-pixel interaction area at the outer corners.
Zero-alpha or color-key pixels would pass input through
to Explorer. No `WS_EX_TRANSPARENT` style is used.

On this PC the original non-layered child passed native visibility and
hit-testing but was absent from the actual taskbar capture. An initialized
layered composition surface makes its drawing visible above the taskbar visuals.
CreateParams preserves that style across WinForms state updates. The smoke test
uses the production supported-OS manifest so layered children work consistently.

WinForms normally assigns a hidden owner to non-taskbar top-level Forms after
creation, which can undo a native parent assignment. Keeping the managed Form
non-top-level avoids that path; regression checks cover parent/style retention
through creation, Enabled changes, and Show.

The launcher menu has a separate transparent tool-window owner. It stays a
top-level popup and can extend above the taskbar without being clipped as a child.
Neither the embedded widget nor this owner requests a native application button,
so there is no ordinary TaskFolder app-button thumbnail to trigger. Windows-wide
thumbnail settings remain unchanged. Actual live hover and thumbnail behavior
still require the native acceptance below.

## Placement and recovery

`WidgetDisplayName` stores the preferred monitor. `WidgetTaskbarPosition` stores
a normalized fraction of the panel's horizontal or vertical axis. The older
desktop X/Y values are retained but not reinterpreted as panel coordinates.
Widget thickness is centered and fitted to the host; drawing uses the host DPI.

Every 750 ms the UI thread checks the host handle, process, DPI and parent.
It refreshes size/placement and raises only the widget within its sibling order.
Taskbar discovery queries `Shell_TrayWnd` and `Shell_SecondaryTrayWnd` directly
with `FindWindowEx`. If a lookup yields no hosts, the existing host is retained
only while its HWND, owning process and window class still match. Its geometry
is refreshed when available; an isolated geometry-query failure retains the
last valid snapshot. A destroyed or reused handle is rejected. A discovered
replacement still takes priority, preserving display/Explorer recovery.
This prevents an empty discovery result alone from disposing a live widget.
Retention is recorded in `taskbar-embedded.log` for subsequent operator checks.
After the widget's first explicit Show, recovery also restores its native
`WS_VISIBLE` flag with `SWP_SHOWWINDOW | SWP_NOACTIVATE`. Moving or redrawing a
natively hidden HWND alone cannot make it visible again. Readiness is recorded
in `SetVisibleCore`, since an embedded non-top-level Form need not raise `Shown`.
Refreshes before the first Show stay hidden. Recovery never shows or activates
the host, so a hidden taskbar ancestor still hides its child.
It does not move Explorer's controls. If Explorer or a display destroys the child,
the app recreates it on the selected taskbar, falling back to the primary taskbar
until the preferred display returns. Checks pause during a drag. Failures are
deduplicated in `taskbar-embedded.log` beside the portable executable (128 KiB cap).

## Outside-click menu dismissal

Each root `Windows11ContextMenuStrip` observes mouse-button presses only while
open. The root and all visible nested dropdowns form its inside region. A press
outside schedules `Close(AppClicked)` on the UI queue, independent of whether
the popup's foreground activation succeeded. The low-level callback always
passes the original event onward; it does not swallow, inject, or record input.
Movement, wheel and button-release events do not dismiss the menu.

Only the root subscribes. Closing/disposal unsubscribes, and a session token
prevents a delayed outside click from closing a subsequently reopened menu.
Existing Escape, Alt/input-language handling and folder-hover delays are kept.
The automated dismissal checks use a fake click source and a queued dispatcher;
they do not install a desktop mouse hook or replay real mouse gestures.

The candidate in `artifacts/outside-menu-dismissal-20261018/publish` passes
**97 hidden-window/logic checks** (15 new dismissal cases). Real outside clicks
on another app, the desktop and taskbar, plus clicks inside search and nested
menus, remain required for operator acceptance. The running app is unchanged.
Evidence: `.qa/outside-menu-dismissal/`.

## Verification

The user confirmed that the earlier native-visibility patch did **not** resolve
the Start-menu disappearance. The running app's log records empty taskbar
discovery followed by widget removal and later reattachment to the same host
HWND. This establishes a separate recovery defect; it does not establish why
the original lookup fails during the shell transition.

The subsequent user-triggered hidden-state capture confirms the lookup failure
with Start open: `EnumWindows` returns no taskbars, while both `FindWindow` and
`FindWindowEx` return the still-valid, visible `Shell_TrayWnd` HWND 66714, owned
by Explorer process 4556. No TaskFolder child remains beneath it. The packaged
new lookup discovers that taskbar and all 82 hidden-window checks pass while
Start remains in the foreground. This verifies discovery in the failing shell
state; the new full application has not yet been launched for visual acceptance.
Evidence: `user-hidden-native-state.jsonl`, `hidden-class-lookup-before.json`,
`hidden-state-smoke.log`, and `hidden-class-lookup-after.json` in the same folder.

The subsequent host-retention change passes **82 hidden-window checks**,
including retention after empty discovery, geometry refresh, replacement
priority, and rejection of a destroyed host. The read-only 45-second native
sample did not capture a Start transition. The new build still requires a
manual Start-menu check and has not replaced the running app. Evidence is in
`.qa/start-menu-host-recovery/`.

The Start-menu visibility recovery change passes **76 hidden-window checks**.
The new native-hide regression fails against the installed pre-fix assembly and
passes against the patched assembly. It also checks pre-Show invisibility,
preserved parenting/placement, restoration above a synthetic sibling, and no
foreground focus change. Both Release builds pass with zero warnings/errors.
Evidence is `.qa/start-menu-visibility/baseline-test.log` and `fixed-test.log`.
These tests do not reproduce a real Start-menu click, prove taskbar pixels, or
validate taskbar auto-hide on the desktop. Opening/closing Start repeatedly and
checking the widget without clicking other taskbar buttons remains required.

The light-frame restoration keeps the larger icon, restores the earlier light
surface/border styling, and leaves the six-dot grip removed. Both Release builds
passed with zero warnings/errors. The current packaged prototype is running;
its actual taskbar appearance was inspected in `light-frame-widget-live.png`.
Mouse interaction handlers were unchanged. The previously blocked smoke
executable was not rerun for this visual change.

The larger-icon/framed refresh builds both Release projects with zero warnings
or errors. The updated packaged app is running. An actual 144 DPI taskbar capture
was inspected alongside neighboring icons: the orange glyph now matches their
visual scale, and the rounded frame is visible with a transparent interior.
Native hit tests still reach the widget at its center and padding. Evidence:
`framed-large-widget-live.png`, `taskbar-framed-large-live.png`, and
`framed-large-widget-verification.json` in `.qa/setparent-prototype/`.
Click/hover/drag handlers were not changed; physical gestures were not replayed.
The previously blocked smoke executable was not rerun.

The transparent-icon refresh builds both Release projects with zero warnings
or errors. The current packaged app was launched, its native alpha presentation
succeeded, and an actual 144 DPI taskbar capture confirmed no visible plate or
border (`.qa/setparent-prototype/transparent-widget-live.png`). Native hit tests
at the arrow cutout and surrounding padding both returned the widget HWND.
The existing click/300 ms hover/drag handlers were preserved. Physical mouse
gestures were not replayed; the smoke executable was not rerun after its prior
Defender ASR block. Current evidence is `transparent-widget-verification.json`.

The icon-only refresh removes the six-dot grip and its spare width, centers the
icon in a 44×44 logical-pixel surface, and retains the existing whole-surface
mouse handlers for drag/click/hover. Release and smoke-project builds passed
with zero warnings/errors. Defender ASR rule
`01443614-CD74-433A-B99E-2ECDC07BFC25` blocked the rebuilt smoke executable
(event 1121), so its 66 checks were not rerun for this refresh. The updated
packaged application was restarted and logged successful taskbar attachment;
real-pointer and visual acceptance of this refresh remain unverified.

Release and regression-project builds pass with zero warnings/errors.
The headless smoke passes **66 checks**: 45 interaction/layout/serialization cases,
20 checks on real hidden HWNDs, and one read-only discovery of the actual taskbar.
It calls the production widget's SetParent path, checks WS_CHILD/no popup/no app
button, verifies the native Show path under a hidden synthetic parent, reparents
between synthetic parents, destroys a host, and creates a replacement child.
The offscreen widget image is `.qa/setparent-prototype/widget-offscreen.png`.
These checks do not attach a window to Explorer or show visible desktop UI.

An explicit `--live <TaskFolder.dll>` smoke mode runs the production application
context for 15 seconds, verifies actual child parenting and hit-testing above the
taskbar composition surface, opens the production menu and checks its hit-testing,
then disposes the test context. It is used only after GUI permission. Its portable
test data belongs to the smoke executable's folder, not the installed app.

The approved live run on this PC passed **8 checks** at 192 DPI: the widget was
parented to the actual taskbar, its center hit-tested to its own child HWND, and
the production menu was visible as an independent popup and hit-tested above the
taskbar. The context closed itself after 15 seconds; no TaskFolder or smoke-test
process remained. Evidence is `.qa/setparent-prototype/live.log`. A subsequent
cross-monitor capture guard was rebuilt and passed the hidden checks; that drag
path has not been exercised on real monitors. This initial live smoke proved
parenting and input hit-testing, not actual visible rendering.

After the reported invisible-widget failure, `--probe-running <pid>` reproduced
the difference between native hit-testing and the actual desktop pixels. The
layered-surface fix was then verified against the running packaged prototype:
the real taskbar capture visibly shows TaskFolder immediately to the right of
Start, and its center still hit-tests to the embedded child HWND. Evidence is
`.qa/setparent-prototype/widget-on-taskbar.png` and
`.qa/setparent-prototype/running-visibility-fix.log`. The corrected prototype
was left running with permission. Physical hover/click and restart acceptance
remain separate from this visual check.

Native operator acceptance: move the actual pointer onto/off the widget, verify
one 300 ms hover and immediate click with no thumbnail, traverse menu/search and
submenus, drag without opening the menu, restart and verify saved position, disable
hover and restart, check other applications' thumbnails, auto-hide, mixed-DPI and
secondary monitors, and recovery after Explorer restart. Do not equate synthetic
host destruction with an actual Explorer-restart test.

## Changed files

- `Utilities/TaskbarHosting.cs`: discovery, DPI scope, SetParent/style verification,
  native coordinates and pure taskbar placement helpers.
- `Views/TaskbarWidgetForm.cs`: child lifecycle, taskbar-only drag and persistence.
- `Views/WidgetMenuOwnerForm.cs`: independent popup owner without an app button.
- `Program.cs`: host watcher, recovery, menu owner, scoped diagnostics.
- `Models/AppSettings.cs`: separate saved axis fraction.
- `Views/SettingsForm.cs`: embedded-widget preferences.
- `tests/WidgetPrototypeSmoke/`: geometry, production HWND and optional live checks.

References:
- https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowshookexw
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-findwindowexw
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-updatelayeredwindow
- https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#layered-windows
- https://github.com/dotnet/winforms/blob/v8.0.0/src/System.Windows.Forms/src/System/Windows/Forms/Form.cs
- https://github.com/airtaxi/Deskband11Lib#how-it-works
