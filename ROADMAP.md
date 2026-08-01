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

## v0.3 — Native Web UI and Open Providers

- [x] Replace the WPF shell with Tauri 2 and React
- [x] Add semantic light and dark themes with Windows synchronization and manual override
- [x] Add automated WCAG AA contrast checks for shipped design tokens
- [x] Add native DeepSeek, custom OpenAI-compatible, custom Anthropic-compatible, and local model connections
- [x] Add Ollama, LM Studio, and vLLM local endpoint presets
- [x] Add official OpenRouter browser authorization with PKCE
- [x] Keep the Windows safety engine isolated in a local .NET agent
- [ ] Validate appearance at 100%, 150%, and 200% Windows scaling ([#1](../../issues/1))

## v0.4 — Evidence and Compatibility

- [x] Collect curated performance Registry state, hardware capabilities, and local policy conflicts
- [x] Add user goals for games/workloads and frame-rate, system-latency, network, balanced, or efficiency priorities
- [x] Show only diagnosis-backed actions with Select all, Select safe only, explicit approval, and printable reports
- [x] Require model findings to cite observed profile values and keep execution on the compiled allowlist
- [ ] Publish a support matrix by Windows build, GPU family, and driver capability
- [ ] Add repeatable pre/post workload measurements instead of synthetic “health scores”
- [ ] Require an evidence note and rollback test for every new optimization
- [ ] Add exportable operation reports with privacy review
- [ ] Improve offline behavior and provider-specific error guidance
- [ ] Add restore-point and rollback integration tests in disposable VMs

## v0.5 — Deep Inventory and Conflict Graph

- [x] Expand the versioned evidence bundle across Registry, BCD, drivers, devices, software, filters, firmware, memory, and runtime state
- [x] Stream real scan-phase progress from the local agent to the Tauri interface
- [x] Add a deterministic conflict graph with exact setting/value pairs, confidence, objective impact, and rationale
- [x] Add native SMBIOS/WMI/CPUID firmware and DIMM inspection with explicitly labelled XMP/DOCP/EXPO heuristics
- [x] Let users review AI-recommended, conflict-related, or all supported reversible actions regardless of risk
- [x] Add reversible high-risk repair actions for TDR and legacy global TCP overrides
- [x] Add request-scoped cancellation that terminates the matching agent process tree and discards partial scans
- [x] Extract 83 Registry probes into a typed, local catalog and report evidence size/privacy classes
- [x] Expand deterministic timer, VPN/filter, TDR/tuning, page-file, mobile-power, memory, and driver/device relationships
- [x] Add exact, versioned offline CPU and memory baselines with explicit `baseline unavailable` fallback
- [x] Expose a read-only low-level telemetry support matrix without installing or loading a driver
- [ ] Add a reviewed optional PawnIO/LibreHardwareMonitor telemetry adapter without silent driver installation ([#3](../../issues/3))

## v0.6 — Distribution Beta

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
