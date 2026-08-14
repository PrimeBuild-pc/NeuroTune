# NeuroTune living implementation plan

This is the single persistent engineering plan for NeuroTune. Update it in the
same commit that changes a milestone status. A milestone is complete only when
its listed checks pass and the evidence column identifies a commit, PR, test,
or validation report.

## Current state

| Field | Value |
|---|---|
| Target | `v0.7.0-alpha.1` |
| Branch | `main` |
| Current milestone | M9 — GPU IRQ closed-loop validation |
| Last verified baseline | `8810ab5`; Windows 11 build 26200 ETW smoke report, 2026-08-14 |
| Distribution | unsigned NSIS plus portable ZIP, GitHub/Discord |
| License | MIT, copyright PrimeBuild |
| Repository visibility | private until the full-history privacy/secret audit passes |

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
- TraceProcessing 1.12.10 was not selected because its redistribution terms add
  obligations beyond the repository's MIT grant. The stable application
  contracts use the MIT-licensed TraceEvent 3.2.5 fallback.
- Windows 10 is retained only in historical validation evidence. New releases,
  automation, compatibility metadata, and support claims target x64 builds of
  Windows 11 that are still supported by Microsoft.

## Milestones

| ID | Milestone | Status | Acceptance evidence |
|---|---|---|---|
| M0 | Close legacy backlog, add MIT, consolidate planning | Completed | `73ad228`; issues #1–#3 closed 2026-08-02; PR #5 |
| M1 | Structured dynamic-plan contract and goal/measurement context | Completed | `9ec9332`; 21 .NET tests, 7 Vitest tests, UI typecheck/lint/build; PR #5 |
| M2 | Extensible reversible capability registry and first expansion | In progress | `c0acdb2`; 25 actions, 24 .NET tests, exact round-trip on Windows 11 build 26200 plus historical Windows 10 build 19045; PR #5 |
| M3 | Verified artifact catalog and deterministic update advisor | In progress | `a7fa9a6`; empty-by-default catalogs, exact text transaction, official vendor advisor, 29 .NET tests; PR #5 |
| M4 | Plan-focused accessible UI and script/resource review | In progress | `b1d7d11`; five labelled types, inert script copy/save, enforced high-risk confirmation; 30 .NET, 7 UI, 2 Rust tests; manual scaling/Narrator remain; PR #5 |
| M5 | NSIS, portable ZIP, checksums, release documentation | Completed | `8416b69`; NSIS and 64,196,039-byte ZIP, checksums and Windows 11 smoke; historical Windows 10 evidence retained; PR #5 |
| M6 | Full-history secret/privacy audit and public repository | Blocked | `32fd2aa`; no secrets; reachable personal Gmail author metadata requires PrimeBuild accept/rewrite/private decision; PR #5 |
| M7 | Optional imported benchmark evidence and researched sources | Planned | Deferred until the planner and advisor are stable |
| M8 | ETW Measurement Alpha | In validation | `1bf387c`, `8810ab5`; three of three valid watchdog captures on Windows 11 build 26200, zero lost events, no raw ETL or WPR orphan; physical DirectX matrix remains |
| M9 | GPU IRQ closed-loop | In progress | Read-only CPU-set/PnP topology, exact current-policy snapshot, and opaque three-candidate preview implemented; writer, restart, Keep/Rollback, driver matrix, and AI candidate selection remain gated |

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
  Registry view. For the expected Binary/DWord types, preserve existence,
  type, byte length, and hex value locally; reject unexpected types as
  non-restorable and never include the snapshot in provider evidence.
- Keep every candidate `ApplyEnabled=false` until AMD/NVIDIA driver fixtures,
  exact capture/verify/restore, restart handling, and the physical-host matrix
  pass. Only then add the supervised writer and AI selection by candidate ID.
- Run `scripts/physical-gpu-measurement.ps1` against a repeatable DirectX scene
  to collect three quality-gated Baselines and a redacted read-only candidate
  report for each validated GPU/driver combination.

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
