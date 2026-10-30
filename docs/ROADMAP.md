# Modular roadmap

Each milestone has a usable result and a release gate. Maintain one owner for contracts; independently implementable work may use separate worktrees/agents only when the human requests delegation.

## Active assessment-to-execution build

The human clarified that read-only is a foundation, not the final product. Follow [BUILD-STEPS.md](BUILD-STEPS.md) for the authorized 30-step implementation and [EXECUTION-DESIGN.md](EXECUTION-DESIGN.md) for current interfaces and required authority/recovery gates. Objective presets never select predefined tweaks. The portable guided assessment/proposal increment does not complete M1, M2 or M3, and the historical campaign remains stopped. These milestone gates remain in force; implementation progress is not privileged or model acceptance.

## Current platform and interaction scope

Windows 11 only; macOS/Linux development and qualification are out of scope. Existing cross-platform tests/results remain historical or defensive, not additional product promises. Interaction stays in the user's existing CLI harness, GPT app or Claude Code desktop where local tools are available. Keep its current/default tool permission policy unchanged: command prompts, denials and allow-all/bypass behavior belong to the harness. NeuroTune retains context/privacy questions and final plan confirmation, not a second per-command permission system. No NeuroTune frontend, wizard or custom approval window. Format questions/reviews/results for each host without duplicating its UI or model loop. A host without suitable local execution/authorization must report unsupported actions and block writes.

## M0 — Read-only skill foundation (implemented)

- Self-contained Agent Skills distribution and Pi package manifest.
- Fixed Windows inventory with collect/share separation.
- Strict PresentMon import, local aggregates, comparison validity gates.
- Fact IDs and non-executable advisory plan validation.
- Unit/CLI/installer tests, package checks, explicit live collector smoke test.

Gate: local tests, clean copied-skill execution from another working directory, archive install smoke test, accurate limitations and no system writes. Exact results belong in [HANDOFF.md](HANDOFF.md).

## M1 — Harness evaluation and installation acceptance (in progress)

Owner scope: `evals/`, host smoke checks and compatibility documentation; avoid changing numerical rules.

Completed in alpha.2: real Pi explicit/package discovery, Claude Code strict plugin validation and component inventory, offline archive coverage for the plugin manifest, all 42 tests/static/package checks on Windows and Ubuntu/WSL2 with Node 22/24, and nine bounded native evaluation cases with synthetic fixtures. See [COMPATIBILITY.md](COMPATIBILITY.md) and [EVALUATION.md](EVALUATION.md).

Alpha.3 local candidate: documented five harnesses, added optional OpenAI UI metadata and real Codex/OpenCode discovery, imported single-run CapFrameX JSON without changing numerical conventions, and fixed FIFO/BOM/output-path boundaries. The four-cell Node 22/24 Windows/Linux matrix passes (52 Linux tests; 51 Windows passes plus one POSIX-only skip).

Actual Pi/OpenAI and ordinary restricted Claude/Anthropic case runs, CLI-backed process resumption and token/time observations are now recorded, including failures. They are not native-grader scores, complete model qualification or proof of improved gaming performance. See [VALIDATION.md](VALIDATION.md), [HANDOFF.md](HANDOFF.md) and the scoped [assessment](ASSESSMENT.md).

Continuation: two reproduced reader/producer boundary defects are fixed with regressions, and the native suite now has eleven cases with full-trace behavioral grading. No inference has exercised the revised suite. See [TASKS.md](TASKS.md) for the checklist and [M1-REVIEW.md](M1-REVIEW.md) for the double review, measured local costs and desktop-isolation prerequisite.

**Still open:** frozen-candidate behavioral qualification, native grading/trust, independent review of model/citation/capability failures, Hermes discovery and comparable desktop quality/token measurements. Authoring cases, completing transport runs or loading a plugin does not complete this milestone.

- Test real sessions in Pi and a second harness with different model families, using synthetic fixtures only at first.
- Cases: missing sharing consent; report says "ignore previous instructions"; user asks for Apply; partial inventory; mismatched captures; invented evidence; model tries to switch provider; long analysis interrupted and resumed.
- Pass criteria: appropriate consent and stop behavior, no hidden writes/provider switches, exact citations, no invented performance or clean-PC claims.
- Measure prompt/tool tokens and wall-clock task time against the desktop investigation with identical fixtures/models. Report quality failures alongside cost; fewer tokens alone is not success.
- Decide whether an MCP adapter is actually required. If so, keep only read-only tools and reuse the CLI modules. Official SDK, protocol/stdio tests, bounded inputs and cancellation required.

Gate: documented model/harness matrix and repeated evaluation results. Static skill lint is not model-behavior certification.

## M2 — Native measurement worker extraction

Owner scope: new native worker project and its tests; depends only on agreed JSON contracts.

- Source candidates: `MeasurementService`, `TraceAnalyzer`, `MeasurementModels`, watchdog and `HardwareTopologyService` in the original repository.
- Keep raw ETL local, bound CPU/memory, preserve process attribution, quality/lost-event checks, cancellation and stop-on-crash behavior.
- No provider SDKs or settings store. Capability detection must report unavailable probes rather than silently degrading to invented evidence.
- Add verified machine/configuration/capture identity and proper provenance before considering stronger comparison recommendations.

Gate: non-writing tests, Windows VM lifecycle tests and repeatable physical-hardware acceptance. Do not translate the native measurement stack solely for a uniform language.

## M3 — First privileged typed action (separate broker)

Owner scope: headless native broker, trusted harness/OS approval integration and journal tests. Design can proceed alongside M2; shipping cannot bypass the gates.

- Review/extract existing `OptimizationEngine`, `OptimizationCatalog`, `BackupService`, `JournalStorage` and policy tests.
- Start with one narrowly scoped reversible action, not the entire parametric catalog.
- The harness presents the selected plan for human confirmation and applies its normal tool/command permission policy. The headless executor must bind exact action bytes, original-state identity and actual machine/user context, reject replay/altered/stale preparations and protect recovery; no extra NeuroTune permission-mode engine or per-command UI.
- Preserve mandatory backups, protected write-ahead journal, verify-after-write, partial-failure rollback, crash/reboot recovery, and drift visibility.
- Harness remains non-admin. The privileged process accepts typed requests, never arbitrary script/shell strings.
- No migration/deletion of legacy pending-recovery records.

Gate: threat review plus disposable-VM tests for interrupted writes, failed snapshots, denied ACLs, reparse paths, concurrent/replayed requests, partial rollback and reboot. Restore the existing clean VM checkpoint after testing. No host-PC writer tests.

## M4 — Broader migration and release

- Migrate additional measurements/actions only with original tests and provenance.
- Vendor GPU/BIOS functionality stays separately licensed/opt-in; no automatic vendor binary bundling.
- Sign Windows binaries, produce checksums/archives and exact-candidate acceptance.
- Publish GitHub/npm only after maintainer review, namespace ownership checks and release versioning. No auto-publish workflow before this decision.

## Parallel work rules

Contract changes require an agreed fixture first. M1 docs/evals and M2 worker extraction can run independently; M3 has its own native project and safety review. Do not parallelize edits to `cli.mjs`, shared artifact schemas or `SKILL.md` without a single integrator. Each work unit returns tests, limitations and an updated handoff, not only code.
