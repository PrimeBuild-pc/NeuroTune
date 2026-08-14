# Alpha validation matrix

## Automated host and VM coverage

| Area | Supported Windows 11 VM | Historical Windows 10 result (unsupported) | Physical hardware |
|---|---|---|---|
| Gen 2, UEFI, Secure Boot, vTPM | required | required | n/a |
| Memory Integrity | enabled and recorded | recorded when available | required before driver experiments |
| v0.6.0-alpha.1 NSIS per-machine install, launch, version, Defender, uninstall | passed on build 26200 | passed on build 19045 after clean reinstall | installer SHA-256 `8E449C97A327E4C5700D602FDCA61102C8A562F72493D59918318D831D142076` |
| v0.6.0-alpha.1 legacy 12-action apply/verify/rollback and crash recovery | passed | passed | high-risk confirmation supplied explicitly by the VM harness |
| v0.6.0-alpha.1 portable ZIP layout, agent response, and UI launch | passed on physical build 26200 | not repeated | complete ZIP contains UI, Agent, Telemetry, license, README, and release notes |
| Scan cancellation / no orphan process tree | Rust fake-agent test plus VM checklist | same | optional |
| All 25 registered actions: Inspect, Capture, Apply, Verify, exact Restore | passed on build 26200 | passed on build 19045 | repeat before stable release |
| Crash while Applying and Rolling back | passed with deterministic VM-only delay hook, kill, history recovery, rollback | passed with the same harness | not required |
| 100%, 150%, 200% scaling | 100% passed; 150%/200% blocked by Enhanced Session | manual visual pass | manual |
| Keyboard-only, focus visibility, reduced motion, forced colors | manual plus CSS/semantic checks | manual plus CSS/semantic checks | manual |
| SPD/XMP/EXPO, motherboard sensors, temperatures, real HAGS | unavailable | unavailable | required |
| PawnIO install/HVCI/uninstall | deliberately excluded | deliberately excluded | disposable dedicated PC only after approval |
| v0.7.0-alpha.1 ETW watchdog, analysis, quality, cleanup | 3/3 valid on build 26200; 0 lost events; no ETL or WPR orphan | not required | physical DirectX AMD/NVIDIA pending |

scripts/vm-provision.ps1 creates clean Hyper-V guests and DPAPI-protected credential files. scripts/vm-validation.ps1 restores the clean checkpoint, copies the installer with PowerShell Direct, runs the automated matrix, and writes a redacted JSON report. Neither script prints or commits guest passwords.

Windows 11 25H2 is used because it is the current ISO available in the lab. As
of v0.7.0-alpha.1, NeuroTune supports only Microsoft-supported Windows 11 x64
builds. Windows 10 entries below are preserved as historical evidence and do
not represent a current support or release gate.

The former disposable `NeuroTune-W10` disk/checkpoint chain became unbootable
and was replaced during v0.6 validation with a clean Windows 10 Pro 22H2
installation. Those results remain reproducibility history only; the VM and
its checkpoint are no longer required or maintained.

The detailed Windows 11 integrity harness then exercised every action separately with deliberately mixed original Registry states (DWORD, QWORD, string, and absent values). All 12 passed Inspect, Apply, Verify, raw read-back, Registry export, apply/rollback restore points, exact value-and-kind rollback, and final manifest validation. The harness restored `Clean-NeuroTune-Alpha2` after completion.

On 2026-08-02 the M2 capability probe repeated the exact-state round trip for
all 25 registered actions, including the new Balanced plan, BCD timer/resource
repair, and complementary
default/on/off states for Game Mode, HAGS, Game DVR, app capture, and visual
effects. Windows 10 Pro 22H2 build 19045 and Windows 11 build 26200 both passed
all 25 cases at that time. A proposed Windows-managed-pagefile writer passed on Windows 10
but failed on Windows 11 build 26200 and was therefore removed from the public
catalog. The Windows 11 VM was temporarily started with 4 GB because the
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
