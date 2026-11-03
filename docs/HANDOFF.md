# Continuation handoff

## Agreed product direction and next-session plan

This section records the user's agreed direction and takes precedence over historical product-planning statements below. It does not enable system changes in the current skill or replace the current security contract.

### Product purpose

NeuroTune closes the domain-knowledge gap between a generic request such as "optimize my PC for more FPS in Fortnite" and an expert investigation. Its main value is well-constructed instructions: context questions, an evidence-led analysis sequence, explicit limits, a useful final audit and a reviewed action plan. These instructions should improve the model's work, not promise that it cannot make mistakes.

The product is a skill guiding the existing harness/model, not a standalone optimizer or a new execution/recovery platform. Use the harness's tools, existing command permissions and ordinary Windows elevation when required. Do not add a NeuroTune UI, model loop or parallel permission system. Small scripts are optional and justified only by a concrete need; a dedicated typed-action broker is not an agreed product requirement.

The computer belongs to the user. After receiving an honest account of benefits, uncertainty, risks and recovery limits, the user decides which proposed changes to accept, reject or revise. Incomplete reversibility must be disclosed before confirmation; it does not by itself justify hiding a recommendation or replacing the user's decision. User confirmation does not create missing tool capabilities, bypass harness/OS restrictions or make a risky operation safe.

### Intended Apply/recovery workflow

1. Complete the investigation and present the final audit and proposed changes, with evidence, uncertainty, risk, restart needs and recovery limits.
2. Let the user approve all, choose a subset, reject or revise the plan. Apply only the confirmed scope; material changes to that scope require renewed review.
3. Before each change, save the original state and the available recovery material in a clearly identified local session directory. Record the target, intended change and restoration procedure. If preparation fails, stop that action and report the failure rather than claiming a backup exists.
4. Have the agent execute through the harness's actual tools and permissions. Do not assume that a Markdown instruction or model-authored approval field grants authority.
5. Verify the resulting state and record before/after values, completed actions, failures and unfinished work. Do not report a partially applied plan as fully successful.
6. Recommend a reboot when needed and explain what remains to verify afterward; do not reboot automatically.
7. On a recovery request, inspect the saved state and current conditions, explain what can and cannot be restored, confirm the intended recovery and verify its outcome.

Recovery instructions must distinguish documentation from usable restoration material:

- Registry: preserve the exact path, value type and original value, including whether the value was originally absent.
- Windows settings/services: preserve the original properties being changed, not merely an after-the-fact diff; record before/after state.
- Configuration files: preserve the original bytes and destination path.
- Removed applications: keep the removal list and available reinstall/settings material, but do not call the list itself a backup or guarantee restoration.
- Removed drivers: preserve available driver packages and recovery instructions, but disclose that a package copy may not recover a machine that loses bootability or connectivity.

Do as much as practical to support reversibility without pretending every operation is fully reversible. Make limitations explicit and leave the informed selection to the user. Backup preparation is an explicit instruction for every model, not an assumed habit of stronger models. Instructions are guardrails, not an OS-enforced sandbox or proof of recoverability.

### Ordered continuation checklist

1. **Align the contract before implementation or new qualification runs.** Reconcile this direction with `AGENTS.md`, `SECURITY.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, the skill/references and relevant tests. They currently prohibit system writes or describe a dedicated executor. Preserve evidence/privacy boundaries, distinguish today's read-only runtime from future agent-guided operations, and revise the no-shell-fallback case so it distinguishes unsupported/unapproved workarounds from the intended reviewed workflow. This handoff-only change does not make those edits or authorize execution.
2. **Freeze the candidate and choose evaluation usage.** Explicitly agree harness/model/judge IDs, account usage, budget and tool grants; use synthetic fixtures first. Follow `docs/EVALUATION.md` for source snapshots and trace retention. No silent paid calls, provider changes or inference delegation.
3. **Run the eleven behavioral scenarios repeatedly.** Start with a positive smoke case, then three runs per case per selected arm. Inspect complete traces for consent, injected instructions, missing evidence, invalid comparisons, exact citations, unsupported measurements and useful answers. Authored cases and completed processes are not passes; investigate critical failures instead of averaging them away.
4. **Exercise a complete CLI-backed investigation.** Test questions, collection/import, evidence reading, analysis and the final audit together, not only responses to supplied summaries.
5. **Test actual interruption/resumption.** Resume through the harness's supported mechanism and check retained evidence, completed/pending work and consent scope. The synthetic handoff case alone does not qualify this.
6. **Check hosts and model families.** Repeat relevant behavior in Pi and a second harness using different model families; verify Hermes discovery separately. Record exact versions and limitations, not a promise of support for every host/model.
7. **Measure usefulness and efficiency.** Compare representative work with/without the skill, recording quality, actual tokens and elapsed time where available. Blanket refusals are not useful-positive passes. Decide whether MCP solves a demonstrated host limitation; if the CLI suffices, document that no adapter is needed.
8. **Review and close M1 on evidence.** Keep per-case pass/fail/blocked results and trace locations, resolve grader disagreements and obtain independent review. Update the handoff/roadmap honestly; installation success and unit tests alone do not close behavioral acceptance.
9. **Implement and test the agreed Apply/recovery guidance.** Prefer clear Markdown operating instructions; add small helpers only where needed. Add targeted preparation/apply/verification/recovery and failure-path trials. Any system-changing test belongs in the disposable Windows VM, never on the host. Do not build a dedicated execution platform by default or count M1 investigation acceptance as Apply/recovery qualification.

The logs further below are historical; their versions, case counts and test totals are not current qualification results.

## Session 2026-11-03: status per checklist item

Uncommitted working tree on top of `03ba9f4`. Raw results, traces, judge outputs and candidate SHA-256 manifests are in ignored `evals/results/` (`summary-2026-11-03.json` consolidates them).

1. **Contract aligned — done.** New [references/apply.md](../skills/neurotune/references/apply.md) and [ADR 0002](decisions/0002-agent-applied-changes.md). SKILL.md gains step 7 (Apply, verify, recover) and hard limits for confirmed-and-backed-up changes, consented read scope, full 64-hex fact IDs and no `approved`-field shortcut; its `description` now states when to load it. Session journals go to `%LOCALAPPDATA%\NeuroTuneSkill\sessions`, never `%LOCALAPPDATA%\NeuroTune\` (the old desktop app's store, which exists on the user's PC with key files; a trial briefly created a `sessions` folder there and it was removed). SECURITY, ARCHITECTURE, ROADMAP (new M2), AGENTS, README (new "Does it help?" section and `assets/eval-results.svg`), EVALUATION, plugin metadata and references updated. `execution-check` and its modules/tests deleted. `npm test` 83 passed / 1 POSIX-only skipped; `npm run check` and `npm run test:package` passed.
2. **Usage chosen by the user.** DeepSeek `deepseek-flash` with the user's key (environment variables and a temporary VM file only, never in the repository; about US$0.20 of the second top-up used) and `claude-haiku-5-5` at effort medium on the user's Claude account (about US$1.5 of reported usage).
3. **Eleven cases × 3 runs × with/without — done for two families.** Haiku with native judge: 32/33 with, 23/33 without. DeepSeek with substitute judge: 33/33 with, 29/33 without. The skill decides `no-shell-fallback` and `unsupported-measurement`; most other cases pass either way. Fixes found on the way: `add_dirs` is not attached on native Windows runs, so `consent-before-reading` (previously passing trivially) and `untrusted-device-text` (previously blocked) now copy their fixture with a scaffold script and need `--scaffold`; Haiku loaded the skill in only 21/33 runs until the description was made explicit (32/33 after). DeepSeek's with-skill arm is from the earlier candidate. Correction to an earlier note: DeepSeek `exact-fact-citations` without skill was not 0/3 (those were judge errors).
4. **Complete CLI-backed investigation — done twice.** DeepSeek in Pi 1.5.0 inside the VM, and Haiku in an isolated Claude Code session on the user's PC (no user settings, plugins or CLAUDE.md). Both ran the CLI, read live state, kept two questions per batch, proposed conservative plans and changed nothing before confirmation.
5. **Interruption/resumption — done.** Every turn was a new process resumed with Pi `--continue` or Claude `--resume`. The first run exposed out-of-scope reads on resume (Pi session logs, a key file); after the SKILL.md fix a VM rerun showed no out-of-scope reads and full fact IDs.
6. **Hosts/model families — two families, two hosts.** Claude Code native runner (Haiku, DeepSeek) and Pi (DeepSeek). Hermes not checked.
7. **Usefulness/efficiency — measured.** Short cases: Haiku +28% tokens with the skill, DeepSeek −6%. Full local investigation with Haiku (two turns): about 3× the cost with the skill ($0.054 vs $0.018; 26 vs 7 model calls), for evidence-linked analysis, a validated plan and a verified, hash-checked backup. Biggest reduction opportunities: compact live command output and not rereading references on resume. No MCP adapter needed: the CLI worked in both hosts.
8. **M1 closure — evidence collected, independent review still missing.** Per-case results and trace locations are in `evals/results/`. Grader disagreement is low; no independent human review of the traces yet.
9. **Apply/recovery — exercised.** Scripted check of every apply.md command in the VM (all categories pass; `reg import` does not delete added values; `pnputil` needs an existing folder). Agent trials: DeepSeek in the VM applied five steps with journal and backups and restored them exactly; Haiku on the user's PC applied one config step and restored it byte for byte. Host state (301 services, 258 scheduled tasks, power plans, 16 registry branches) was snapshotted before and compared after cleanup: identical except one key that changes by itself. VM reset to `Clean-NeuroTune-Alpha2`. Still open: denied elevation, an apply interrupted midway, driver/app removal, and a without-skill arm that applies changes (deliberately not run on the host).

## Historical alpha.2 decision

The sibling repository `NeuroTune.Skill` implements M0 and the host-acceptance infrastructure of M1. Version at that handoff: **0.1.0-alpha.2**. The original NeuroTune application and its recovery stores were not migrated or modified. See [ADR 0001](decisions/0001-skill-first.md) for the alternatives and [ROADMAP.md](ROADMAP.md) for the next gates.

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

Start with the agreed direction and ordered checklist at the top of this handoff. The commands below remain useful; historical milestone assumptions do not override that agreement.

1. Read this handoff, architecture and the M1 gate; inspect Git status before edits.
2. Query the local Graphify graph for source navigation. It is AST-only: document meaning is not semantically indexed. Explicitly read the roadmap and security contract when needed.
3. Run `npm test` and `npm run check`; use `npm run test:package` for a clean offline archive install. Optional `test:pi` and `test:claude` require explicit paths to installed host binaries.
4. Continue M1 with the prepared synthetic cases after choosing the approved model/judge/budget. Keep `cli.mjs` and artifact contracts under one integrator if work is later delegated.
5. For Apply/recovery, follow the agreed skill-first scope above, not an assumed M2/M3 executor migration. Leave the original application's recovery stores and any pending recovery untouched.

## Original repository navigation

Original revision: `15db2ed7c056fcbb70fca316a2a090c2d7f7f21b`. The existing Graphify graph was refreshed and a wiki generated. Result: 2,921 nodes, 7,852 edges, 172 communities. Diagnostic: no missing/dangling endpoints or same-endpoint collapse in the saved graph; 20 self-loops remain. The graph is a navigation aid, not a correctness proof. Pre-existing `plans/` files were left untouched.
