# Making of "Malware" | CYBRSLC&CDRU

**Build a behavior. Observe its artifacts. Verify cleanup.**

A hands-on Windows workshop exploring malware-like behavior through a transparent,
opt-in simulator. Participants modify and compile C# source, investigate processes
and persistence, test YARA rules, and practice evidence-backed cleanup.

**Audience:** beginner to intermediate · **Duration:** 120 minutes · **Format:** pairs

> **Lab use only.** Use disposable Windows VMs, a clean snapshot, and dummy data.
> This is not live malware, a covert keylogger, or a remote-access tool.
> Some actions are real: worker processes, explicitly installed startup entries,
> timestamp files, and explicitly exported input-exercise logs.

## What you will learn

- Connect source code to observable Windows behavior.
- Compare exact-file hashes with pattern-based YARA detection.
- Identify processes by path, PID, parent, and command line.
- Distinguish persistence installation from successful later execution.
- Investigate Registry Run entries and scheduled tasks.
- Test scoped input handling without collecting other applications' keystrokes.
- Preserve evidence, remove lab artifacts, and verify cleanup.

## Simulator behavior

| Component | What it does |
| --- | --- |
| GUI controller | Displays consent controls, diagnostics, and Stop controls |
| Popup demo | Up to five owned demo windows; replaces the batch every 45 seconds after the intro |
| Lab workers | Six labeled processes; no automatic respawn; monitor their controller |
| Real applications | One launch attempt each for Notepad and Calculator after demo consent |
| Optional persistence | Separate consent installs a current-user Run value and scheduled task |
| Marker modes | Write small timestamps and exit; do not launch the popup demo |
| Keyboard exercise | Separate opt-in; captures only its own input box; latest 500 events; explicit export |
| Sound | Optional and off by default |

The GUI has no networking, credential theft, encryption, global keyboard capture,
privilege escalation, or self-repair. Its fake terminal commands are visual only.
Opening the GUI or starting the popup demo does **not** install persistence.

## Requirements and containment

- Disposable Windows VM with a standard-user account and clean snapshot.
- Windows PowerShell **5.1** and the **.NET Framework 4.x C# compiler**.
- ProcMon, Process Explorer, Registry Editor, Task Scheduler, and optionally ILSpy.
- YARA with the `pe` module for detection exercises.
- Some Windows YARA builds require Microsoft's
  [Visual C++ x64 Redistributable](https://aka.ms/vs/17/release/vc_redist.x64.exe).

Prepare tools before isolating the VM. Disable guest networking for the core
exercise and disconnect shared folders, clipboard, drag-and-drop, and passthrough
after staging materials. Do not use personal credentials or production systems.
If protection blocks an action, preserve the alert; do not disable protection.

## Quick start

Commands below assume this repository layout, with sources under `Simulator`.
From the repository root, enter that folder:

```powershell
Set-Location .\Simulator
```

If your workshop download has the source files directly at its root, open
PowerShell there instead. The current directory should contain
`Build-Prank.ps1`, the C# sources, and the `Meems` folder.

Build and run:

```powershell
.\Build-Prank.ps1
```

Only after compilation succeeds:

```powershell
.\bin\DoNtStopMePorfavor.exe
```

No NuGet packages or modern .NET SDK are required. The build embeds the supplied
images and creates the controller plus six executables under `bin\Workers`.
For a prepared binary package, copy the **entire built `bin` folder**, not just
the controller. Extract to a guest-local folder before execution.

### If PowerShell blocks the script

Inspect the policy and downloaded-file mark first:

```powershell
Get-ExecutionPolicy -List
Get-Item -LiteralPath .\Build-Prank.ps1 -Stream Zone.Identifier -ErrorAction SilentlyContinue
```

Follow institutional signing policy. If the effective policy is `RemoteSigned`,
you have verified the workshop source, and the restriction is the downloaded-file
mark, explicitly trust only that reviewed build script:

```powershell
Unblock-File -LiteralPath .\Build-Prank.ps1
```

This does not satisfy `AllSigned` or override Group Policy. Do not use a blanket
unblock or execution-policy bypass.

## Automated checks

From the source folder, after building:

```powershell
# Does not install persistence or write marker files:
.\Test-Persistence.ps1

# ONLY inside the disposable VM:
.\Test-Persistence.ps1 -InstallInDisposableVm
```

The explicit VM integration test installs entries, checks duplicate protection,
executes both marker modes (including via Scheduler), and attempts removal in
`finally`. Verify cleanup even if a test fails. It refuses to begin with existing
named lab entries.

These tests do not establish automatic timer/sign-in dispatch or all interactive
GUI behavior. `-DefinitionOnly` provides a smaller check without executable-launch
tests; it is not proof that the executable can run.

## YARA detection

Set the path to your actual extracted YARA executable:

```powershell
$yara = 'C:\Tools\YARA\yara64.exe'
& $yara --version

# Positive control:
& $yara -s .\Workshop.yar .\bin\DoNtStopMePorfavor.exe
"Scan exit code: $LASTEXITCODE"

# Negative control: an unrelated PE:
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
& $yara -s .\Workshop.yar $compiler
"Scan exit code: $LASTEXITCODE"
```

Expected:

- GUI: `CYBRSLC_CDRU_Benign_GUI_Simulator`, with matching strings.
- Compiler: no matches and scan exit code `0`.
- Errors are not successful negative results. Check each scan separately.

The `\x00` bytes displayed in wide-string matches are expected UTF-16LE encoding.
The rule identifies workshop patterns; it does not prove malicious intent.
YARA scans read files without executing them or installing persistence.

**Construction exercise:** build separate baseline/modified copies, change one
fictional display sentence, compare SHA-256 hashes, and scan both. Keep consent,
Stop logic, persistence identifiers, and detection markers unchanged.

## Inspect real persistence in Windows

In the GUI, choose **Persistence lab... > Install (VM only)** and confirm the
separate prompt. Keep the executable at the same path until removal.

Open the inspection tools:

```powershell
Start-Process regedit.exe
Start-Process taskschd.msc
Start-Process 'ms-settings:startupapps'
```

| Artifact | Expected |
| --- | --- |
| Registry key | `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` |
| Value | `CYBRSLC&CDRU Lab Marker` |
| Value data | Quoted full executable path followed by `--lab-marker registry` |
| Scheduled task | Root library: `CYBRSLC&CDRU-Lab-Marker-<current-user SID>` |
| Task action | Same executable with `--lab-marker task` |
| Task security | Current user; interactive token; least privilege; no stored password |
| Timing | First trigger approximately one minute after install; every five minutes while signed in |

Refresh Registry Editor and Task Scheduler to compare before/after state.
Startup Settings or Task Manager may show the startup application, but do not
enumerate all persistence. Inspect the exact Registry value and scheduled task.
Do not edit Windows' TaskCache backing keys.

### Verify automatic execution

Without manually triggering the task, inspect its marker after about one minute
and again five minutes later:

```powershell
Get-Content -LiteralPath "$env:LOCALAPPDATA\CYBRSLC&CDRU\PersistenceLab\task-last-run.txt"
```

Save work, sign out of Windows in the VM, and sign back in. Inspect:

```powershell
Get-Content -LiteralPath "$env:LOCALAPPDATA\CYBRSLC&CDRU\PersistenceLab\registry-last-run.txt"
```

Compare fresh UTC timestamps with the test times. An old file is not proof of a
new run. Locking or disconnecting is not signing out. Startup policy can delay or
prevent execution; preserve failures rather than bypassing restrictions.

Neither marker mode should launch demo windows, workers, or keyboard capture.

## GUI and keyboard acceptance checks

- Confirm six worker PIDs, paths, and READY + RUNNING states after consent.
- Confirm at most five demo popups; a closed popup returns only at replacement.
- Stop one positively identified lab worker; it must not respawn.
- Test tray restore, Stop ALL, and controller termination; workers must exit.
- Real Notepad/Calculator remain user-controlled.
- In **Keyboard lab...**, verify consent defaults off, input-box-only capture,
  focus-loss stop, Clear, explicit export, and empty state after reopening.
- Generate more than 500 character events; exported event count must be 500.
- Never identify a process by its lookalike name alone or use real credentials.

## Defend, remove, and verify

Use the simulator as a defender challenge: identify the process, scan the file,
inspect startup mechanisms, preserve evidence, remove the verified lab artifacts,
and check that execution stops.

**Stop & Exit does not remove persistence.**

1. Stop the demo and verify worker exits.
2. From the same installed executable, choose **Persistence lab... > Remove lab entries**.
3. Independently refresh Registry Editor and Task Scheduler; both exact entries
   must be absent.
4. Allow any in-flight marker run to finish; watch past the next expected trigger
   and confirm timestamps no longer update.
5. Retain approved evidence, then delete only the two named marker files and
   specific dummy exports you created.
6. Restore the clean VM snapshot.

Alternative removal from the source folder:

```powershell
$sample = (Resolve-Path .\bin\DoNtStopMePorfavor.exe).Path
$cleanup = Start-Process -FilePath $sample -ArgumentList '--remove-lab-persistence' -Wait -PassThru
if ($cleanup.ExitCode -ne 0) { throw "Lab cleanup failed: $($cleanup.ExitCode)" }
```

Removal refuses conflicting commands rather than deleting unrelated entries.
Do not delete the whole Run key. If the executable was moved or entries changed,
inspect the exact artifacts or restore the pre-lab snapshot.

**Older builds:** remove `CYBRSLC`-only entries using the old executable before
switching to `CYBRSLC&CDRU`, or restore the clean snapshot. Renamed builds do not
migrate or remove the older entries.

YARA identifies matching files; it does not automatically block or remove them.
Removing these known lab artifacts is not a general guarantee that a real
infection has been eradicated.

## Workshop materials

- [Workshop plan](CYBRSLC_Workshop_Revised.md): 120-minute teaching sequence.
- [Simulator instructions](Simulator/Offline-Chaos-Instructions.txt): current
  behavior, controls, artifact names, and manual acceptance checklists.
- [Build script](Simulator/Build-Prank.ps1) and
  [tests](Simulator/Test-Persistence.ps1).
- [YARA rules](Simulator/Workshop.yar).
- [Controller](Simulator/DoNtStopMePorfavor.cs),
  [persistence helper](Simulator/LabPersistence.cs), and
  [worker](Simulator/LabWorker.cs).

The workshop plan may retain older branding examples. Use the Simulator
instructions and current source for the renamed artifact identifiers.

The older [console sample](Simulator/SimulatedPayload.cs) is optional and separate.
Its `Build.ps1` / `Test-Simulator.ps1` workflow is not the GUI build. Its explicit
HTTP telemetry option is outside the offline core workshop and requires a
separately verified isolated lab service; it is not C2.

## References

- [MITRE ATT&CK: Registry Run Keys / Startup Folder](https://attack.mitre.org/techniques/T1547/001/)
- [MITRE ATT&CK: Scheduled Task](https://attack.mitre.org/techniques/T1053/005/)
- [Process Monitor](https://learn.microsoft.com/en-us/sysinternals/downloads/procmon)
- [Process Explorer](https://learn.microsoft.com/en-us/sysinternals/downloads/process-explorer)
- [YARA documentation](https://yara.readthedocs.io/en/latest/)
- [Official YARA releases](https://github.com/VirusTotal/yara/releases)
- [ILSpy](https://github.com/icsharpcode/ILSpy)
