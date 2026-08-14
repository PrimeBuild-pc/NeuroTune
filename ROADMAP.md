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

## v0.6 — Dynamic Plan Alpha

- [x] Structured AI plans containing executable actions, manual guidance, script artifacts, verified resources, and update notices
- [x] Safe, Balanced, and Aggressive risk policies over one contextual plan
- [ ] Extensible typed capability registry with expanded reversible action coverage
- [x] Optional user-provided game context and performance observations
- [x] Official-source GPU, chipset, and motherboard update advisor foundation
- [x] Unsigned NSIS and portable ZIP assets with SHA-256 checksums
- [ ] MIT-licensed public repository after full-history privacy and secret review

## v0.7 — ETW Measurement Alpha

- [x] Capture named, bounded WPR sessions for an already-running process without CPU sampling or stack walks
- [x] Analyze ISR/DPC, context switches, ReadyThread, per-core residency, migrations, and temporal overlap locally
- [x] Persist versioned sessions atomically and delete raw ETL after successful analysis unless the user opts in
- [x] Add trace quality gates, deterministic observations, and 1+1 exploratory / 3+3 repeated comparisons
- [x] Keep ETL, PID, command lines, and full paths out of optional AI evidence
- [ ] Complete WPR/analyzer smoke validation on Windows 10 build 19045 and Windows 11 build 26200
- [ ] Validate repeatability with DirectX workloads on physical AMD and NVIDIA GPU hosts
- [x] Add read-only CPU/PnP topology and opaque GPU IRQ candidate previews from 3+ valid baselines
- [ ] Gate the first supervised GPU IRQ-affinity experiment on those physical validation reports

## v1.0 — Release Criteria

- [ ] Supported actions pass apply/verify/rollback tests across the support matrix
- [ ] No unresolved critical or high-severity security findings
- [ ] Installer and portable artifacts have documented checksums and reproducible provenance
- [ ] Recovery documentation is validated by users unfamiliar with the project
- [ ] Performance claims are backed by reproducible measurements
- [ ] Accessibility and privacy reviews are complete

## Deliberate Non-Goals

NeuroTune will not execute LLM-generated scripts, accept model-supplied download or write paths, download remote tweak catalogs, delete user files for “cleanup,” disable Defender or Firewall for performance, force HPET/platform timers, or apply undocumented generic network tweaks. Generated scripts may be reviewed and saved for manual use; verified text artifacts require a locally reviewed, hash-pinned definition. DEVICE-TWEAKER remains research input only and is never imported, executed, or copied into the production backend.
