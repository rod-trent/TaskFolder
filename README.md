# TaskFolder

Local Windows application launcher with a tray menu and optional desktop widget.

## Stable release

`1.3.0` is the stable local release approved by Andrey on 2026-10-26. It includes the desktop widget, the Gates icon, configurable widget size and hover-menu dismissal, consistent submenu typography, menu fades and deferred Settings opening. It targets Windows 10/11 and .NET 8 Windows Desktop. The self-contained folder includes its runtime; framework-dependent builds require that runtime separately. See [release notes and verification boundaries](docs/releases/1.3.0.md).

The packaged-app catalog provider remains disabled until its native Shell identity check passes. The desktop/PWA catalog, explicit file/folder/URL/command input and existing shortcut import remain available. See [the upgrade and recovery runbook](docs/usability-upgrade.md) for limits, migration, rollback and acceptance gaps.

## Features

- Eight direct favorites, overflow for all favorites, independent pin order and a visible launch profile.
- One manager for profiles/folders, multiselect, move/copy/reorder, folder creation, aliases, pinning, target repair and status filtering. Keyboard/context actions accompany drag operations.
- Moving or browsing another profile does not activate it. Switching the launch profile is a separate action.
- Add files, folders, HTTP/HTTPS URLs, explicit executable/interpreter commands or existing `.lnk`/`.url` files. Batch preview shows destinations, duplicates and launch arguments. Adding never launches an item.
- Existing `.lnk` bytes are preserved, including browser/PWA arguments and Shell metadata. Launch dispatch uses the `.lnk` itself. Actual application startup/window behavior requires a native launch check.
- Search covers Unicode names and aliases, unordered words, RU/EN keyboard layout and bounded typos. Direct matches rank first. Arrows select a result, Enter uses that identity, and empty/stale selection has no launch target. Korean IME behavior still requires interactive acceptance.
- Journaled operations, recoverable link removal, explicit restore and explicit recovery cleanup. Raw EXE payloads, unmanaged files and launch targets are protected from shortcut removal.
- Versioned ZIP configuration export/import with checksums, quotas, selected profiles/items, new profiles by default, explicit merge/conflict choices and import undo. Installed apps/runtime are not copied.
- RU/EN/System language and Light/Dark/System theme. Windows high contrast takes priority. Native file/folder dialogs retain Windows styling.
- Desktop widget that moves in both axes, stays in Explorer's desktop layer below application windows, and remembers its display and position. The tray icon provides access while applications cover the desktop. Size is adjustable from 50–200%, with position lock, a 100–1500 ms opening hover delay (default 300 ms) and a 200–2000 ms hover-menu hide delay (default 600 ms). See [desktop widget behavior and checks](docs/desktop-widget.md).
- Main, nested and shortcut context menus fade in over 160 ms; the dismissed root fades out over 100 ms with eased timing. Sibling submenus close directly while the root remains interactive. Closing releases input immediately. Windows animation settings and high contrast disable these effects. See [menu motion and verification](docs/menu-motion.md).
- Separate Recently Used/Most Used visibility, custom icons, header logo, hotkey and explicit Windows integration settings.

## First use and existing data

Normal data root: `%APPDATA%\TaskFolder`. Portable mode (`TaskFolder.exe --portable`) uses the application directory.

For legacy data, open **Manage shortcuts…** and review the migration dialog. It shows the exact root, inventory, unresolved records, snapshot destination and size. Migration runs only after **Create snapshot and migrate** and its confirmation. Original legacy JSON/link files remain intact; raw EXE files are not copied into the snapshot. Migration is not an automatic side effect of opening the application.

After migration the authoritative catalog is `state.v2.json`, with a last-good `.bak`, revision/fingerprint checks and a single writer lease. Legacy `settings.json`/`metadata.json` are retained for compatibility/reference and are not the v2 source of truth. Do not run old and new versions against the same root concurrently.

## Manager and recovery

Open **Manage shortcuts…** from the tray menu. Its tree selects the browsed profile/folder; the location line also identifies the launch profile. **Use this launch profile** explicitly changes launch behavior.

- F2: rename; Delete: remove through Recovery.
- Ctrl+M: move; Ctrl+Shift+C: copy; Ctrl+N: new folder; Ctrl+O: Add.
- Alt+Up/Down: reorder the complete folder group. Clear search/attention filters first.
- Ctrl+P: pin/unpin; Ctrl+F: search; Ctrl+Enter in aliases: save the draft.
- Internal drag moves/reorders; Ctrl-drag copies. External file drops enter the Add preview.

A preview is not a reservation. Changed state revisions, source bytes or destination contents invalidate confirmation rather than overwrite newer data. Alias drafts remain bound to the original ID during refresh/navigation. Concurrent alias edits require resolution.

**Recovery…** lists persisted operations and their stored payload/metadata sizes. Recover interrupted operations before further writes. Restore refuses changed/occupied paths; choose another name where offered. Cleanup is permanent, separately confirmed and never automatic.

## Export and import

Use the manager toolbar overflow for **Export configuration…** and **Import configuration…**. Export to a new ZIP path. Archives can contain private command arguments and external target references; choose their destination deliberately.

Import validates the entire archive before staging a journaled operation. Choose profiles/items and a destination prefix; existing profile names require explicit merge. A conflict defaults to skip; rename needs an unused relative path, and replace must retain link kind. Applying language/theme/menu preferences is optional. Startup registration, hotkey, taskbar/monitor positions and other local integration preferences are retained.

Defaults: 10,000 catalog entries, 256 MiB total uncompressed data, 16 MiB per icon. Absolute/UNC/drive paths, traversal, ADS, duplicate/case-colliding names, symlinks, undeclared payloads, unsupported schemas and checksum mismatches are refused. Raw executable binaries are excluded; they become external link definitions with warnings. Identical repeated import is idempotent; a separate copy is explicit.

## Build and checks

From the source directory:

```powershell
dotnet build TaskFolder.csproj -c Release
dotnet run --project tests/CoreSmoke/CoreSmoke.csproj -c Release -- all
dotnet run --project tests/PowerMenuSmoke/PowerMenuSmoke.csproj -c Release
```

Core lanes: `baseline`, `migration`, `operations`, `favorites`, `organizer`, `adding`, `search`, `backup`, `presentation`, `widget`, `menu-motion`. They use temporary roots and fake launch/power adapters. The `menu-motion` lane also creates hidden native windows; it does not show visible menus or exercise real input.

Native/interactive lanes require inspecting their effects and appropriate authorization:

```powershell
dotnet run --project tests/TaskbarPrototypeSmoke/TaskbarPrototypeSmoke.csproj -c Release
dotnet run --project tests/WidgetPrototypeSmoke/WidgetPrototypeSmoke.csproj -c Release
dotnet run --project tests/WindowsIntegrationSmoke/WindowsIntegrationSmoke.csproj -c Release
dotnet run --project tests/ShellIntegrationSmoke/ShellIntegrationSmoke.csproj -c Release -- catalog
dotnet run --project tests/UiSmoke/UiSmoke.csproj -c Release -- constructors
```

`WindowsIntegrationSmoke` writes/removes only a temporary test HKCU subtree and temporary links. `WidgetPrototypeSmoke` uses hidden synthetic windows; its default success does not prove the real taskbar. `UiSmoke` without arguments opens a disposable fixture launcher; it never creates the production application context or a launch callback. Its Settings window can expose real integration actions: leave those controls untouched during fixture QA.

Local self-contained folder creation:

```powershell
dotnet publish TaskFolder.csproj -c Release -r win-x64 --self-contained true -o <external-candidate-folder> -p:PublishSingleFile=false -p:IncludeNativeLibrariesForSelfExtract=false
```

Keep the complete folder, including `TaskFolder.dll`, runtime files and `ru/TaskFolder.resources.dll`. `BuildInstaller.ps1` accepts `-PublishPath`, `-SkipPublish` and `-OutputDirectory`; it checks the Russian satellite. Inno Setup must already be available. Compiling an installer does not install, restart or publish the application.

## Acceptance still required

The completion report records exact checks and source/artifact hashes. Packaged Shell identity and isolated Windows integration execution are blocked by Windows access-denied errors on their test hosts. Full keyboard/IME, repeated drag/cancel, live theme/DPI/high-contrast, real app/PWA launch, Windows 10, real taskbar/monitor lifecycle, and warm actual menu-open p95 remain separate acceptance scenarios. Green fixtures do not establish those results.

The accepted build was applied to the installed application during this local workflow. The stable release is recorded in [the release notes](docs/releases/1.3.0.md). No real-data migration or public upload is part of this release. This source folder has no Git metadata; no commit, push or Git release tag is claimed.

MIT License; see `LICENSE.txt`.
