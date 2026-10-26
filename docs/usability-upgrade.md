# Usability upgrade and recovery runbook

## Scope and version

Stable local release: `1.3.0`, approved by Andrey on 2026-10-26; see [release notes](releases/1.3.0.md). State schema: **2**. Operation journal schema: **1**. Configuration archive schema: **1**. These are separate formats; an archive is not a journal backup or an installation package.

The authoritative implementation plan is `docs/implementation-plans/2026-10-25-taskfolder-usability-implementation-plan.md`. Source/fixture implementation covers S0–S9 and S10 integration. Stable designation records the user's acceptance of the current local application; it does not establish coverage of the blocked packaged provider or unverified native/GUI/platform scenarios. This document does not authorize installation, real-data migration, rollback, publication or Windows configuration changes.

## Data layout

```text
<DataRoot>/
  state.v2.json             revisioned catalog and preferences
  state.v2.json.bak         last-good state
  state.writer.lock        exclusive writer coordination
  settings.json            retained legacy input
  metadata.json            retained legacy input
  Shortcuts/               Default profile managed links/raw EXE bindings
  Profiles/<name>/         other profiles and empty folders
  Assets/Icons/            managed imported icon assets
  Migration/<id>/          explicitly approved legacy snapshot and manifest
  Recovery/<operation-id>/ journal, before/after payloads and compact receipts
  Staging/                 prepared link definitions outside watcher trees
```

Normal root is `%APPDATA%\TaskFolder`; portable root is the executable directory. A fixture passes an explicit temporary root. Verify the actual mode/root before any cutover; never infer them from a process name. Do not mix copies or run an old version against an adopted v2 root.

## Migration

1. Obtain explicit authorization before stopping the installed application or changing real data. Confirm root, portable mode, old process and candidate paths.
2. Open **Manage shortcuts…**. The migration dialog inventories without mutation. Check profiles, nested/empty folders, duplicate basenames, assigned/unresolved legacy metadata, snapshot root and byte size.
3. Confirm the shown scope. The service rechecks fingerprints and snapshots selected legacy JSON/managed link/icon inputs, preserving original files. Raw EXE payloads are hashed and retained in place rather than copied. A changed preview is refused.
4. Commit creates state.v2.json atomically. Existing legacy JSON is retained. Ambiguous original records are retained for review; statistics are not guessed across identical basenames.
5. Reopen the catalog and verify inventory/history, the active launch profile, pin order and missing/unavailable entries. A failed/future-schema read is not permission to overwrite the original.

The manager created before migration subscribes to v2 catalog events after explicit adoption. No live migration was run during implementation; only fixtures were migrated.

## Journal, failure and recovery

Operations prevalidate the whole batch, expected revision/state fingerprint, managed paths, source bytes and preview directory fingerprints. Source/destination changes cause a conflict before staging. The journal records immutable before/after state and per-file payload checksums before applying deltas. Repeated operation IDs cannot apply a different request.

After interruption, open **Recovery…** and inspect pending operations. Recovery compensates uncommitted file deltas or completes an already committed journal marker. New mutations are blocked while recovery is pending/corrupt. Do not delete or edit a journal to bypass that block.

Undo is a new journaled operation. It refuses externally edited files, occupied paths, changed metadata or changed settings. An occupied restore destination can require a replacement filename; it never silently overwrites a new file. Raw EXE removal suppresses its catalog binding; target binaries, unmanaged content and external launch targets stay untouched.

Recovery storage is retained. Only the explicit cleanup action revokes stored undo payloads; compact receipts retain replay protection. There is no time-based auto-purge. Large exports and migration snapshots have separate lifetimes and are not removed by cleanup.

## Rollback boundaries

- **Before real cutover:** keep the installed application/data unchanged; discard or retain the external candidate as requested.
- **After migration, before file mutations:** stop the candidate with authorization, inspect the migration manifest/hashes and choose an explicit restoration procedure for the confirmed root. Preserve state.v2 and the snapshot as evidence; do not overwrite an original on assumption.
- **After move/remove/repair/import:** use operation recovery/undo first and compare state/file inventory. Reverting source or switching EXEs does not undo file mutations. An old legacy JSON view may be stale.
- **After external edits:** stop new mutations. Resolve conflicts using the original journal, current bytes and state revision; do not loop retries, reset data or clear the recovery directory.

## Archives

Exports include a versioned manifest, SHA256 for every managed .lnk/.url payload, profile/folder metadata and managed icon assets. Existing links retain bytes. Extractable external icons become assets; unavailable icons retain a warning/reference. Raw EXE payloads become external links, never copied binaries. Runtime, installed programs, DLL/EXE icon source binaries, local environment files and unrelated documents are excluded.

Targets/arguments may be private or executable commands. The preview offers explicit disclosure; do not send an archive or diagnostic externally without authorization. A displayed manifest description is not proof that a link is safe to launch. No payload is executed during export/preview/import.

Import limits: 10,000 catalog entries, 256 MiB aggregate uncompressed bytes, 16 MiB per icon. Compressed size, entry lengths and streamed bytes are bounded. The entire central directory/manifest/checksum inventory must validate before a commit request is offered. Only declared `manifest.json`, `links/<id>.lnk/.url`, and whitelisted icon assets are accepted. Absolute/UNC/drive paths, traversal, ADS, mixed separators, symlinks/reparse attributes, nonregular objects, case collisions, missing/extra payloads, missing/future schema and truncated ZIPs are refused.

New profiles are the default. Profile prefix, selected profiles/items and per-item skip/rename/replace decisions define the frozen request. Rename must use an unused safe relative path; replacement retains shortcut kind and uses the common undo journal. An identical archive/options import returns the prior result instead of duplicating it. A new copy requires an explicit choice and available destination profile name. After undo, use a new prefix/copy rather than treating the previous receipt as a fresh request.

Optional presentation import covers language/theme/menu settings. Startup registration, hotkey, widget position/monitor, taskbar integration and active launch profile stay local. Archive import does not install applications or register commands with Windows.

## Final acceptance matrix

| Surface | Evidence available | Remaining check |
| --- | --- | --- |
| State/identity/recovery | Isolated migration, stale-write, journal phase faults, replay, raw EXE and unknown-content fixtures | Real root pilot only after separate authorization |
| Manager | Batch/folder/reorder/alias fixtures; native RU/EN light/dark constructors; running RU manager; actual watcher refresh retained/saved alias draft | Repeated drag/cancel, full keyboard/context flow, focus and DPI matrix |
| Add/catalog | File/folder/URL/command/batch fixtures; links preserve arguments and bytes | ShellIntegrationSmoke host execution denied; packaged provider disabled pending native persisted-identity proof; real launches untested |
| Search | Unicode/aliases/layout/fuzzy/ranking/stale-selection fixtures; synthetic 1,000-entry search timing | Korean IME, actual menu navigation and warm actual menu-open p95 |
| Archive | Round-trip, asset/empty-folder, merge/conflict, malicious/quota, undo, fault and idempotency fixtures | Interactive preview/apply walkthrough and transfer to another PC |
| Presentation | Resource key/format/plural/serialization and palette/high-contrast tests; native form constructors; RU manager observed | Live locale/theme input preservation, full long-label/high-contrast and 100/150/200% DPI |
| Widget | Pure boundary/lock/delay/coordinate tests; existing hidden-window/rendering recovery tests | Live taskbar, monitor/DPI change, Start/outside click and lock-during-drag |
| Windows integration | Harness scope inspected; user authorized isolated temporary HKCU test | Test host execution denied before start; no temporary key or real registration changed |
| Packaging | Folder publish and satellite/runtime manifest checks are recorded in the completion report | Installer compiler availability and actual clean-machine install are separate |

The initial Shell/Windows integration execution failures returned Win32 error 5. Windows security settings were not altered and blocked hosts were not routed through another runtime. UI automation subsequently lost reliable foreground/geometry control, so unfinished charters remain gaps. Inno Setup availability and candidate artifacts are reported separately; this is not release approval.

## Build and artifact discipline

Keep QA output under an external task directory. Existing bin/obj and the narrowly ignored installer directory contain disposable tooling output. Source snapshots are explicit manifests plus hashes; do not recursively copy the repository into itself. The original local release freeze preceded Git initialization; its manifests retain `git: null`. The public repository update and stable tag are recorded in [repository provenance](repository-origin.md).

Build-server and task-created UI processes must stop after QA. Keep minimal synthetic evidence and immutable source/publish manifests. Do not delete unrelated artifacts or user data as cleanup. A published folder must retain `ru/TaskFolder.resources.dll`, TaskFolder.dll and its runtime/config dependencies; single-file extraction is disabled.
