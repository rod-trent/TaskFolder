# Taskbar access prototype

**Superseded:** the active application now uses the floating widget documented in
[widget-prototype.md](widget-prototype.md). The native-button experiments below
are retained as historical evidence and do not describe the current launcher.

## Hover detection correction (15 October 2026)

The first manual trial displayed the native Windows thumbnail instead of the
TaskFolder menu. That confirms a failed hover interaction; the precise runtime
cause has not been captured. Code review found an overly strict identity match:
`Appid: TaskFolder.TaskFolder` (with a space) was rejected unless the UIA name was
exactly `TaskFolder`. The detector now extracts and trims the AppID independently
of any decorated or localized button label. A different explicit AppID cannot
match just because its label says TaskFolder. Eight regression cases were added.

Windows thumbnail settings and Explorer behavior are not changed. The correction
addresses detection; it does not establish that the menu will replace or always
remain above the Shell thumbnail. That still needs a native trial.

Follow-up manual result: both the launcher menu and the native thumbnail appear,
and the thumbnail covers the menu. Detection is therefore functioning in that
trial, but the native-button prototype fails the required menu-only interaction.
Changing menu z-order would not satisfy that requirement. Windows-wide thumbnail
settings must remain unchanged. A custom overlay widget would be a different
surface and would need explicit agreement on positioning and its lack of a
reserved native taskbar slot before implementation.

The updated package is in `artifacts/taskbar-prototype-hover-fix/`. Exit the running
prototype and launch `Start-Prototype.cmd` from that new folder. This folder uses
its own portable settings/shortcuts; the earlier prototype's data is retained in
the earlier folder. No running process is stopped or updated automatically.

This build writes a bounded `taskbar-hover.log` beside its executable in portable
mode (up to 128 KiB). It records TaskFolder candidates, identity matches, UIA
errors, dwell expiry, and menu display. It does not log other app candidates or
shortcut contents. If hover still fails, this log distinguishes failure to detect
the button from a detected hover whose menu was hidden or not displayed.

Both the application and the updated regression project build successfully with
zero warnings/errors. Regression execution has not been retried after the earlier
Defender block; no Defender settings were changed for this correction.

The running app now has a normal taskbar button backed by a minimized, transparent
WinForms window. Clicking restores the launcher menu instead of an empty window.
The existing tray menu, search, profiles, shortcuts, and hotkey are reused.

Settings → **Taskbar access (prototype)**:

- **Show TaskFolder button on the taskbar** controls the taskbar window.
- **Open menu on hover after 300 ms (experimental)** controls hover independently.
- Both default to enabled, including when loading older settings files.
- Changes apply after **OK**. Cancel preserves the previous values.

Hover uses native Windows UI Automation on an MTA background thread, with no new
package dependencies. It checks only when the pointer is over a Windows taskbar,
identifies the TaskFolder button, and opens once per continuous 300 ms visit.
Leaving, dragging with the left mouse button, and disabling hover cancel the dwell.
Settings temporarily suspends hover. Explorer failures are retried off the UI thread.
The tray remains available if detection is unavailable.

## Run locally

1. Exit the currently running TaskFolder through its tray menu. The single-instance
   guard deliberately prevents a second copy from running alongside the installed app.
2. Run `artifacts/taskbar-prototype/Start-Prototype.cmd`, which starts
   `TaskFolder.exe --portable --menu` from the packaged folder.
   This framework-dependent prototype requires the .NET 8 Windows Desktop runtime.
   Portable mode keeps its settings and shortcuts beside this executable, separate
   from the installed app's `%APPDATA%/TaskFolder` data.
3. The button appears while TaskFolder runs. Pin it manually through the taskbar's
   context menu if desired; the prototype does not change Windows pinning settings.
4. To return to the installed app, exit the prototype and launch the installed copy.

Start-menu `--settings` activation is preserved. A second launch without that flag
now opens the menu in the existing process. The taskbar window supplies explicit
pin/relaunch metadata using `--menu`, retaining `--portable` when applicable.
`--menu` opens the launcher on a first launch too. Startup launches remain silent.

## Verification

Current result (15 October 2026): application Release build, smoke-project build,
and framework-dependent publish passed with zero warnings/errors. The smoke
executable could not run: Defender ASR event 1121 blocked it even after a
user-approved exact-path per-rule exception was confirmed. The exception was
removed; the original policy value was restored exactly, rule modes were
unchanged, and antivirus/real-time protection remained enabled. See
`.qa/taskbar-prototype/defender-result.json`. No native GUI acceptance was run.

Automated smoke: `dotnet run --project tests/TaskbarPrototypeSmoke -c Release`.
It covers dwell timing, cancellation, reentry, one-open-per-visit, target movement,
negative coordinates, settings compatibility/persistence, native COM initialization,
and worker shutdown. It does not show or interact with desktop windows.

Native acceptance still required on the target Windows installation:

- Click repeatedly, launch a shortcut, reopen, type into search, and dismiss with
  Escape or an outside click. No empty window or duplicate TaskFolder button.
- Hover for 300 ms, leave sooner, move from button into menu/submenus, dismiss
  without leaving the button, and leave/reenter. No menu reopening loop.
- Disable hover, save, restart; click still opens. Cancel does not apply changes.
- Disable the taskbar button; the tray still works. Reenable it from Settings.
- Check Windows thumbnail preview interference, focus/search, Alt+Tab, pin/relaunch
  (including when the app was started with `--startup` or `--portable`), and close.
- Move/reorder the button, test multiple monitors with different DPI, auto-hide,
  and restart Explorer. Detection must not open menus for another taskbar button.

Windows owns taskbar previews and exposes no supported custom per-button hover
callback. UIA taskbar identity and preview/focus behavior are prototype assumptions
until confirmed by those native checks. No Explorer injection or shell patching.

## Source changes

- `Program.cs`: taskbar lifetime, menu positioning, distinct menu/settings activation.
- `Views/TaskbarButtonForm.cs`: minimized taskbar window and click handling.
- `Services/TaskbarHoverService.cs`: worker, dwell cancellation, one trigger per visit.
- `Utilities/TaskbarAutomation.cs`: native taskbar button discovery.
- `Models/AppSettings.cs`, `Views/SettingsForm.cs`: persisted button/hover controls.
- `Services/WindowsIntegrationService.cs`: separate menu activation event name.
- `Utilities/WindowsShellLink.cs`: window identity and pin/relaunch metadata.
- `tests/TaskbarPrototypeSmoke/`: headless timing/persistence/COM smoke project.
- `docs/taskbar-prototype.md`: launch, rollback-to-installed-app steps and acceptance.

The source folder is not a Git checkout. Changes were reviewed in the affected
logical units; no commit, synchronization to the publish checkout, or installation
was performed.

References:
- https://learn.microsoft.com/en-us/windows/win32/shell/taskbar
- https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-threading
- https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-obtainingelements
- https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/UIAutomationClient.h
