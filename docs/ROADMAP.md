# Modular roadmap

Each milestone has a usable result and a release gate. Maintain one owner for contracts; independently implementable work may use separate worktrees/agents only when the human requests delegation.

## M0 — Read-only skill foundation (implemented)

- Self-contained Agent Skills distribution and Pi package manifest.
- Fixed Windows inventory with collect/share separation.
- Strict PresentMon import, local aggregates, comparison validity gates.
- Fact IDs and non-executable advisory plan validation.
- Unit/CLI/installer tests, package checks, explicit live collector smoke test.

Gate: local tests, clean copied-skill execution from another working directory, archive install smoke test, accurate limitations and no system writes. Exact results belong in [HANDOFF.md](HANDOFF.md).

## M1 — Harness evaluation and installation acceptance

Owner scope: `tests/evals/`, compatibility documentation; avoid changing numerical rules.

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

Owner scope: native broker, approval UI and journal tests. Design can proceed alongside M2; shipping cannot bypass the gates.

- Review/extract existing `OptimizationEngine`, `OptimizationCatalog`, `BackupService`, `JournalStorage` and policy tests.
- Start with one narrowly scoped reversible action, not the entire parametric catalog.
- Human approval must bind exact action parameters, original-state identity, machine, expiry and nonce outside model-writable state. The broker must reject replay, altered plans and stale originals.
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
