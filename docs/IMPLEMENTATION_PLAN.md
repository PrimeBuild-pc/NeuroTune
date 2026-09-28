# NeuroTune living implementation plan

This is the single persistent engineering plan for NeuroTune. Update it in the
same commit that changes a milestone status. A milestone is complete only when
its listed checks pass and the evidence column identifies a commit, PR, test,
or validation report.

## Current state

| Field | Value |
|---|---|
| Target | Next AI-harness alpha |
| Branch | `feat/system-scan-custom-power-plans` |
| Current milestone | M11/M12 — local system latency capture and BIOS read capability implementation; M9 writer remains gated |
| Last verified baseline | `5186020`; 60 .NET tests, PowerShell 5.1 collector self-test, and rewritten public branch/tag verification, 2026-09-01 |
| Latest working-tree validation | 2026-09-28: after the watchdog-pipe fix, 70 .NET, 10 UI and 2 Rust tests passed with .NET format/build, UI typecheck/lint/build, Rust fmt/clippy and native Tauri Release build; privileged Start/live/Cancel smoke remains |
| Distribution | unsigned NSIS plus portable ZIP, GitHub/Discord |
| License | MIT, copyright PrimeBuild |
| Repository visibility | public; approved history rewrite is an active privacy gate |

## AI optimization harness TODO

NeuroTune's core product is an AI harness for evidence-led gaming optimization,
not a generic tweak pack with an AI summary. The model chooses bounded next
steps and explains hypotheses; local typed code owns inspection, approval,
execution, verification, rollback, and measurement quality.

### P0 — closed-loop run contract

- [x] Persist one `OptimizationRun` linking goals, sanitized system evidence,
  diagnosis, requested probes, approved actions, measurement sessions,
  comparisons, decisions, and recovery state.
- [x] Enforce a state machine for Scan → Diagnose/propose → Measure baseline →
  Approve → Apply → optional verified restart → Measure candidate → Evaluate →
  Keep/Rollback. P1 may later split hypothesis and proposal into separate turns.
- [x] Resume safely after restart or process failure without repeating a write.
- [x] Stabilize the Tauri process-tree cancellation test and prove no child
  process survives cancellation.

### P1 — bounded AI tool loop

- [x] Replace the single-pass diagnosis-only flow with a bounded multi-turn
  planner that may request only registered read-only probes and measurements.
- [x] Keep every write behind a local capability ID, compatibility inspection,
  explicit approval, backup, verification, and rollback.
- [x] Record every model request, accepted/rejected tool proposal, evidence ID,
  and stop reason without storing credentials or raw private evidence.
- [x] Add deterministic limits for turns, payload size, repeated requests, and
  provider failure; fall back to the local conflict graph.

### P2 — measurement-led gaming evaluation

- [x] Make ETW Baseline/Candidate sessions part of the same optimization run
  and generate an automatic Keep/Rollback recommendation from valid repeated
  comparisons.
- [x] Add local PresentMon-compatible frame-time evidence for FPS, 1% low,
  stutter, and present mode without sending raw traces to a provider.
- [x] Detect and reject changed workloads, hardware, configuration, duration,
  thermal state, or invalid capture quality before claiming a result.
- [x] Keep input-latency claims manual/unverified unless supported measurement
  hardware is explicitly integrated.

### P3 — detected gaming and hardware context

- [x] Detect installed launchers, games, executables, graphics API signals,
  per-game Windows GPU preferences, display topology, and active GPU mapping.
- [x] Add a reviewed optional low-level telemetry adapter for temperatures,
  clocks, throttling, utilization, power, and memory-profile evidence without
  silent driver installation.
- [x] Expand exact component baselines and return `baseline unavailable` for
  every unmatched CPU, GPU, DIMM, board, BIOS, or driver.
- [x] Keep official version comparison deterministic offline: injected exact/error
  fixtures and `ComparisonUnavailable` fallback; never infer an update from an
  unpinned web result.

### P4 — reversible capability and recipe coverage

- [x] Complete typed per-app GPU targets, Windows 11-version-qualified
  Windows-managed page-file handling, and platform-qualified power/core-parking
  capabilities.
- [x] Surface overlay/startup conflicts contextually, but keep intervention
  manual and the per-game recipe catalog empty until exact detection,
  compatibility, destination, and rollback exist.
- [x] Keep repository PowerShell files as developer validation tooling rather
  than bundled optimizations; executable optimizations use typed Inspect,
  Capture, Apply, Verify, and Restore capabilities, while model-generated
  scripts remain inert artifacts.
- [x] Keep GPU IRQ affinity read-only until the AMD/NVIDIA physical matrix,
  restart/resume flow, exact restore, and supervised Keep/Rollback pass.

### P5 — validation and release gates

- [ ] Pass every supported writer through disposable Windows 11
  Inspect/Apply/Verify/Rollback and interrupted-operation recovery.
- [ ] Complete repeated physical DirectX validation on supported AMD and
  NVIDIA hosts and publish the scoped support matrix. The read-only fleet
  collector is complete; four third-party inventory reports are catalogued
  in [the fleet matrix](VALIDATION_MATRIX.md#amd-nvidia-fleet-intake).
  Repeated physical benchmark and rollback runs remain.
- [ ] Complete 100/150/200% scaling, keyboard-only, Narrator, and
  forced-colors checks under supervised Windows runs.
- [x] Complete automated privacy, inert export-report, offline-provider, and
  recovery-documentation checks; keep manual visual acceptance separate.
- [x] Complete the approved full-history author-metadata rewrite and verify
  every remote branch/tag before public visibility.
- [x] Claim performance gains only from reproducible, quality-gated repeated
  measurements tied to the exact run and machine configuration.

### Current validation blockers

- The existing `NeuroTune-W11` VM on `D:` was repaired and registered in place
  without copying its disks or modifying its AVHDX/checkpoint chain. On Windows
  11 build 26200, 16 targeted writer round trips passed, including page file,
  core parking, and per-app GPU high/default; interrupted Apply and Rollback
  recovery also passed with the exact run ID. P5 remains open for the complete
  current-action sweep and per-app GPU power-saving case.
- Physical repeated DirectX validation still needs supported AMD and NVIDIA
  runs. Inventory intake now covers RX 7900 XT, RTX 5060 Ti, RTX 5080, and
  RTX 3070 on an Intel i5-10600K/Z490 fixture;
  the RTX 5080 already has a configured GPU interrupt policy. These offline
  reports establish test cases, not latency, effective routing, or a gain.
- Scaling, Narrator, forced-colors, keyboard-only, and final recovery UX checks
  require supervised manual runs. Static UI contract now exposes selected
  provider/theme state, labelled icon-only destructive actions, live status
  feedback, inert text export, and no executable artifact path; UI tests,
  typecheck, lint, and production build pass.
- Branches and tags now contain only noreply identities and the `main` ruleset
  was restored after the coordinated force-push. GitHub's read-only heads for
  merged PRs #4–#9 still retain old commit objects; only GitHub Support can
  dereference those PRs and purge cached views.

## Locked product decisions

- Product scope includes replacing LatencyMon's diagnostic workflow and then
  optimizing from measured evidence: monitor → attribute → propose → approve
  → change → remeasure → keep/restore. M11 closes measurement and remediation
  gaps; M12 adds board-specific BIOS guidance and optional supported access.
  Existing ETW support is partial coverage, not a claim of LatencyMon parity.
- NeuroTune builds a contextual plan from the local profile, game/workload,
  user objective, symptoms, and optional user-provided measurements. It does
  not ship generic optimization packs.
- The model may return executable capabilities, manual guidance, script
  artifacts, verified external resources, and update notices.
- Only locally registered, typed, reversible capabilities can execute.
  Model-generated scripts can be reviewed, copied, or saved but never run by
  NeuroTune.
- Safe, Balanced, and Aggressive are selection/confirmation policies over the
  same contextual plan, not fixed tweak profiles.
- External CFG/TXT/patch automation requires a PrimeBuild-reviewed local entry
  with a fixed source, SHA-256, compatibility rules, backup, verification, and
  rollback. No executable, driver, firmware, or model-supplied URL is accepted.
- Driver, chipset, and BIOS advice uses exact hardware identity and official
  sources. NeuroTune links to manual updates and never installs or flashes them.
  BIOS setting changes are a separate planned M12 capability: optional reads,
  separately enabled writes through a documented supported OEM interface,
  explicit per-change approval, and hardware-specific recovery validation.
  Enabling access does not authorize arbitrary firmware writes or flashing.
- Defender, Firewall, UAC, and forced HPET/platform-timer changes are not
  executable performance capabilities. A future VBS/HVCI capability may be
  Aggressive only after dedicated capture, rollback, and validation.
- Automated game benchmarking is deferred. Measurements supplied by the user
  are explicitly labelled unverified input.
- ETW measurement follows Baseline → trace → local analysis → comparison →
  hypothesis. Raw ETL never reaches a provider and is deleted after successful
  analysis unless the user explicitly keeps it.
- The first measurement release is read-only. GPU IRQ affinity and every later
  device writer remain gated on repeated physical-host validation and exact
  rollback evidence.
- Driver/device affinity means Windows interrupt routing. Service affinity is
  process affinity, may affect a shared service host, and is not persisted or
  automated until NeuroTune can prove a dedicated process and repeatable gain.
- The fleet collector is source-visible, offline, no-admin, and read-only. It
  exports no user/computer name, serial, MAC/IP, full path, Registry path, raw
  PnP instance ID, stable cross-report device key, or interrupt-mask value.
- DEVICE-TWEAKER is design input only. NeuroTune does not import its backend,
  force MSI mode, change RSS/NDIS, use RWEverything, or write PCI state.
- TraceProcessing 1.12.10 was not selected because its redistribution terms add
  obligations beyond the repository's MIT grant. The stable application
  contracts use the MIT-licensed TraceEvent 3.2.5 fallback.
- Windows 10 is retained only in historical validation evidence. New releases,
  automation, compatibility metadata, and support claims target x64 builds of
  Windows 11 that are still supported by Microsoft.

## Milestones

| ID | Milestone | Status | Acceptance evidence |
|---|---|---|---|
| M0 | Close legacy backlog, add MIT, consolidate planning | Completed | `112eb5c`; issues #1–#3 closed 2026-08-02; PR #5 |
| M1 | Structured dynamic-plan contract and goal/measurement context | Completed | `61da21a`; 21 .NET tests, 7 Vitest tests, UI typecheck/lint/build; PR #5 |
| M2 | Extensible reversible capability registry and first expansion | In progress | `4ef8abb`; 25 actions, 24 .NET tests, exact round-trip on Windows 11 build 26200 plus historical Windows 10 build 19045; PR #5 |
| M3 | Verified artifact catalog and deterministic update advisor | In progress | `976936a`; empty-by-default catalogs, exact text transaction, official vendor advisor, 29 .NET tests; PR #5 |
| M4 | Plan-focused accessible UI and script/resource review | In progress | `1720ce3`; five labelled types, inert script copy/save, enforced high-risk confirmation; 30 .NET, 7 UI, 2 Rust tests; manual scaling/Narrator remain; PR #5 |
| M5 | NSIS, portable ZIP, checksums, release documentation | Completed | `97bb259`; NSIS and 64,196,039-byte ZIP, checksums and Windows 11 smoke; historical Windows 10 evidence retained; PR #5 |
| M6 | Full-history secret/privacy audit and public repository | In progress | Branches/tags clean and force-updated; ruleset restored; six GitHub-managed historical PR refs require a Support purge |
| M7 | Optional imported benchmark evidence and researched sources | Planned | Deferred until the planner and advisor are stable |
| M8 | ETW Measurement Alpha | In validation | `45746f2`, `868bf94`; three of three valid watchdog captures on Windows 11 build 26200, zero lost events, no raw ETL or WPR orphan; physical DirectX matrix remains |
| M9 | GPU IRQ closed-loop | In progress | `5186020`; read-only CPU-set/PnP topology, exact current-policy snapshot, opaque three-candidate GPU preview, and shareable redacted fleet collector implemented; writer, restart, Keep/Rollback, driver matrix, and AI candidate selection remain gated |
| M10 | AI optimization harness | In validation | `10e3044`, PR #10; CI passed before coordinated rewrite and retriggered afterward; 60 .NET tests, 7 UI tests, 2 Rust tests, Release builds, lint/typecheck, unsigned NSIS/portable ZIP/checksums, committed Windows 11 writer/recovery reports; independent Claude Code reviews via Herdr |
| M11 | LatencyMon replacement and measured system remediation | In progress | 2026-09-28: system-wide WPR mode, live per-core Windows counters, complete interrupt aggregates, corrected completion timestamps/overlap, kernel image rundown, hard pagefaults and UI implemented; 70 .NET/10 UI/2 Rust tests and native Release pass; interrupt-to-process probe, causal attribution and writers remain |
| M12 | BIOS guidance and optional OEM settings access | In progress | 2026-09-28: opt-in local firmware inspection and scan consent implemented and gated; MSI read interface detected on MS-7C37 BIOS 1.S1, but the read probe returned no setup value; exact settings/menu support and writes remain unavailable |

## M8 — ETW Measurement Alpha

- Select an already-running process and record for 30–600 seconds (180 by
  default) using one named, globally serialized WPR session.
- Capture only process/thread, loader, CSwitch, ReadyThread, ISR/DPC, and CPU
  metadata in memory mode. Do not enable stack walk or sampled profiling.
- Persist session state atomically under
  `%LocalAppData%\NeuroTune\measurements\<session-id>` and let an internal,
  non-UI-callable watchdog stop the recording at its deadline.
- Analyze locally with nearest-rank percentiles and an interval sweep. Unknown
  module/thread identities stay `Unknown`; reports make no causal claim.
- Reject comparisons across executables, hardware/configuration fingerprints,
  durations outside ±10%, invalid quality gates, or lost critical events.
- Keep `PerformanceSnapshotService` as general observation only; its WMI CPU,
  RAM, process count, ping, and power-plan values are not benchmark proof.
- After analyzer validation, open a separate milestone for supervised GPU IRQ
  affinity. No MSI, RWEverything, PCI writes, secret executables, or imported
  DEVICE-TWEAKER backend code enters M8.

## M9 — GPU IRQ closed-loop

- Read CPU group, physical core, SMT index, efficiency class, and last-level
  cache cluster from the native Windows CPU-set API. Never relabel a cache
  cluster as a CCD.
- Map physical PCI AMD/NVIDIA GPUs to local PnP identity, driver version, and
  the derived affinity-policy Registry location. These identifiers remain
  local and are not added to provider evidence.
- From at least three valid matching Baselines, rank at most three distinct
  physical cores by median interrupt share, target residency, and Ready/IRQ
  overlap. Expose opaque `candidateId` values and validated masks as a
  read-only preview.
- Inspect `AssignmentSetOverride` and `DevicePolicy` through the 64-bit local
  Registry view. Preserve the documented Binary/DWord/QWord affinity-mask
  forms plus the DWord policy with exact existence, type, byte length, and hex
  value locally; reject unexpected types as non-restorable and never include
  the snapshot in provider evidence.
- Keep every candidate `ApplyEnabled=false` until AMD/NVIDIA driver fixtures,
  exact capture/verify/restore, restart handling, and the physical-host matrix
  pass. Only then add the supervised writer and AI selection by candidate ID.
- Run `scripts/physical-gpu-measurement.ps1` against a repeatable DirectX scene
  to collect three quality-gated Baselines and a redacted read-only candidate
  report for each validated GPU/driver combination.
- Use `tools/hardware-collector` to gather the broader AMD/NVIDIA and device
  inventory first. Fleet JSON establishes which fixtures to build; it cannot
  unlock a writer without repeated Baseline/Candidate performance evidence.
- Intake on 2026-09-07 adds two NVIDIA reports to the existing AMD report,
  organized by GPU vendor under `docs/validation/hardware-fleet`. Prioritize
  default-policy and existing one-byte Binary-mask restore cases; see the
  [fleet matrix](VALIDATION_MATRIX.md#amd-nvidia-fleet-intake) for exact identities,
  hashes, limitations, and outstanding physical checks.

## M11 — LatencyMon replacement and measured system remediation

Reuse `MeasurementService`, `TraceAnalyzer`, `HardwareTopologyService`, the
planner's registered probes, `OptimizationRun`, and the reversible action
registry. Extend their versioned evidence and UI; do not build a second engine.

| Requirement | Current evidence | Remaining acceptance |
|---|---|---|
| Start/stop system monitoring, optionally focus a workload | System-wide mode without a target, 30–600 s deadline, watchdog/status polling and live per-core counter summaries | Quantify observer overhead and broader physical workload coverage; system-wide snapshots deliberately cannot satisfy optimization Baselines |
| Time spent on every logical processor, grouped by physical core | All trace processors, scheduled busy/idle ms and separate DPC/ISR distributions; live Windows busy/idle/DPC/ISR counter deltas use the actual sample window and group/LP identity | Validate multiple processor groups and physical-core grouping in the report; no-switch scheduling intervals stay Unavailable; scheduled time and interrupt time are not additive |
| DPC/ISR spikes by driver | Complete per-module/core aggregates with paginated UI and the 50 longest interrupts; kernel image rundown fixes previously Unknown modules; end timestamps and nested-overlap union corrected | Driver version/device mapping and causal attribution; retain Unknown for unresolvable modules |
| Interrupt-to-user-process latency | Not implemented; scheduler Ready Time is a different metric | Implement and validate a bounded dedicated probe with current/max/distribution; expose Unavailable until measured, including timer/stall diagnostic limitations |
| Hard pagefaults | WPR HardFaults enabled; count/rate, resolution totals/percentiles/maxima per captured PID with local process names; legacy captures explicitly Unavailable | Full process-lifetime separation and hard-fault timeline correlation with memory/storage pressure and affected workloads |
| Driver/device/service attribution | Static inventory and temporal observations exist | Link spike evidence to devices, driver stacks, process lifetime, and hosted services; a framework module or shared host alone is insufficient to name a culprit |
| Evidence-led affinity and remediation | GPU candidate ranking exists, always `ApplyEnabled=false` | Complete M9; add separately validated process/dedicated-service affinity and device/service actions only when exact targeting and rollback exist |

The reference coverage comes from [Resplendence's measurement guide](https://www.resplendence.com/latencymon_using)
and [interrupt-to-process methodology](https://www.resplendence.com/latencymon_interrupt2process).
This is functional coverage, not identical instrumentation or interchangeable
numbers. Validate instrument differences on repeated matched workloads before
claiming parity; report unsupported measurements explicitly.

The current implementation uses Windows ETW and the already installed TraceEvent
library; it does not automate or redistribute LatencyMon. No documented public
LatencyMon telemetry API was found in the reviewed official material. See
[local validation](LOCAL_LATENCY_VALIDATION.md) for captured results and remaining
limits. The analyzer rejects ETL larger than 512 MiB or more than two million
interrupts / one million hard faults rather than silently truncating statistics.
Comparisons reject mixed analyzer versions, use metrics present in every run,
and never recommend Keep with no improvement or a reproducible regression.
Interrupt-share redistribution no longer counts as a performance improvement.

- [ ] Show the user the measured problem, affected core/driver/process,
  hypothesis, proposed change, expected tradeoff, and verification workload.
  Do not treat one maximum spike or the least occupied core as sufficient proof.
- [ ] Rank affinity using repeated core occupancy, Ready Time, DPC/ISR burden,
  workload contention, and topology (SMT siblings, processor groups, efficiency
  class, cache/NUMA). IRQ routing and process affinity stay distinct actions.
- [ ] Add device power/configuration or non-critical service interventions
  incrementally through Inspect/Capture/Apply/Verify/Restore. Check dependencies
  and dedicated process identity before service changes; never pin an entire
  shared service host on behalf of one service. Driver updates remain official
  manual guidance unless a separately reviewed installation capability exists.
- [ ] Use the existing run to test one hypothesis at a time with 3+3 matched
  Baseline/Candidate captures, restart/resume where needed, and exact rollback.
  Keep requires a repeatable gain without workload, stability, thermal, or
  functional regression; inconclusive results must not become automatic Keep.
- [ ] Add deterministic trace checks for unit conversions, totals/window
  boundaries, Unknown attribution, process reuse/shared hosts, hard faults,
  missing/lost events, and CPU groups. Validate physical audio, gaming, network,
  storage, and USB workloads; zero events can be valid idle evidence and must
  be distinguished from a missing provider. Keep raw paths/traces local.

## M12 — BIOS guidance and optional OEM settings access

- [x] Add off-by-default BIOS reading consent, an on-demand local inspection,
  and consent-aware firmware collection during scans. Show MSI provider
  detection separately from usable settings and expose no write command.
- [ ] Offer separate settings for BIOS/UEFI reading and writing, with advanced
  access off by default. Reading permission never enables writing; writes also
  require reads, a supported exact platform, and approval of the concrete diff.
  Align existing automatic SMBIOS inventory with the user's read preference.
- [ ] Detect board vendor/model/revision and BIOS version, then use exact
  official manuals to explain applicable enable/disable choices, menu paths,
  expected effects, and how to undo them. Mark current settings Unknown unless
  actually read; label user-entered values and heuristics. No universal list of
  BIOS tweaks or blanket disabling of C-states, SMT, or security features.
- [ ] Reuse `SystemProfiler` and the official-source advisor. Add setting reads
  only through documented OEM interfaces qualified by board/firmware version;
  unsupported machines retain manual guidance. SMBIOS identity is not a dump
  of BIOS setup values ([Win32_BIOS](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-bios)).
- [ ] Evaluate the first setting writer only on hardware with a documented OEM
  interface, readable original state, allowed values, exact restore and a tested
  recovery procedure if Windows cannot boot. Generic
  [UEFI variable writes](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setfirmwareenvironmentvariablew)
  are not a universal BIOS setup API. Do not invent NVRAM offsets or flash BIOS.
- [ ] Test read disabled, read only, write unsupported, permission/authentication
  failure, firmware-version mismatch, reboot readback, rollback, and failed-boot
  recovery. A Windows restore point alone does not establish firmware recovery.
  Never collect BIOS passwords into logs/provider evidence. Keep a setting only
  after the same repeated workload evaluation used for Windows changes.

## M1 — dynamic plan foundation

- Add typed game context: game, version, launcher, graphics API, resolution,
  refresh rate, display mode, VRR, V-Sync, frame cap, symptoms, and constraints.
- Add optional user-provided average FPS, 1% low, frame time, input/network
  latency, packet loss, and notes with bounded numeric validation.
- Replace the single recommendation shape with `ExecutableAction`,
  `ManualGuidance`, `ScriptArtifact`, `ExternalResource`, and `UpdateNotice`.
- Require evidence IDs for executable items; validate referenced action and
  resource IDs locally. Bound all text, scripts, references, and item counts.
- Add deterministic Safe/Balanced/Aggressive selection policy. High-risk items
  always retain a separate explicit confirmation.
- Ensure there is no Tauri or agent command that can execute a script artifact.

## M2 — capability registry

- Separate immutable action metadata from `Inspect/Capture/Apply/Verify/Restore`
  implementations while keeping manifest-schema-v2 history readable.
- Require unique IDs, supported build/hardware notes, source/evidence notes,
  side effects, risk, restart requirements, and a policy decision.
- Expand in validated batches: default/on/off gaming and graphics state; power
  plans and throttling; per-app GPU preference; Windows-managed page file;
  memory/MPO/TDR/TCP repairs; removal of manual BCD timer/resource overrides;
  documented core-parking/power settings. Do not force HPET.
- Keep VBS/HVCI disabled from the public registry until its dedicated VM and
  physical-host rollback matrix exists.
- Implemented first validated batch: the original 12 actions, Balanced power,
  default/on/off state families for gaming/capture/visual settings, and BCD
  timer/resource-limit repair. All 25 passed the Windows 11 probe; the earlier
  Windows 10 pass is retained only as historical evidence.
- Remaining before M2 completion: typed per-app GPU targets, a cross-version
  page-file backend, and platform-qualified core-parking/power definitions.
  The first WMI page-file writer was removed after Windows 11 rejected it.

## M3 — external intelligence

- Add an initially empty external-app catalog and a text-only artifact catalog.
- Resolve destinations only from known templates or a local user picker;
  reject traversal, reparse escapes, unsupported extensions, size mismatch,
  content mismatch, non-HTTPS sources, host mismatch, and hash mismatch.
- Add exact vendor adapters for NVIDIA/AMD/Intel GPU drivers, AMD/Intel chipset,
  and MSI/ASUS/Gigabyte/ASRock motherboard support. If an exact comparison is
  unavailable, provide only the official support link and say so.
- Implemented the catalog and transaction guardrails: canonical HTTPS URL,
  exact content type/size/SHA-256, strict UTF-8, `.cfg`/`.txt`/`.patch` only,
  bounded destination/reparse checks, atomic replacement, exact backup, verify,
  and restore. Both artifact and external-application catalogs remain empty.
- Implemented deterministic vendor recognition and official support links for
  NVIDIA, AMD, Intel, MSI, ASUS, Gigabyte, and ASRock. `UpdateAvailable` is
  emitted only for an exact model plus a pinned numeric version record;
  otherwise the UI explicitly reports `ComparisonUnavailable`.
- Remaining before M3 completion: reviewed version-feed adapters and fixtures
  for changed pages/offline behavior, plus the first user-approved artifact.

## M4–M7 — product and release

- Present one contextual plan with clear visual and accessible separation
  between NeuroTune actions, manual guidance, unverified scripts, verified
  resources, and official update notices.
- Keep manual metrics editable and marked as user-provided; add file imports
  only in M7 and never auto-launch a game.
- Produce the unsigned per-machine NSIS installer, a complete portable ZIP,
  and `SHA256SUMS` in CI. Keep signing/Store work out of the roadmap.
- Before public visibility, scan current files and every Git object for keys,
  VM credentials, unredacted profiles, usernames, device names, and personal
  paths. Stop for explicit rotation/history-rewrite approval on any real hit.

## Required checks

```powershell
dotnet format NeuroTune.sln --no-restore
dotnet build NeuroTune.sln -c Release --no-restore
dotnet test NeuroTune.sln -c Release --no-build
dotnet list NeuroTune.sln package --vulnerable --include-transitive
cd ui
npm test
npm run typecheck
npm run lint
npm run build
cd src-tauri
cargo fmt --check
cargo clippy -- -D warnings
cargo test
```

New system writers additionally require the disposable Windows 11
Inspect/Apply/Verify/Rollback matrix. Historical Windows 10 results do not
expand the current support claim. Installer and portable assets require a
final launch/uninstall or extract/launch smoke check before tagging.

## Update protocol

1. Change the current milestone and status before implementation begins.
2. Record design decisions under Locked product decisions.
3. Mark completion only after the required checks pass.
4. Replace evidence placeholders with PR, commit, test, or report references.
5. Set the next incomplete milestone as Current milestone before ending a work
   session.
