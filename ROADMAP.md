# NeuroTune Roadmap

NeuroTune is developed safety-first: a feature is not considered complete merely because it can change Windows. It must also detect compatibility, explain the impact, verify the result, and restore the previous state.

## v0.2 — Guided Alpha

- [x] Guided Setup → Scan → Review → Apply → Restore workflow
- [x] Provider connection test and dynamic model discovery
- [x] Local-only scan before any data is sent
- [x] Per-action compatibility and current-state checks
- [x] Mandatory, verified System Restore point
- [x] Per-action journal with crash recovery warning
- [x] Verified reverse-order rollback
- [x] Before/after observational telemetry
- [x] English product UI and clearer safety messaging
- [ ] End-to-end validation on clean Windows 10 and Windows 11 virtual machines ([#2](../../issues/2))
- [ ] Accessibility pass with keyboard-only and screen-reader testing ([#1](../../issues/1))

## v0.3 — Evidence and Compatibility

- [ ] Publish a support matrix by Windows build, GPU family, and driver capability
- [ ] Add repeatable pre/post workload measurements instead of synthetic “health scores”
- [ ] Require an evidence note and rollback test for every new optimization
- [ ] Add exportable diagnostic and operation reports with privacy review
- [ ] Improve offline behavior and provider-specific error guidance
- [ ] Add restore-point and rollback integration tests in disposable VMs

## v0.4 — Distribution Beta

- [ ] Signed MSIX or installer with clean uninstall behavior
- [ ] Code-signed binaries and documented checksum verification
- [ ] Opt-in update checks with signed release metadata
- [ ] First-run onboarding and System Protection readiness checks
- [ ] Crash reporting that is local by default and exportable by the user
- [ ] Public beta feedback template and compatibility issue workflow

## v1.0 — Release Criteria

- [ ] Supported actions pass apply/verify/rollback tests across the support matrix
- [ ] No unresolved critical or high-severity security findings
- [ ] Installer, executable, and update metadata are signed
- [ ] Recovery documentation is validated by users unfamiliar with the project
- [ ] Performance claims are backed by reproducible measurements
- [ ] Accessibility and privacy reviews are complete

## Deliberate Non-Goals

NeuroTune will not execute LLM-generated scripts, download remote tweak catalogs, delete user files for “cleanup,” disable security controls for performance, or apply undocumented generic network tweaks.
