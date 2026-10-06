# Alpha Test Guide

Use a disposable Windows virtual machine. Do not use a primary PC for the first validation cycle.
This is a procedure, not proof that every step passed. Bind acceptance to one
final commit/installer SHA-256; old v0.7 and pre-hardening v0.8 results are historical.
See [the current gates](VALIDATION_MATRIX.md#current-candidate-not-release-certified).
Provider calls, optional downloads/drivers and system-changing tests require
explicit authorization; routine local checks do not grant it.

## Prerequisites

- A currently supported Windows 11 build, x64
- A VM checkpoint created outside the guest operating system, unless a
  supervised, already-running existing guest is run with
  `-SkipCheckpointRestore`
- System Protection enabled on the Windows drive
- Administrator access
- A low-value test API key, an OpenRouter account, or a disposable local model endpoint

## 1. Setup

1. Start NeuroTune and approve the administrator prompt.
2. Verify **Use Windows setting**, **Light**, and **Dark** in Settings; change the Windows app theme and confirm System follows it.
3. Select a provider, enter its API key, and choose **Test & discover models**.
4. For OpenRouter, also test browser sign-in and confirm the loopback success page returns control to NeuroTune.
5. For **ChatGPT plan**, test official browser registration, eligible plan consent, identity-only refusal, account-specific model names, first-login acknowledgement and one explicitly requested diagnosis. Confirm no API-key fallback. Retest saved-client login, adding another account/workspace, credential renewal, quota and permission failures, sign-out and restart. Validate only in an authorized account; local JWT/SSE tests do not prove live plan access.
6. For optional SCEWIN, use only an explicitly approved, trustworthy local package on compatible firmware. Verify hash inspection, approval invalidation after folder changes, rejection without consent/elevation, and blocked export during active recording. After approval, validate `/o /s` export, unknown/conflicting settings and cleanup; never disable security features to make it pass. Prefer offline `nvram.txt` import to test parsing without drivers. See [scope and risk](SCEWIN_SYSTEM_ONE.md).
7. Test one custom HTTPS endpoint and one local Ollama, LM Studio, or vLLM endpoint.
8. Confirm that the model list loads and the selected model persists after restarting NeuroTune.
9. Inspect `%LocalAppData%\NeuroTune`: credential files must not contain readable plaintext.

### Startup and decorative motion

- Cold-start the packaged app on Windows: the main window starts hidden with a dark native WebView background and is shown after page load. A static inline branded scene covers module/CSS loading; no blank white intermediate page should appear. This native presentation needs desktop acceptance, not just a browser preview.
- Delay/fail local initialization: the startup scene must reflect real local loading, release on error and expose the error in the usable app. It must not wait for optional ChatGPT model discovery, fake percentages or a minimum splash duration.
- Check finite logo/path reveal, restrained idle orbit motion, card entrances, hover/press feedback and keyboard focus at 150% scaling in light/dark mode. No new animation library or video asset is required.
- With reduced motion, all decorative animations/transitions are disabled. With a capture already recording at startup, they stay disabled; before automated/manual WPR startup they freeze until capture finalization/cancellation is observed. Navigating away from an active manual capture conservatively keeps motion frozen until another authoritative refresh.
- Run `node artifacts/check-startup-motion.cjs` against a local preview to check pre-JS presentation, readiness/error release, nonblocking discovery, capture freeze, reduced motion, keyboard access and narrow layout.

### Instrumented native UI / WPR smoke

Use an isolated diagnostic Release build; never distribute it with a debugger.
From the repository root in an elevated PowerShell, after approving the normal
Windows UAC prompt:

```powershell
$config = Get-Content ui/src-tauri/tauri.conf.json -Raw | ConvertFrom-Json
$config.app.windows[0] | Add-Member -NotePropertyName additionalBrowserArgs -NotePropertyValue '--remote-debugging-address=127.0.0.1 --remote-debugging-port=9224' -Force
$config.app.windows[0] | Add-Member -NotePropertyName dataDirectory -NotePropertyValue 'ui-polish-native-validation' -Force
New-Item artifacts -ItemType Directory -Force | Out-Null
$config | ConvertTo-Json -Depth 32 | Set-Content artifacts/native-polish.conf.json
Push-Location ui
npm run tauri -- build --no-bundle --config ../artifacts/native-polish.conf.json
Pop-Location
$app = Start-Process (Resolve-Path ui/src-tauri/target/release/neurotune.exe) -PassThru
# Requires Playwright; use PLAYWRIGHT_PATH if installed outside this repository.
node scripts/check-native-ui.cjs
# Separate opt-in: real system-wide watchdog/analysis/Stop/Cancel smoke.
$env:NATIVE_CAPTURE = '1'
try { node scripts/check-native-ui.cjs } finally { Remove-Item Env:NATIVE_CAPTURE }
```

The native checker attaches only to `http://tauri.localhost/`, preserves the
real immutable command bridge, refuses an existing recording, does not select
another provider or request inference, and cleans up only its own capture IDs.
It validates static/quiet motion, actual trace quality and raw-trace deletion,
not benchmark gains or overhead. Manual Stop/Cancel use a 120-second watchdog
budget but end early; that shortened trace is not a benchmark. The normal
watchdog smoke records 30 seconds. The result includes tested Agent/frontend
hashes in `artifacts/native-polish-validation.json`.

Close only the test instance after confirming recording has ended, check
`wpr -status` and absence of orphan Agent/Telemetry processes, then rebuild the
**production** installer without the override before packaging:

```powershell
$app.CloseMainWindow()
$app.WaitForExit()
wpr -status
Push-Location ui
npm run tauri -- build --bundles nsis
Pop-Location
./scripts/package-release.ps1
```

Rebuilding clears diagnostic browser arguments; the isolated WebView directory
does not replace the user's normal UI preferences. Logical viewport emulation
inside WebView2 does not substitute for changing actual Windows DPI or testing
Narrator/cold-start frame continuity. Do not run installer/writer validation on
the primary PC; installation/uninstallation and recovery still belong in a VM.

## 2. Local Scan and Privacy

1. Open **Advanced tools → Local evidence → Scan this PC** without requesting an AI diagnosis.
2. Confirm that live phase progress appears for hardware/firmware, Windows/Registry, network/devices, software, and services.
3. During a second scan, select **Cancel scan** and confirm the UI treats it as informational, keeps no partial profile, and leaves no agent, `powercfg`, `netsh`, WMI, or other probe process running.
4. Confirm that CPU, GPU, motherboard, DIMMs, BCD, drivers, applications, device issues, and all 83 Registry facts are populated. BIOS-specific details remain omitted until firmware reading is enabled in step 11.
5. Review the sanitized JSON, fact count, UTF-8 payload size/limit, telemetry support matrix, exact component baselines, and local conflicts.
6. Confirm that unknown components report `baseline unavailable` rather than a nearest-model guess.
7. Confirm that the Windows username, device name, serial numbers, and MAC addresses do not appear.
8. Confirm that no provider request occurs until **Run AI diagnosis** is selected.

## 3. Diagnosis and Review

1. Open **Diagnosi completa**, choose goals and a running game/application, keep a repeatable scene active and start once. Collection, the ten-second preparation period, three captures and ETL quality checks must run automatically before either assistant/provider is invoked. Do not mistake scheduling/interrupt measurements for FPS or end-to-end input latency.
2. Test the no-workload path: one system-wide snapshot may support a diagnostic AI investigation, but cannot unlock automatic apply. Invalid/mismatched workload baselines stop before provider transmission.
3. Verify actual phases, elapsed time, recorder-readiness countdown, trace count, bounded native logs and no percentage progress. Cancel during scan, recorder startup, recording, trace analysis and provider work; only the owned recording may be cancelled. Completed reports remain local; app closure leaves watchdog finalization/recovery available under advanced measurements.
4. With an authorized provider, verify a model-requested read-only follow-up, its real tool/result log, time/limitations, original citations and persisted application-owned investigation IDs. Unsupported readers must report unavailability, never run arbitrary commands/queries/paths.
5. Inspect the final risk-ordered proposal: evidence, expected benefit, trade-offs, prerequisites, uncertainty, reversibility and manual/script proposals outside the executable catalog. Local heuristics are separately labelled and challengeable, not imposed memory-policy conclusions. Unknown executor IDs downgrade to nonexecuting manual guidance; provider failure is not presented as a completed deterministic plan.
6. Select individual items, all executable recommendations or none. Unavailable actions remain disabled; high risk requires separate consent. **Finish without changes** must close only a prewrite run and permit a new diagnosis; it cannot dismiss an operation or pending recovery.
7. Change/save investigation limits under provider settings (default 12 turns/10 minutes; bounds 2–32 turns/1–30 minutes). Confirm elapsed budget cancellation/turn exhaustion is explicit, and the consented UI provider/model is the one actually used. No provider/API fallback is automatic.
8. Print the report and verify that navigation and execution controls are omitted. Run `node artifacts/check-complete-diagnosis.cjs` against a local preview for mocked sequencing/progress/decline/keyboard/reduced-motion/narrow-viewport acceptance; this is not elevated hardware or live-inference acceptance.

### Prompt-specialized analysis presets

- Check **Performance complessive**, **Latenza del sistema**, **Ottimizzazione rete**, **Stabilità del sistema**, **Privacy e sicurezza Windows** in both complete and advanced diagnosis. Selected helper text explains investigation focus, not automatic tweaks; preserve notes/workload and risk independently. Keyboard/150%/reduced-motion/narrow view remain usable.
- For each focus, run the mocked planner-loop tests: every turn includes the selected application-owned focus plus the same authority, uncertainty and evidence rules. Readers remain available across domains. Goals survive run save/load; recommendations/approved IDs are not prepopulated by the preset.
- Check latency/overall prompts do not treat scheduling as input latency/FPS, network does not invent ping/jitter/path tests, and stability does not automatically stress-test, repair or reset the PC. Comparison explanation uses focus but cannot replace numerical metrics/decisions.
- Restore saved FPS/efficiency runs without converting their IDs/goals; their labelled legacy selection remains visible. Reject invalid objective/risk enums. A changed current UI goal must not bypass persisted matching-goal checks.
- Run `node artifacts/check-complete-diagnosis.cjs` against a local preview for four-option selection, helper descriptions, same-goal request forwarding, keyboard and no-write mocked acceptance. This does not prove live AI compliance or hardware improvements.

### Privacy/security audit and separate Defender operations

Read [scope, sources and limits](PRIVACY_SECURITY.md) first. Routine checks:
`dotnet test tests/NeuroTune.Tests --filter 'FullyQualifiedName~PrivacySecurityTests|FullyQualifiedName~AuditChecklistTests|FullyQualifiedName~PlannerLoopTests'`,
`cd ui; npm test`, and `node scripts/check-privacy-security.cjs` against a local
preview (Playwright installed or `PLAYWRIGHT_PATH` configured). The browser
checker mocks **every** agent command, including scans and provider work.

Mandatory coverage source tests exercise weak/malformed model output, budgeted
repair, missing/partial/unavailable checks, forged consent and sources, imported
reports, bounded task projections and restarted/legacy journal validation.
Reports must show all 15 checks in five areas before AI conclusions; unknown,
empty, truncated or uncited observations cannot become verified evidence.
Verify print, light/dark, source IDs and hostile-text escaping with browser mocks.
Current reports always leave antivirus-scan unverified because scan operations
are not linked to audit evidence: command return/history does not satisfy it.
TronScript is not registered or executable, and selecting the preset must remain
inert. Do not run actual startup/task readers, providers, scans or cleanup tools
for routine source tests; validate native module/projection behavior only in the
separately authorized VM procedure below.

In an explicitly authorized **disposable Windows 11 VM**, bound to the rebuilt
Agent/frontend/installer hashes:

1. Selecting the privacy objective must preserve the selected investigation
   mode, goals and unchecked scanner consents. Independently choose audit-only:
   no workload selector, warmup, WPR start, ETL analysis, Baseline or Apply.
   Verify persisted/restarted audit mode and backend rejection of approval,
   Apply and linked measurements; measured privacy runs keep all existing gates.
2. Review the prepared evidence before provider consent. Confirm no threat
   resource/exclusion paths, process command lines, account IDs or event messages
   leave the reader. Check Unknown/unavailable, missing registry values, policy
   versus preference, edition/build/MDM and different elevated-user contexts.
   Complete audit must pause on the prepared sanitized-evidence preview before
   any provider request; cancel sends nothing and continuing requires an explicit
   second consent. Advanced audit uses its existing local preview and diagnosis
   consent. Model/provider calls require independent authorization; use mocks
   otherwise.
3. Read Defender status without scanning. Check Normal/passive/alternative AV,
   disabled/unavailable/module failure, busy/unknown state, outdated signatures,
   more than 40 detections, and empty history. None certifies a clean PC.
4. For separately consented quick/full scans, require resource, remediation and
   network/cloud/sample consents **plus** final confirmation. Cancelling that
   confirmation must launch nothing. Observe actual Windows Security start/end
   and scan coverage versus command return; latest-event idle may be stale.
   Do not weaken protections/exclusions or claim cancellation from app closure.
5. Interrupt/close the client and restart: the protected pre-command journal
   must prevent repeating an uncertain scan. Acknowledgment requires reviewing
   Windows Security and idle evidence; a live owned scanner must reject it.
   Verify scan/recorder-start concurrency, long scans/deadlines, command failure,
   busy state after return, and recording/measured-run refusal without cancelling
   an existing workload or antivirus task. Never kill an unrelated AV service.
6. Verify scan-journal owner/ACL and medium-integrity read/write/rename/deletion
   denial under `%ProgramData%\\NeuroTune-journals\\<SID>\\defender-scans`; malformed
   or unsafe/reparse journals fail closed before scanning. Mock ACL/schema tests
   do not replace real token/installation acceptance.
7. Remediation remains Windows Security → Protection history. EICAR testing
   requires **additional explicit authorization** and isolation; never use real
   malware. Observe configured quarantine/sample behavior and disclose data loss
   and unsupported reversal. Do not infer maliciousness from unsigned metadata.
8. Check optional Autoruns/Sigcheck links and unverified, redacted text imports;
   NeuroTune must not download/run them, accept EULAs, query VirusTotal or upload
   files. Test light/dark, real DPI, keyboard, Narrator and native app restart.

No live scanner, quarantine, VM ACL, native UI or installer acceptance has been
performed for this feature; source/mock checks do not replace live acceptance.

### Optional supporting files

- Add CPU-Z/GPU-Z/HWiNFO-style TXT/HTML/CSV and UTF-16 exports under **Report e screenshot di supporto**. Check inert editable text, best-effort sensitive-labelled-line omission and prepared-content SHA-256. Edits invalidate approval and need **Aggiorna anteprima locale**; removing all files restores the optional no-file path. No HTML/script, external link, program or driver is executed.
- Check 8-file/4-image caps, 512-KiB raw reports, 40,000 characters per report / 80,000 total, empty/binary/invalid encodings, paths/executables/PDF/archive rejection and no silent truncation. Mandatory evidence must not be discarded to make optional reports fit.
- Add PNG/JPEG screenshots. Check source size/dimension limits before decode, canvas re-encoding, actual prepared preview readability and metadata removal. Image pixels are not anonymized; require both preview/privacy consent and selected-model vision/cost confirmation before Start. Source images may be cropped externally before upload.
- Validate Responses `input_image`, OpenAI-compatible `image_url` and Anthropic base64 image blocks through unit tests; do not substitute another model/provider or silently drop images on failure. Live vision acceptance/billing requires separately authorized provider calls.
- In the final report, check `support:*` IDs and user-supplied/unverified labels. Visual interpretation must not mint verified measurement IDs or unlock baselines. Report text/file metadata/hash persist; screenshot base64 pixels must not be present in run journals. A recovered request without matching attachments fails explicitly and can be dismissed before a new diagnosis.
- Run `node artifacts/check-supporting-files.cjs` with a local preview and packaged agent for real local preparation/normalization plus mocked principal inference/recording. Check attachment/vision consent remains independent from the analysis preset.

## 4. Apply and Roll Back

1. Create an Optimization Run, record three matching quality-valid Baselines,
   and select one low-risk approved action.
2. Apply it and confirm that NeuroTune reports a completed operation linked to
   the same run ID.
3. Verify that the operation directory contains `manifest.json` and Registry exports where applicable.
4. Confirm that a restore point with the NeuroTune operation ID exists in Windows.
5. If the run requires a restart, restart Windows and verify the restart gate
   before continuing.
6. Record three matching quality-valid Candidates and compare the exact 3+3
   sessions. Confirm drift or invalid quality blocks the decision gate.
7. Review the automatic recommendation, choose **Keep candidate**, and confirm
   `run-keep` completes only after the accepted repeated comparison.
8. With no other active run, select the kept operation in Activity & Restore
   and run rollback using its original run ID.
9. Confirm that the action returns to its original state, the manifest reports
   **Rollback completed**, and the kept run is terminal with a Rollback decision.
10. Repeat with a second run and choose Rollback directly at the decision gate.

### Security/recovery release checks

- Journal location: `%ProgramData%\NeuroTune-journals\<user SID>\operations` and `runs`, not LocalAppData. Verify owner and ACL on root, SID directory, children and replaced files; use the linked medium-integrity token to prove read/write/rename/deletion is denied. A user-owned pre-created root or reparse path must fail closed without ACL repair.
- On an upgrade VM, retain a pending legacy journal: reads/new writes must stop with an explicit manual-review message. Recover using the previous build and archive only reviewed legacy data; never promote editable old JSON to trusted recovery state.
- Reject unknown schemas, malformed/duplicate/missing Registry/BCD fields, invalid value kinds, unknown actions, contradictory state flags and journal ID/directory mismatch before any writer.
- `scripts/vm-action-integrity.ps1` now removes the GPU cache before rollback and changes the active scheme during core-parking recovery. Its deterministic run journal is a **VM-only writer fixture**, not a validated measurement. Its elevated replacement preserves protected ACLs and a valid transition history.
- Separately stage an approved `.pow` in the VM, apply with consent, delete the staged file, restart NeuroTune and restore the original active scheme and target existence without the source file.
- Test I/O failure and interruption before/after capture, durable flush, apply and inverse restore. Process kill is not equivalent to sudden power loss; capture both limitations explicitly.
- Rebuild one exact installer from the final tree; previous binaries/checksums and old VM results do not cover these changes. See [security and upgrade safety](../SECURITY.md) and [current release gates](VALIDATION_MATRIX.md#current-candidate-not-release-certified).

## 5. Recovery

In a disposable VM only, terminate NeuroTune while an operation is marked
**Applying**. Restart it, reconcile the same run ID, and confirm that the
recovery path identifies the interrupted journal. Retry rollback with the same
run and operation IDs, then verify the matching operation alone was restored.
Repeat while **Rolling back** and confirm the retry is idempotent.

## 6. ETW Measurements

1. Open **Measurements**. For diagnostics, leave **Monitor the entire system**
   enabled. For workload comparisons, disable it, start a workload yourself,
   refresh the process list, and select that already-running process.
2. Record a 30-second Baseline. Close the UI during a second capture and verify
   that the internal watchdog still saves it at the deadline without leaving a
   named WPR session active.
3. Test Stop, Cancel, and analysis cancellation. Cancel must delete incomplete
   capture data; analysis cancellation must leave the ETL retryable.
4. Confirm the report separates ISR and DPC, shows Ready Time, running time,
   migrations, per-core residency/interrupt share, and explicit trace quality.
5. With **Keep raw ETL** off, confirm `capture.etl` disappears after successful
   analysis. With it on, confirm the file stays local.
6. Create one Baseline and one Candidate to confirm an Exploratory comparison;
   then create 3+3 valid sessions to confirm median aggregation and the
   Improvement/Regression/Inconclusive rule.
7. Opt one completed report into the next AI diagnosis. Inspect the provider
   payload and verify it contains only `measurement:*` IDs with numeric/boolean
   values—never ETL bytes, PID, command line, username, or full path.
8. Repeat the smoke test on supported Windows 11 builds used for release.
   Record WPR orphan checks and lost-event counts. Physical DirectX validation
   on AMD and NVIDIA hosts is mandatory before enabling any GPU action.
9. Select at least three valid Baselines and generate the GPU IRQ preview.
   Confirm it returns at most three distinct physical cores, uses the Windows
   group/SMT/efficiency/cache-cluster labels verbatim, and exposes no Apply
   control. Confirm WPR reports no active session before and after this step.
10. Confirm live per-core counters include group/LP and the actual sample
    window; after analysis verify busy/idle ms, separate DPC/ISR distributions,
    all driver pages, the 50-longest-event timeline and hard pagefaults.
    Legacy reports must label unavailable counters/fault tracing rather than
    display a measured zero. System-wide captures cannot unlock a writer.
11. In **Settings → BIOS / UEFI inspection**, verify that reading is initially
    disabled. Opt in and read firmware, then run a new scan. Opt out and verify
    a subsequent scan omits BIOS details. Existing saved reports stay intact.
    The presence of MSI WMI methods must not be presented as readable settings
    or writable firmware.

For unattended, read-only physical desktop diagnostics (no game required):

```powershell
.\scripts\local-latency-validation.ps1 -AgentDirectory .\ui\src-tauri\target\release\agent -DurationSeconds 30 -Count 3
```

The report records binary/profile hashes, actual capture quality, all per-core
and driver aggregates, hard-fault summary, live counter samples and firmware
read support. These background snapshots do not replace the DirectX 3+3 matrix.

## 7. Optional managed System One (separate consent)

1. Confirm default-off startup performs no model download/inference. All original provider choices remain unchanged.
2. For the cloud alternative, choose OpenRouter API, save a separate test key, load/select a model and explicitly enable cloud/API-cost consent. Verify the main provider/key/model is unchanged, no runtime is downloaded, scores are absent, failures preserve main evidence, and deleting the auxiliary key leaves main credentials intact. Actual API requests/charges require an authorized test key.
3. Explicitly install 1.7B or 4B from Settings on a disposable supported Windows x64 host with sufficient disk/RAM. Verify actual byte progress, immutable artifact checks, private Python and locked dependencies, then the real local inference smoke test before enabling. This is a multi-GB download, not part of routine unit tests.
4. Cancel mid-download and resume; remove a partial installation without losing measurements, provider settings or credentials. Close the app mid-install and confirm its agent/worker tree stops. Failed checksum, bootstrap or smoke test must leave the component disabled.
5. Launch the app as administrator and verify the actual child token is medium-integrity/non-admin. If de-elevation fails it must refuse to run. Confirm only selected environment entries/stdio handles are inherited. This is not a filesystem/network sandbox.
6. Exercise diagnosis, comparison and apply/error annotation. Check uncalibrated topic/abstention notes, preserved original facts/goals, and unchanged registered action/approval/transaction state. Kill the optional worker and confirm main-provider diagnosis still works with original evidence.
7. Start a capture while optional inference is pending: startup must serialize, with no worker alive during recording. Try optional install/remove/inference during capture; heavy activity must be rejected/skipped. Confirm normal exit, deadline, cancellation and app close leave no resident model/server or descendants.
8. Record latency including cold startup, RAM/VRAM, CPU idle, held-out accuracy/abstention and actual saved remote planning turns. Do not claim acceleration from upstream figures.
9. After a valid comparison, select **Ask selected AI to explain**. Confirm explicit provider transmission of aggregate metrics/goals, no raw ETL/PID/path, no API billing fallback, and no change in numerical recommendation, metrics, approval or decision. Provider failure must leave the original comparison available.

See [component scope and current validation ceiling](SCEWIN_SYSTEM_ONE.md) and [deterministic-control audit](DETERMINISTIC_CONTROLS.md). Full installation/inference, elevated-token validation and speed/accuracy measurements are still manual release checks, not demonstrated by unit tests.

## Reporting

Include the Windows build, hardware/VM configuration, provider, selected action IDs, operation status, and redacted log lines. Never include an API key or an unredacted system profile.

## Hyper-V automation

From an elevated host PowerShell run scripts/vm-provision.ps1, then run scripts/vm-validation.ps1 with InstallerPath set to the generated NSIS installer.

For the read-only ETW pass, publish the agent and run the following from an elevated host PowerShell while the disposable Windows 11 VM is already running:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\vm-measurement-smoke.ps1 -AgentDirectory .\ui\src-tauri\agent
```

This records and analyzes three 30-second Baselines, exercises the independent watchdog, validates trace quality and read-only GPU previews, then deletes its sessions and temporary guest files. It does not restore checkpoints or change VM power state. The redacted result is written to `artifacts/vm-measurement-smoke.json`.

For a physical AMD/NVIDIA DirectX pass, close the NeuroTune UI, start the game,
and keep a repeatable scene running. From an elevated PowerShell use its
current PID:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\physical-gpu-measurement.ps1 -AgentDirectory .\ui\src-tauri\agent -ProcessId (Get-Process RDR2).Id -GraphicsApi DirectX12
```

The harness records three Baselines, rejects lost or incomplete traces,
inspects the current GPU IRQ policy, verifies candidates remain read-only,
retains completed sessions in history, and writes a redacted report plus a
complete local report to `artifacts/physical-gpu-measurement-<timestamp>.json`
and `.json.local.json`. Cleanup touches only session IDs created by this
invocation; incomplete sessions are cancelled/deleted. Use `-GpuName` when the host exposes
more than one physical AMD/NVIDIA adapter. A successful run validates only
that exact GPU, driver, game, tester-declared graphics API, and Windows build.
Graphics API defaults to `Unverified`; capture success alone does not verify
the scene/API or any optimization benefit. For guided selection, run
`scripts/Avvia-misure-gioco.cmd` as administrator with the game already open.
The script asks for the process and scene/settings, then allows 30 seconds
to return to the game.

For the slower per-action integrity pass, run scripts/vm-action-integrity.ps1
with InstallerPath set to the same final installer. It creates real restore
points and validates mixed Registry value kinds, absent values, page-file
multi-strings, core-parking power settings, and dynamic per-app GPU preferences.
By default it restores the selected clean Hyper-V checkpoint in a finally
block. Use `-SkipCheckpointRestore` only for a supervised existing guest; in
that mode the script captures and restores each seeded state directly.

Provisioning refuses to overwrite an existing VM or VM directory. Validation
restores `Clean-NeuroTune-Alpha2` by default, so use it only with the disposable
`NeuroTune-W11` guest created for this project. Passing
`-SkipCheckpointRestore` never creates, deletes, or merges checkpoints.

The 2026-10-04 v0.8 installer-only attempt in the existing `NeuroTune-W11`
guest was blocked by rejected saved credentials before any installation.
Healthy VM heartbeat/lock screen is not an installer pass. The test restored
its own checkpoint, removed it, verified the original off state/resources/disk
inventory and reclaimed temporary storage. Do not reset passwords, disable
protections, repeatedly retry rejected credentials, or restore an unrelated
baseline to force this gate. Use valid guest credentials and repeat install,
installed-binary/resource checks, process smoke, Defender, uninstall and retained
user-data verification. Current local failure/cleanup evidence is in
`artifacts/vm-install-uninstall.json`; v0.7 results below are historical only.

The 2026-08-31 automated Windows 11 reports cover installation, 16 targeted
writer round trips (including page file, core parking, and per-app GPU
high/default), interrupted Apply and Rollback recovery, orphan-process checks,
Defender, PawnIO absence, HVCI configuration, and clean uninstall. The complete
current-action sweep, per-app GPU power-saving case, scaling, keyboard
navigation, forced colors, physical sensors, SPD/XMP/EXPO, and real-GPU HAGS
remain manual or physical-hardware checks.
