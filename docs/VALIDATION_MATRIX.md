# Alpha validation matrix

## Automated host and VM coverage

| Area | Supported Windows 11 VM | Historical Windows 10 result (unsupported) | Physical hardware |
|---|---|---|---|
| Gen 2, UEFI, Secure Boot, vTPM | required | required | n/a |
| Memory Integrity | enabled and recorded | recorded when available | required before driver experiments |
| v0.7.0-alpha.1 NSIS per-machine install, agent version, Defender, uninstall | passed on build 26200 | not repeated; v0.6 historical pass only | installer SHA-256 `8D33E53E0CDEBA56C09303C8E082F1BB68DDEDD25AAD06D96F80D1F829B79465` |
| v0.7.0-alpha.1 run-aware apply/verify/rollback and interrupted Apply/Rollback recovery | passed | not repeated; v0.6 historical pass only | explicit `OptimizationRun`/`runId`; deterministic VM-only BaselineReady fixture; not a 3+3 quality-gate test |
| v0.6.0-alpha.1 portable ZIP layout, agent response, and UI launch | passed on physical build 26200 | not repeated | complete ZIP contains UI, Agent, Telemetry, license, README, and release notes |
| Scan cancellation / no orphan process tree | Rust fake-agent test plus VM checklist | same | optional |
| Writer integrity: Inspect, Capture, Apply, Verify, exact Restore | 16 targeted actions passed, including page file, core parking, and per-app GPU high/default | historical legacy pass | every-current-action sweep and per-app GPU power-saving remain open before stable release |
| Crash while Applying and Rolling back | passed with deterministic VM-only delay hook, kill, history recovery, rollback | passed with the same harness | not required |
| 100%, 150%, 200% scaling | 100% passed; 150%/200% blocked by Enhanced Session | manual visual pass | manual |
| Keyboard-only, focus visibility, reduced motion, forced colors | manual plus CSS/semantic checks | manual plus CSS/semantic checks | manual |
| SPD/XMP/EXPO, motherboard sensors, temperatures, real HAGS | unavailable | unavailable | required |
| PawnIO install/HVCI/uninstall | deliberately excluded | deliberately excluded | disposable dedicated PC only after approval |
| v0.7.0-alpha.1 ETW watchdog, analysis, quality, cleanup | 3/3 valid on build 26200; 0 lost events; no ETL or WPR orphan | not required | redacted physical DirectX harness available; AMD/NVIDIA runs pending |

scripts/vm-provision.ps1 creates clean Hyper-V guests and DPAPI-protected credential files. scripts/vm-validation.ps1 normally restores the clean checkpoint, copies the installer with PowerShell Direct, runs the automated matrix, and writes a redacted JSON report. `-SkipCheckpointRestore` is available for a supervised, already-running existing guest; the harness then restores every state it seeds instead of changing the checkpoint chain. Neither script prints or commits guest passwords.

Windows 11 25H2 is used because it is the current ISO available in the lab. As
of v0.7.0-alpha.1, NeuroTune supports only Microsoft-supported Windows 11 x64
builds. Windows 10 entries below are preserved as historical evidence and do
not represent a current support or release gate.

The former disposable `NeuroTune-W10` disk/checkpoint chain became unbootable
and was replaced during v0.6 validation with a clean Windows 10 Pro 22H2
installation. Those results remain reproducibility history only; the VM and
its checkpoint are no longer required or maintained.

On 2026-08-31 the recovered in-place `NeuroTune-W11` guest on `D:` passed the
v0.7 run-aware validation without copying the VM or modifying its checkpoint
chain. `docs/validation/v0.7.0-alpha.1-windows11-writer-integrity.json`
records 16 passed targeted
actions and no failures, including Windows-managed page-file sizes, AC core
parking, and dynamic per-app GPU high/default preferences. Each case passed
Inspect, Apply, Verify, raw read-back, restore-point lookup, exact rollback,
and terminal manifest checks.
`docs/validation/v0.7.0-alpha.1-windows11-recovery.json` records successful
install/version, run-aware apply/rollback, interrupted Apply and Rollback
recovery, Defender, PawnIO absence, HVCI configuration, and uninstall.
The writer harness asserts that its deterministic VM-only journal fixture
reaches `BaselineReady`; it does not claim to validate a real ETW Baseline or
the repeated 3+3 Keep gate, which remain separate tests.

On 2026-08-02 the M2 capability probe repeated the exact-state round trip for
all 25 registered actions, including the new Balanced plan, BCD timer/resource
repair, and complementary
default/on/off states for Game Mode, HAGS, Game DVR, app capture, and visual
effects. Windows 10 Pro 22H2 build 19045 and Windows 11 build 26200 both passed
all 25 cases at that time. That historical result predates the current
Windows-managed page-file, core-parking, and dynamic per-app GPU writers and does
not replace the current v0.7 evidence above. The Windows 11 VM was temporarily started with 4 GB because the
host disk could not allocate its 8 GB runtime-state file; the runner then shut
it down and verified that its original 8 GB startup setting was restored.

## Manual accessibility acceptance

Recorded Windows 11 result on 2026-08-02: the 100% scale pass completed without overlap or clipping. Windows blocked 150% and 200% changes because Hyper-V Connect was using an Enhanced/remote session, so those two results are not claimed. High Contrast and Reduced Motion remain optional manual follow-ups.

At 100%, 150%, and 200% display scale:

1. Resize to the minimum 720×600 window and confirm no control is unreachable.
2. Traverse every page with Tab/Shift+Tab and activate controls with Space/Enter.
3. Confirm the current navigation page is announced and focus is always visible.
4. Enable Windows Always show scrollbars, Reduced Motion, and a High Contrast theme.
5. Confirm status is never communicated by color alone and notices are announced.
6. Print the review report and confirm interactive controls are omitted.

VM automation cannot validate real sensor correctness, GPU scheduling on a passed-through GPU, or motherboard firmware state.
