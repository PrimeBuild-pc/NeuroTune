# Alpha validation matrix

## Automated host and VM coverage

| Area | Windows 11 25H2 VM | Windows 10 22H2 VM | Physical hardware |
|---|---|---|---|
| Gen 2, UEFI, Secure Boot, vTPM | required | required | n/a |
| Memory Integrity | enabled and recorded | recorded when available | required before driver experiments |
| NSIS per-machine install, launch, version, Defender, uninstall | passed | passed after clean reinstall | release smoke test |
| Scan cancellation / no orphan process tree | Rust fake-agent test plus VM checklist | same | optional |
| All 12 actions: Inspect, Apply, Verify, exact rollback | passed | passed | repeat before stable release |
| Crash while Applying and Rolling back | passed with deterministic VM-only delay hook, kill, history recovery, rollback | passed with the same harness | not required |
| 100%, 150%, 200% scaling | 100% passed; 150%/200% blocked by Enhanced Session | manual visual pass | manual |
| Keyboard-only, focus visibility, reduced motion, forced colors | manual plus CSS/semantic checks | manual plus CSS/semantic checks | manual |
| SPD/XMP/EXPO, motherboard sensors, temperatures, real HAGS | unavailable | unavailable | required |
| PawnIO install/HVCI/uninstall | deliberately excluded | deliberately excluded | disposable dedicated PC only after approval |

scripts/vm-provision.ps1 creates clean Hyper-V guests and DPAPI-protected credential files. scripts/vm-validation.ps1 restores the clean checkpoint, copies the installer with PowerShell Direct, runs the automated matrix, and writes a redacted JSON report. Neither script prints or commits guest passwords.

Windows 11 25H2 is used because it is the current ISO available in the lab; this is a current replacement for the earlier 24H2 request. Windows 10 remains in the matrix while the README declares Windows 10 support.

The original disposable `NeuroTune-W10` disk/checkpoint chain became unbootable and was replaced with a clean Windows 10 Pro 22H2 installation. After Windows Update and restoring the Hyper-V PowerShell Direct service, the full matrix passed against checkpoint `Clean-NeuroTune-W10-Reinstalled`. The original checkpoints were not overwritten.

The detailed Windows 11 integrity harness then exercised every action separately with deliberately mixed original Registry states (DWORD, QWORD, string, and absent values). All 12 passed Inspect, Apply, Verify, raw read-back, Registry export, apply/rollback restore points, exact value-and-kind rollback, and final manifest validation. The harness restored `Clean-NeuroTune-Alpha2` after completion.

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
