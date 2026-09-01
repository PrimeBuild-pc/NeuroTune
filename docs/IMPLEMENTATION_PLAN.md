# NeuroTune living implementation plan

This is the single persistent engineering plan for NeuroTune. Update it in the
same commit that changes a milestone status. A milestone is complete only when
its listed checks pass and the evidence column identifies a commit, PR, test,
or validation report.

## Current state

| Field | Value |
|---|---|
| Target | Next AI-harness alpha |
| Branch | `codex/ai-optimization-harness-20260830` |
| Current milestone | M9 — physical device-affinity matrix intake |
| Last verified baseline | `5186020`; 60 .NET tests, PowerShell 5.1 collector self-test, and rewritten public branch/tag verification, 2026-09-01 |
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
  collector is complete; third-party reports and benchmark runs remain.
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
  hosts. The shareable collector now gathers redacted GPU/driver, CPU-set, and
  interrupt-policy facts without admin, network access, or system writes; it
  deliberately does not claim performance evidence.
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
