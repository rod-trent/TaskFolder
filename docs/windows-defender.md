# Windows Defender and TaskFolder

## Observed on this Windows 11 PC

On 10 October 2026, Defender event **1121** identified rule
`01443614-CD74-433A-B99E-2ECDC07BFC25` (executable reputation/prevalence).
The configured rule mode read as **6 / Warn**. Events named the installer, the
Windows integration smoke executable, and Inno Setup's temporary setup engine:

`C:\Users\honan\AppData\Local\Temp\is-<random>.tmp\TaskFolder-Setup-1.2.4-win-x64.tmp`

These events establish an ASR reputation block. They do not establish a malware
detection or prove that a binary is malware-free. No TaskFolder-specific malware
detection appeared in the targeted event sample.

Defender custom scans with `-DisableRemediation` reported **found no threats**
for the rebuilt installer, its folder payload, the existing application at
`C:\userdata\Taskfolder\TaskFolder.exe`, and the isolated smoke executable.
Scan exclusions were ignored and archives scanned. The signature version was
`1.459.655.0` (updated 10 October 2026). This is a scan result, not a guarantee.

The rebuilt installer SHA-256 is
`c9ca8c2a65ce18397ab5e73cb29c97732351ccb2e56282a04518cdc872674eb6`.

## Local application status

### Temporary launch verification, 18 October 2026

The host-recovery build at
`artifacts/start-menu-host-recovery-20261018/publish/TaskFolder.exe` was blocked
by rule `01443614-cd74-433a-b99e-2ecdc07bfc25` even after direct modification of
the existing per-rule policy value and successful `Get-MpPreference` readback.
A fresh non-elevated launcher did not resolve that attempt. Configuration
readback alone was therefore insufficient evidence of runtime enforcement.

With the user's explicit permission, the successful attempt used the documented
`Add-MpPreference -AttackSurfaceReductionOnlyExclusions` API for **that exact EXE
only**, a three-second settling interval, and a fresh non-elevated launcher.
Defender recorded configuration event 5007 for the addition at 20:28:17 KST;
the process was created at 20:28:20.748 KST. `Remove-MpPreference` removed the
entry immediately after `Start-Process` returned (approximately 0.21 seconds),
and event 5007 confirmed removal. This was an ASR-only file exclusion; no folder,
DLL, antivirus scan/process exclusion, rule-mode change or service restart was
introduced. The exact previous exclusion list, per-rule policy, rule modes and
protection settings were verified restored. Real-time protection stayed enabled.

The registry-versus-API mechanism and settling interval were not isolated in
separate experiments; the internal cache/notification cause remains an inference.
Use the working API path and confirm actual process creation rather than treating
registry/CIM readback as proof that launch is allowed. Do not broaden exceptions
or disable protection if a future launch remains blocked.

PID 21548 loaded the expected patched DLL (SHA-256
`7A0688614B45130E83BD29A469F4AEF2C0CC05D24074AE1A39E12A8E0227285E`)
and recorded successful taskbar attachment. Settings and metadata hashes were
unchanged. Start-menu visual acceptance remains with the user. An additional
1121 event at 20:28:34 names Explorer as the accessing process **after** exclusion
removal; it is distinct from the successful launcher/process creation and does
not establish that the running app exited.

Evidence: `.qa/start-menu-host-recovery/asr-launch-api-20261018-01/`
(`verified-result.json`, `helper-result.json`, `launch.json`, `defender-events.json`
and `native-after-launch.jsonl`). The existing permanent per-rule paths below
were preserved. This temporary exception is no longer installed.

References:
- https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-configure#configure-global-asr-rule-exclusions-in-powershell
- https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-overview#file-and-folder-exclusions-for-asr-rules

On **11 October 2026**, following explicit user authorization and Windows UAC
consent, the six permanent paths below were added for this ASR rule only.
Elevated `Get-MpPreference` readback confirmed the complete scope. All ASR rule
IDs and modes matched the pre-change state; antivirus and real-time protection
remained enabled.

The isolated Windows integration smoke passed **34 checks** using real HKCU
test registry entries and real COM shortcuts. It ran under the same Windows
user with an elevated token, without launching the app GUI. Its temporary
exception was removed and removal confirmed in Defender's configuration.

Results: `.qa/defender/result.json` and
`.qa/defender/windows-integration-smoke.log`.
Rollback state: `.qa/defender/rollback-state.json`.
Earlier verification attempts are preserved separately in the same directory.

Defender normalizes the policy's `>`-separated list to a `|`-separated list in
`Get-MpPreference`. Also, exclusions apply when the accessing process starts:
the successful test used a fresh verifier process started **after** its
temporary exception was configured. A verifier already running before that
change still received an ASR block.

TaskFolder **1.2.4 was installed on 11 October 2026** at
`C:\Users\honan\AppData\Local\Programs\TaskFolder` using the per-user installer
with startup selected. Setup exited with code 0. The installed version, HKCU
uninstall entry, Start shortcut target and `TaskFolder.TaskFolder` AppUserModelID
were verified. The user confirmed that Start search finds TaskFolder as an app
and opens it without a warning.

Windows Settings > Apps > Startup visibly lists **TaskFolder / On**. The Run
entry points to the installed executable with `--startup`. The app's Windows
integration repair refreshed its registration status; the startup command did
not change. A fresh settings window subsequently confirmed that startup is
registered for this copy.

Real-time protection was found off during the first acceptance check. The user
restored it manually; verification made no Defender setting changes. At
**01:13:17 KST**, the previous TaskFolder process had ended and the exact startup
command launched a new process, PID 94564, with antivirus and real-time
protection enabled. It stayed running, and opening settings activated that same
instance. A subsequent custom scan of the installed folder, with exclusions
ignored, found no threats (exit 0). No TaskFolder detection or ASR block appeared
in the protected runtime verification window.

The initial standard-shell installer attempt did record ASR event 1121 at
00:55:05 KST. The successful installation used a fresh elevated process under
the same user at 00:57:04-00:57:09 KST. These are separate attempts; the earlier
block is not a block of the installed app during the later protected test.

Acceptance evidence is in `.qa/windows-acceptance/result.json`,
`registration.json`, `runtime-launch.json`, `runtime-after.json`,
`installed-scan.json`, and `install.log`. An actual Windows sign-in has **not**
been tested; Windows was not restarted and the user was not signed out.

Both `installer/TaskFolder-Setup-1.2.4-win-x64.exe` and
`installer/TaskFolder-1.2.4-win-x64-folder.zip` were built. The ZIP contains the
complete folder payload and does not run Inno Setup's temporary engine.
Keep all extracted files together. This changes packaging, not ASR policy.

## Packaging change

The installer now uses a self-contained **folder publish**, with `TaskFolder.dll`,
`coreclr.dll`, `hostfxr.dll`, and `hostpolicy.dll` beside `TaskFolder.exe`.
`PublishSingleFile=false` and `IncludeNativeLibrariesForSelfExtract=false` avoid
.NET's executable-library extraction under `%TEMP%\.net\TaskFolder`.
All files from a folder publish must stay together.

Inno Setup still uses its normal temporary setup engine. It is separate from
.NET extraction. Do not change `UseSetupLdr` solely to hide the temporary file.

## Reviewed local exclusion scope

The applied change is **per-rule**, for the one rule above, on this PC. It uses
the Windows 24H2+ policy format verified in the installed WindowsDefender.admx:

`HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\ASR\ASROnlyPerRuleExclusions`

The value name is the rule GUID. Its `REG_SZ` data is a `>`-separated path list.
Existing entries must be merged rather than overwritten.

| Path | Purpose |
| --- | --- |
| `C:\userdata\Taskfolder\TaskFolder.exe` | Existing copy |
| `C:\userdata\Taskfolder\TaskFolder.dll` | Application assembly if that copy is updated to folder deployment |
| `C:\Users\honan\AppData\Local\Programs\TaskFolder\TaskFolder.exe` | Default installed application |
| `C:\Users\honan\AppData\Local\Programs\TaskFolder\TaskFolder.dll` | Default installed application assembly |
| `C:\Users\honan\code\forks\TaskFolder-main\installer\TaskFolder-Setup-1.2.4-win-x64.exe` | This local installer |
| `C:\Users\honan\AppData\Local\Temp\is-*.tmp\TaskFolder-Setup-1.2.4-win-x64.tmp` | Only this version's temporary installer engine |

No antivirus scan exclusion, process-wide exclusion, global ASR exclusion, or
rule-mode change was made. Real-time antivirus scanning remains enabled.
The Temp pattern grants this ASR exception to **any file with that exact name in
a matching folder**; a path-based exception is not a hash-based allowlist.

A **temporary verification exception** was applied only to
`C:\Users\honan\code\forks\TaskFolder-main\tests\WindowsIntegrationSmoke\bin\Release\net8.0-windows\WindowsIntegrationSmoke.exe`.
It was removed after the smoke completed. Future verification must likewise
remove only its newly added test entry, preserving any pre-existing entry.

Before applying: record the old rule value and policy enable flag; pin the
installer and application hashes; confirm the elevated administrator session.
Applying needs explicit approval because it changes Windows security settings.
The app/installer must never add these exclusions automatically.

## Verification and rollback

Verify the effective per-rule exclusions using elevated `Get-MpPreference` and
the native policy key. Run the isolated Windows integration smoke only after
its explicit exception is effective. Policy readback is not proof of Start
search, installation, startup after sign-in, or user-visible GUI behavior.

Store the pre-change state and the exact newly added paths in
`.qa/defender/rollback-state.json`. To roll back, remove only those newly added
paths from this rule's current `>`-separated value. Preserve paths added by
others after the change. Delete the value if no paths remain and it did not
exist originally. Restore the policy enable flag only if this operation created
or changed it and no other per-rule values now need it.

Run this rollback in an elevated PowerShell session after inspecting the saved
state. It preserves unrelated exclusions and removes only this operation's
new entries; empty registry keys can remain.

```powershell
$state = Get-Content -LiteralPath 'C:\Users\honan\code\forks\TaskFolder-main\.qa\defender\rollback-state.json' -Raw | ConvertFrom-Json
$ruleKey = Get-Item -LiteralPath $state.RuleKey -ErrorAction Stop
$current = [string]$ruleKey.GetValue($state.RuleId)
$added = @($state.AddedPermanent) + @($state.AddedTemporary)
$remaining = @($current -split '>' | Where-Object { $_ -and $_ -notin $added })
if ($remaining.Count -eq 0 -and -not $state.RuleValueExisted) {
    Remove-ItemProperty -LiteralPath $state.RuleKey -Name $state.RuleId
} else {
    New-ItemProperty -LiteralPath $state.RuleKey -Name $state.RuleId -PropertyType String -Value ($remaining -join '>') -Force | Out-Null
}
$ruleKey = Get-Item -LiteralPath $state.RuleKey
if ($state.FlagCreated -and $ruleKey.GetValueNames().Count -eq 0) {
    $baseKey = Get-Item -LiteralPath $state.BaseKey
    if ($baseKey.GetValue($state.FlagName) -eq 1) {
        Remove-ItemProperty -LiteralPath $state.BaseKey -Name $state.FlagName
    }
}
```

Do not bypass Tamper Protection, signed-script policy, or organization policy if
the administrative operation is refused. Report the real blocker instead.

## Installer 1.2.5 update - 15 October 2026

After explicit user approval, an elevated operation added only these two paths
to rule `01443614-cd74-433a-b99e-2ecdc07bfc25` on this PC:

- `C:\Users\honan\code\forks\TaskFolder-main\installer\TaskFolder-Setup-1.2.5-win-x64.exe`
- `C:\Users\honan\AppData\Local\Temp\is-*.tmp\TaskFolder-Setup-1.2.5-win-x64.tmp`

The six existing entries listed above were preserved. Registry readback and
elevated `Get-MpPreference` both confirmed all eight paths. ASR rule modes,
global ASR exclusions, antivirus exclusions, and the per-rule policy enable flag
were unchanged; antivirus and real-time protection remained enabled.

The installer SHA256 was checked before the operation:
`7EF752327DD010B610B1C207C395C9E466AB7797CA122FC84842AAFAF9D08F0F`.
The same path-based exception caveat above applies to these entries.

Evidence and original state are saved separately from the 1.2.4 operation:

- `.qa/defender/installer-1.2.5/apply-result.json`
- `.qa/defender/installer-1.2.5/rollback-state.json`
- `.qa/defender/installer-1.2.5/Update-InstallerExclusions.ps1`

To remove only the two newly added entries while retaining other current paths,
run the following in elevated PowerShell after obtaining permission for rollback:

```powershell
& 'C:\Users\honan\code\forks\TaskFolder-main\.qa\defender\installer-1.2.5\Update-InstallerExclusions.ps1' -Mode Rollback
```

The installer was not launched or installed during this operation. Effective
policy verification does not establish successful installation or runtime
behavior of the installed 1.2.5 build. The default installed application paths
already have per-rule exceptions; the version present during diagnosis was 1.2.4.

Later on 15 October, installer 1.2.5 was rebuilt with the 200 ms folder-hover
change at the same path. Its current checksum is in the adjacent `.exe.sha256`
file and `.qa/folder-hover-200ms/verification.json`; the hash above records the
earlier approved build. The earlier installer is preserved under
`.qa/folder-hover-200ms/previous-installer/`. No Defender policy was changed for
this rebuild. The rollback helper can remove its recorded entries even when the
installer has subsequently been rebuilt or removed.

## Primary references

- [Microsoft: per-rule exclusions and policy format](https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-configure#configure-per-asr-rule-exclusions-in-group-policy)
- [Microsoft: ASR exclusions and rule-specific support](https://learn.microsoft.com/en-us/defender-endpoint/attack-surface-reduction-rules-overview#file-and-folder-exclusions-for-asr-rules)
- [Microsoft: single-file deployment and extraction](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#native-libraries)
- [Inno Setup: Setup Loader and temporary executable](https://jrsoftware.org/ishelp/topic_setup_usesetupldr.htm)
- [Microsoft: custom Defender scan and DisableRemediation](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus)
