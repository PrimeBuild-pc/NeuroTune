# Continuation handoff

## Current decision

The sibling repository `NeuroTune.Skill` implements M0 and the host-acceptance infrastructure of M1. Current version: **0.1.0-alpha.2**. The original NeuroTune application and its recovery stores were not migrated or modified. See [ADR 0001](decisions/0001-skill-first.md) for the alternatives and [ROADMAP.md](ROADMAP.md) for the next gates.

The project is intentionally not a full optimization executor. Do not fill that gap with shell commands or permissive approval flags. The useful deliverable is an installable investigation skill backed by deterministic local analysis.

## Implemented

- Self-contained standard skill, JSON CLI and Pi package manifest.
- Fixed Windows collection, allowlisted evidence, content-bound citations.
- Strict PresentMon import, frame percentiles/stutters and conservative repeated comparisons.
- Non-executable plans with citation/shape validation.
- Copy installer with no overwrite; MIT attribution and pinned upstream collector hash.
- Synthetic unit/CLI/installer tests, explicit live collector and archive smoke tests.
- Windows/Linux, Node 22/24 CI definition, not yet run remotely.

## Alpha.2 continuation

- Added `.claude-plugin/plugin.json`: the existing standard skill loads as `/neurotune:neurotune`, without hooks, agents, servers, permission grants or a new runtime.
- Real Pi 1.3.0 RPC checks passed for explicit skill and local-package discovery. The isolated session exposes exactly one skill and reports zero tokens, tools and assistant messages.
- Real Claude Code 2.1.304 strict validation and `plugin details` passed: one skill, zero agents/hooks/MCP/LSP servers. Validation of a raw `SKILL.md` path was found to be the wrong CLI invocation; the documented smoke check uses the plugin root and component inventory.
- `npm test`: **42/42 passed**. Added distribution-version, native eval-definition and synthetic-fixture contract checks. Numerical behavior and collection remain unchanged.
- `npm run check` and `npm run test:package` passed. The 26-file offline archive includes the hidden Claude manifest and excludes evaluation cases/results.
- Refreshed Graphify and its wiki: 197 nodes, 389 edges, 13 communities; zero missing endpoints and two existing self-loops. This is structural navigation, without LLM/API extraction.
- Nine native Claude eval cases are authored under `evals/`, using the documented schema rather than a custom inference runner. They include a private unread fixture, a hostile device-name file and a useful positive control. **These model evaluations have not run.**
- Upgraded the user's shared `~/.agents/skills/neurotune` copy to alpha.2 after verifying every installed alpha.1 file against the expected source. The previous copy is retained under ignored `artifacts/installed-backups/`, outside harness discovery. The new copy matches source byte-for-byte and `doctor` passed.
- No live collection was repeated for this metadata/testing milestone. The M0 collector acceptance below remains historical evidence, not a fresh hardware certification.

Read [COMPATIBILITY.md](COMPATIBILITY.md) for repeatable host commands and [EVALUATION.md](EVALUATION.md) for approved live-evaluation setup, costs, limits and grading. Host smoke processes use temporary configuration and withhold inherited credentials. No user harness settings are changed.

M1 remains open for actual model behavior, different model families, CLI-backed runs, real session resumption and measured efficiency. Do not report nine authored cases as nine passes. No paid-provider call, background agent or model switch was used in this continuation.

## Cross-platform acceptance follow-up

Tested alpha.2 source commit: `e7400154cbdb9f1fdc610cfc399454a6ac4e1489`.

- Existing `Ubuntu-22.04` is WSL2 (Ubuntu 22.04.5 LTS); no new distribution or VM was installed. WSL's default version setting was 1, but this actual distribution is version 2. Docker's distribution was not used.
- No native Linux Node was installed. Temporary official Linux x64 Node 22.23.3 and 24.22.0 archives were downloaded over HTTPS and matched against upstream SHA-256 checksums before execution.
- Both Linux versions passed **42/42 tests**, `npm run check` and `npm run test:package` from a clean clone on ext4, as UID 1000. `doctor` correctly reported `liveInventory=false`, `offlineAnalysis=true`, `systemWrites=false`, `providerCalls=false`.
- Temporary official Windows x64 Node 22.23.3 passed the same checks from a separate clean clone. Its download used WSL HTTPS after Windows curl failed a revocation check; no TLS verification was disabled. Existing Windows Node 24.20.0 acceptance completes the four-cell local OS/runtime matrix.
- Temporary runtimes/clones were removed. No global Node installation, shell profile, Windows feature or harness setting was changed. Raw test logs are in ignored `artifacts/linux-acceptance.txt` and `artifacts/windows-node22-acceptance.txt`.
- This follow-up changes acceptance documentation only. The alpha.2 runtime, installed skill and existing distribution archive are unchanged. Remote CI, real model evals and hardware/privileged acceptance remain unverified.

See [COMPATIBILITY.md](COMPATIBILITY.md) for exact versions and scope. The next M1 step is an explicitly selected model/judge and permitted account usage for the existing synthetic evals; do not silently use paid API credentials or assume unlimited subscription usage.

## M0 verification log

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
- The local Windows/Linux Node 22/24 matrix passed; this does not imply remote CI, other distributions/architectures or Linux-hosted Windows collection passed.
- No real-game performance improvement, hardware benchmarking, recovery parity or security certification was measured.
- No GitHub push, npm publication or paid-provider call is needed for this milestone.

## Resume efficiently

1. Read this handoff, architecture and the M1 gate; inspect Git status before edits.
2. Query the local Graphify graph for source navigation. It is AST-only: document meaning is not semantically indexed. Explicitly read the roadmap and security contract when needed.
3. Run `npm test` and `npm run check`; use `npm run test:package` for a clean offline archive install. Optional `test:pi` and `test:claude` require explicit paths to installed host binaries.
4. Continue M1 with the prepared synthetic cases after choosing the approved model/judge/budget. Keep `cli.mjs` and artifact contracts under one integrator if work is later delegated.
5. For M2/M3, inspect the original project's graph and source tests, not only this package. Preserve its pending-recovery path.

## Original repository navigation

Original revision: `15db2ed7c056fcbb70fca316a2a090c2d7f7f21b`. The existing Graphify graph was refreshed and a wiki generated. Result: 2,921 nodes, 7,852 edges, 172 communities. Diagnostic: no missing/dangling endpoints or same-endpoint collapse in the saved graph; 20 self-loops remain. The graph is a navigation aid, not a correctness proof. Pre-existing `plans/` files were left untouched.
