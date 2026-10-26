# Desktop widget

The optional widget is a layered child of Explorer's existing desktop icon host
(`Progman` or `WorkerW` containing `SHELLDLL_DefView`). On newer Explorer layouts
with a nonredirected container and a visible layered icon view, it is parented
inside `SHELLDLL_DefView` so the composed icon surface cannot cover it as a sibling.
A hidden icon view uses the container instead. Explorer's styles and visibility
are never modified. The widget moves freely in both
axes within a display's full desktop bounds. Application windows cover it; it
does not occupy a taskbar slot. The existing tray icon opens the launcher while
an application covers the desktop. `--menu` activation also uses the tray menu.

Click or optional hover opens the existing menu beside the widget. The popup
uses its independent foreground owner, so it can receive keyboard input and
dismiss on outside click. Dragging does not open the menu. Right-click opens
Settings. Hover delay and position lock remain configurable.

Settings also offers a saved widget size from 50–200% (default 100%). Frame,
artwork and hit area scale together with display DPI; resizing restores the
normalized saved position and keeps the widget inside its desktop host.

Menus opened by widget hover hide after the pointer stays outside the widget,
root menu and every visible submenu for the configured delay (200–2000 ms,
default 600 ms). Returning resets the countdown. A held mouse button pauses it;
keyboard/IME input or right-click switches that session to normal menu lifetime.
Click-, keyboard- and tray-opened menus retain their existing close behavior.

## Application icon

The shared application icon is the approved amber/orange **Врата (Gates)** mark.
`Resources/TaskFolderIcon.png` retains the exact generated artwork; provenance
and the prompt are recorded in `Resources/TaskFolderIcon.provenance.json` and
`design/icon-concepts/2026-10-26/06-gates-amber.md`. Run
`scripts/New-ApplicationIcon.ps1` to regenerate the ten-size ICO. It embeds into
the application executable and installer, and the existing shared loader supplies
the same artwork to the tray and widget. The square source retains its original
proportions and transparency during conversion.

## Position and recovery

Existing `TaskbarButtonEnabled` and `TaskbarHoverEnabled` settings retain their
on/off meanings. `WidgetDisplayName` and normalized `WidgetPositionX/Y` restore
desktop placement. The previous `WidgetTaskbarPosition` is retained without
being interpreted as a desktop coordinate. Without saved X/Y, the widget starts
near the lower-right part of the desktop. A disconnected display falls back to
an available display without overwriting the saved preference until a new drag
is committed. Placement intersects monitor bounds with the desktop host's actual
client rectangle, keeping the child within its parent and outside monitor gaps.

The watcher recovers a hidden/destroyed child and follows Explorer host changes.
It reuses cached pixels, skips unchanged position/size updates, and retains the
existing bounded graphics-failure retries. Hover checks the actual native hit
target so a covering window cannot trigger the widget through its surface.
If Explorer's desktop host cannot be found, tray access stays available.

No Explorer restart, shell injection, forced WorkerW creation, desktop style
modification, registry change, new dependency or top-level overlay is required.
The internal `TaskbarWidgetForm`, `TaskbarHost` and native helper names remain for
compatibility with the existing implementation and regression harness.

## Verification

Run the pure pointer, placement and persistence cases:

```powershell
dotnet build TaskFolder.csproj -c Release
dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- widget
dotnet run --project tests/WidgetPrototypeSmoke/WidgetPrototypeSmoke.csproj -c Release -- --render bin/Release/net8.0-windows/TaskFolder.dll
```

The render harness uses hidden synthetic parents, including a nonredirected
desktop with a layered icon view, exercises the production child
and rendering recovery, and reads the real desktop host without attaching to it.
Its output goes to `%TEMP%/taskfolder-desktop-widget-20261026/`. These checks do
not establish visible desktop stacking or real pointer interaction.

Interactive acceptance still required: drag across the desktop in both axes;
restart and verify placement; open apps over the widget and access the menu from
the tray; test click/hover/drag/lock and outside-click dismissal; use Win+D twice;
check fullscreen apps, Explorer restart, wallpaper changes, virtual desktops,
display disconnect/reconnect and mixed-DPI monitors. Rendering inherits the
desktop host DPI; physical sizing across mixed-scale monitors needs particular
attention. Explorer's desktop window tree is an implementation detail, so its
behavior needs native verification on the target Windows build.

The native parenting and sibling-order implementation follows Microsoft's
[SetParent contract](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setparent),
[child window rules](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features),
and [SetWindowPos semantics](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos).
