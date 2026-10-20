# Continuation handoff

## Current decision

A new sibling repository, `NeuroTune.Skill`, implements the read-only M0 milestone. The original NeuroTune application and its recovery stores were not migrated or modified. See [ADR 0001](decisions/0001-skill-first.md) for the alternatives and [ROADMAP.md](ROADMAP.md) for the next gates.

The project is intentionally not a full optimization executor. Do not fill that gap with shell commands or permissive approval flags. The useful deliverable is an installable investigation skill backed by deterministic local analysis.

## Implemented

- Self-contained standard skill, JSON CLI and Pi package manifest.
- Fixed Windows collection, allowlisted evidence, content-bound citations.
- Strict PresentMon import, frame percentiles/stutters and conservative repeated comparisons.
- Non-executable plans with citation/shape validation.
- Copy installer with no overwrite; MIT attribution and pinned upstream collector hash.
- Synthetic unit/CLI/installer tests, explicit live collector and archive smoke tests.
- Windows/Linux, Node 22/24 CI definition, not yet run remotely.

## Verification log

Initial local Windows / Node 24.20.0 run: 37 tests passed after correcting the installer to copy directory entries into its exclusively reserved destination. The first run caught Node's `cp` refusing an existing destination when `errorOnExist` was set; the failure was fixed and rerun.

The explicit live Windows collector smoke test passed: collection, artifact validation and existing-output refusal. Hardware facts were not printed into model context; temporary reports were removed. This did not certify a non-admin run, all hardware configurations or a disposable-VM release candidate.

Final local acceptance (2026-10-20, Windows, Node 24.20.0):

- `npm test`: **39/39 passed**. Targeted review added failing regressions for coerced hash values and importer/reader bound mismatches; both were fixed and rerun.
- `npm run check`: JavaScript syntax, Markdown links, skill metadata, dependency policy and byte-identical collector provenance passed.
- `npm run test:package`: built a 23-file archive, installed it with npm offline into a temporary prefix, ran its CLI and copied/executed a standalone skill from an unrelated directory. Passed; temporary installs removed.
- `npm run test:collector`: real read-only Windows collection and overwrite refusal passed. No report contents were printed or committed.
- Installed a standalone copy in the user's shared `~/.agents/skills/neurotune` directory and ran its `doctor` successfully. Reload the harness to discover it. No harness settings or credentials were changed.
- Graphify generated `graph.json`, `graph.html`, `GRAPH_REPORT.md` and a navigation wiki locally: 159 nodes, 329 edges, 10 communities. No LLM/API calls. Two self-loops, no missing/dangling endpoints; two top-level scripts yielded no symbols. The graph is partial structural navigation, not semantic document indexing.

The local distribution is built into `artifacts/` with SHA-256 checksums. Artifacts, personal evidence and generated graph files are excluded from Git/npm source payloads. The repository has no remote and has not been pushed or published.

## What not to infer

- Passing code tests does not prove a model obeys the consent workflow or resists prompt injection.
- The Windows host smoke test is read-only, not an elevated-writer test.
- No Linux/Node 22 execution is claimed until CI or a matching local environment runs it.
- No real-game performance improvement, hardware benchmarking, recovery parity or security certification was measured.
- No GitHub push, npm publication or paid-provider call is needed for this milestone.

## Resume efficiently

1. Read this handoff, architecture and the M1 gate; inspect Git status before edits.
2. Query the local Graphify graph for source navigation. It is AST-only: document meaning is not semantically indexed. Explicitly read the roadmap and security contract when needed.
3. Run `npm test` and `npm run check`; use `npm run test:package` for a clean offline archive install.
4. Start M1 with synthetic harness evaluations. Keep `cli.mjs` and artifact contracts under one integrator if work is later delegated.
5. For M2/M3, inspect the original project's graph and source tests, not only this package. Preserve its pending-recovery path.

## Original repository navigation

Original revision: `15db2ed7c056fcbb70fca316a2a090c2d7f7f21b`. The existing Graphify graph was refreshed and a wiki generated. Result: 2,921 nodes, 7,852 edges, 172 communities. Diagnostic: no missing/dangling endpoints or same-endpoint collapse in the saved graph; 20 self-loops remain. The graph is a navigation aid, not a correctness proof. Pre-existing `plans/` files were left untouched.
