# Issue #3 Follow-up Implementation Plan

This document is the hand-off for continuing [issue #3](https://github.com/PrimeBuild-pc/NeuroTune/issues/3) in a new session. Start from `main` after commit `857f5d5` and the `v0.5.0-alpha.1` prerelease.

## Current Baseline

NeuroTune v0.5 already provides:

- a schema-v3 system profile and flattened, redacted evidence bundle;
- 83 vetted Registry probes;
- five streamed scan phases;
- native WMI/SMBIOS/CPUID inventory for hardware, firmware, DIMMs, drivers, devices, storage, network, software, services, BCD, filters, and runtime state;
- explicit heuristic DDR4 XMP/DOCP detection above 3200 MT/s;
- a deterministic objective-aware conflict graph;
- exact evidence ID/value validation for model findings and recommendations;
- AI-recommended, conflict-fix, and all-supported action views;
- stronger confirmation without hiding supported high-risk actions;
- reversible grouped TDR and legacy TCP repairs;
- an eight-minute provider timeout without artificial delay.

A local smoke scan produced more than 500 evidence facts and multiple exact conflict patterns. PawnIO was detected on the development PC, but the application does not use or install it.

## Non-Negotiable Boundaries

1. Model output never becomes a command, script, Registry path/value, firmware write, or downloadable action.
2. Every writable action remains compiled locally and implements inspect, capture, apply, verify, and restore.
3. Risk affects warnings and confirmation, not whether a supported action is visible.
4. A detected parameter may remain read-only when no reliable reversible implementation exists.
5. Firmware uncertainty must be explicit. Never infer factory values, XMP/EXPO, PBO, timings, or voltage from insufficient evidence.
6. Never silently install a kernel driver. Driver setup requires a separate informed-consent step, pinned artifact hash, signature/license review, uninstall path, and compatibility check for Memory Integrity.
7. Do not lengthen analysis with sleeps. Longer duration must come from real probes, samples, validation, or model passes.

## Important Files

| Area | File |
|---|---|
| Profile schema and conflicts | `src/NeuroTune.Core/Models.cs` |
| Windows probes and sanitization | `src/NeuroTune.Core/SystemProfiler.cs` |
| Deterministic relationship rules | `src/NeuroTune.Core/ConflictAnalyzer.cs` |
| Evidence flattening and model validation | `src/NeuroTune.Core/LlmClient.cs` |
| Reversible action implementations | `src/NeuroTune.Core/OptimizationCatalog.cs` |
| Agent commands | `src/NeuroTune.Agent/Program.cs` |
| Tauri process bridge/progress | `ui/src-tauri/src/lib.rs` |
| Scan, diagnosis, conflicts, and action UI | `ui/src/App.tsx`, `ui/src/App.css` |
| Core checks | `tests/NeuroTune.Tests/CoreTests.cs` |

## Remaining Work

### 1. In-Phase Cancellation

The current Tauri bridge streams stderr phase names but waits for the agent process to exit.

Recommended minimum implementation:

1. Give each invocation a request ID.
2. Store active child processes in Tauri state.
3. Add a restricted `cancel-agent` command that can terminate only the matching NeuroTune.Agent child.
4. Make the Scan button become **Cancel scan** while active.
5. Treat cancellation as a non-error state and discard partial profiles.
6. Confirm that no `powercfg`, `netsh`, WMI, or other child process remains after cancellation.

Do not add a general process-kill API.

### 2. Optional Low-Level Telemetry Adapter

Native Windows APIs cannot reliably expose complete SPD/XMP/EXPO profiles, memory timings, PBO state, SMU limits, effective clocks, or motherboard sensor values.

Before adding a dependency:

1. Compare the stable `LibreHardwareMonitorLib` package with current upstream PawnIO support. Do not accidentally reintroduce an old vulnerable Ring0 driver.
2. Review PawnIO, PawnIOLib, module licenses, signatures, setup behavior, and IOCTL boundary.
3. Prefer a separate optional telemetry process so driver/library licensing and failure cannot compromise the optimization agent.
4. Keep the adapter read-only in this milestone.
5. Return capability records such as `supported`, `unavailable`, `blocked-by-HVCI`, or `driver-not-approved` instead of zero/default values.
6. Sample effective clocks, temperature, power, throttling flags, and limits over a short documented interval; do not run a stress test without separate consent.
7. Support only explicitly validated CPU/motherboard families and expose the support matrix in the UI.

PawnIO presence on one development system must not become an installation assumption.

### 3. Trustworthy Factory Baselines

Factory-vs-current comparison requires exact component identification and trustworthy reference data.

- CPU: use CPUID family/model/stepping and vendor specification identifiers.
- Memory: use manufacturer and part number only when they are non-empty and stable.
- GPU: use PCI vendor/device/subsystem IDs and VBIOS where exposed.
- Motherboard: use manufacturer/product/revision and BIOS version, never serial numbers.
- Do not send serial numbers, MAC addresses, account names, or unique hardware fingerprints to providers.
- Store reference data locally in a versioned, reviewable format. If downloaded later, require signed metadata and a pinned source.
- A missing exact match must yield `baseline unavailable`, not a nearest-model guess.

### 4. Data-Driven Probe Catalog

`SystemProfiler.cs` now contains enough probes that the next expansion should move definitions into a typed local catalog rather than adding more repeated statements.

Each probe should declare:

- stable evidence ID;
- category and source (`Registry`, WMI/CIM, command, CPUID, optional telemetry);
- hive/path/value or query;
- expected type and interpretation;
- privacy classification;
- supported Windows builds/hardware;
- default/absence semantics;
- evidence source note.

The catalog remains compiled/local. It is not a remote tweak feed.

### 5. Conflict Rule Expansion

Keep rules deterministic and pairwise/multi-evidence where possible.

Priority families:

- BCD timer overrides versus CPU/platform timer capabilities;
- capture/overlay/filter stacks versus FPS and frametime goals;
- VPN/filter/offload combinations versus network-latency goals;
- VBS/hypervisor/security trade-offs without offering security-disable actions;
- memory profile, DIMM mismatch, temperature, and stability relationships;
- GPU TDR values combined with overclock/tuning software and device errors;
- power plan, throttling, form factor, temperature, and efficiency conflicts;
- page-file/memory overrides versus installed RAM and workload goals;
- stale driver, device-error, and software-hook interactions.

Every rule must include exact evidence values, relationship kind, confidence, objective relevance, explanation, counterproductive effect, and only existing supported action IDs.

### 6. Model Synthesis

The current evidence preview is roughly tens of kilobytes and uses one model request.

If broader evidence exceeds provider context limits:

1. Partition evidence by stable domain, not arbitrary character slices.
2. Run local conflict analysis first.
3. Ask the model for bounded domain findings.
4. Validate every domain response against that domain's evidence IDs.
5. Run one final synthesis over validated findings and local conflict patterns.
6. Show domain progress and estimated provider calls before starting because BYOK requests can cost money.
7. Allow a single-pass mode for small local models.

Never add artificial waiting to imply thoroughness.

### 7. Supported Action Expansion

The **All supported** view is already present. New writable parameters should be added only when their local implementation is reversible.

For each new action:

- document current/default semantics and platform support;
- include the exact evidence IDs that justify it;
- classify low/medium/high risk;
- preserve Registry kind and absence, not only value text;
- back up every affected key or state source;
- verify apply and rollback independently;
- add one focused runnable test plus VM validation for system writes.

Do not create a generic `set arbitrary evidence value` action.

## Suggested Order

1. Add cancellation to the Tauri/agent boundary.
2. Extract the typed probe catalog without changing collected values.
3. Add evidence privacy classes and payload-size reporting.
4. Expand deterministic conflict tests.
5. Prototype the optional telemetry adapter behind a compile/runtime feature flag.
6. Add exact component baselines for one CPU family and one memory path.
7. Add domain-based multi-pass synthesis only after measuring payload/context failures.
8. Add reversible actions and VM rollback validation last.

## Required Checks

```powershell
dotnet format NeuroTune.sln --no-restore
dotnet build NeuroTune.sln -c Release --no-restore
dotnet test NeuroTune.sln -c Release --no-build
cd ui
npm test
npm run typecheck
npm run lint
npm run build
cd src-tauri
cargo fmt --check
cargo clippy -- -D warnings
```

Also run:

- direct `scan` and `analyze-local` agent smoke checks;
- username/device-name redaction checks on the complete evidence bundle;
- payload size and provider context-limit checks;
- `dotnet list NeuroTune.sln package --vulnerable --include-transitive`;
- `npm audit --audit-level=high`;
- a Tauri release build;
- disposable Windows VM apply/verify/rollback tests before releasing new write actions.

## Completion Criteria for Issue #3

Issue #3 can close only when:

- cancellation leaves no orphan process;
- every conflict cites real evidence values and explains objective-specific impact;
- optional low-level telemetry has explicit consent and a reviewed trust boundary;
- unsupported firmware facts remain honest and visible;
- all supported actions are user-selectable regardless of recommendation/risk;
- no arbitrary model-generated write path exists;
- new actions pass apply, verify, and rollback validation on the supported matrix.
