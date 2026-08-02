# NeuroTune living implementation plan

This is the single persistent engineering plan for NeuroTune. Update it in the
same commit that changes a milestone status. A milestone is complete only when
its listed checks pass and the evidence column identifies a commit, PR, test,
or validation report.

## Current state

| Field | Value |
|---|---|
| Target | `v0.6.0-alpha.1` |
| Branch | `codex/dynamic-planner-foundation` |
| Current milestone | M2 — capability registry |
| Last verified baseline | `64d74bb` / PR #4 |
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

## Milestones

| ID | Milestone | Status | Acceptance evidence |
|---|---|---|---|
| M0 | Close legacy backlog, add MIT, consolidate planning | Completed | Issues #1–#3 closed 2026-08-02; MIT and this living plan added in the M0 commit |
| M1 | Structured dynamic-plan contract and goal/measurement context | Completed | M1 commit (this milestone commit): 21 .NET tests; 7 Vitest tests; UI typecheck, lint, and production build |
| M2 | Extensible reversible capability registry and first expansion | In progress | M2 foundation commit (this milestone commit): 25 actions, 24 .NET tests, exact round-trip on Windows builds 19045 and 26200 |
| M3 | Verified artifact catalog and deterministic update advisor | Planned | Host/hash/path/version fixtures and no arbitrary URL/file execution |
| M4 | Plan-focused accessible UI and script/resource review | Planned | Vitest, typecheck, lint, keyboard/announcement checks |
| M5 | NSIS, portable ZIP, checksums, release documentation | Planned | Release build and asset-layout/checksum verification |
| M6 | Full-history secret/privacy audit and public repository | Planned | Audit report contains no unresolved secret or personal-data finding |
| M7 | Optional imported benchmark evidence and researched sources | Planned | Deferred until the planner and advisor are stable |

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
  timer/resource-limit repair. All 25 pass the disposable Windows 10/11 probe.
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

New system writers additionally require the disposable Windows 10/11
Inspect/Apply/Verify/Rollback matrix. Installer and portable assets require a
final launch/uninstall or extract/launch smoke check before tagging.

## Update protocol

1. Change the current milestone and status before implementation begins.
2. Record design decisions under Locked product decisions.
3. Mark completion only after the required checks pass.
4. Replace evidence placeholders with PR, commit, test, or report references.
5. Set the next incomplete milestone as Current milestone before ending a work
   session.
