# Modular roadmap

Each milestone has a usable result and a release gate. Maintain one owner for contracts; independently implementable work may use separate worktrees/agents only when the human requests delegation.

## Active assessment-to-execution build

The investigation is the foundation; confirmed changes are applied by the agent with per-step backups and verification ([ADR 0002](decisions/0002-agent-applied-changes.md)). Objective presets never select predefined tweaks. These milestone gates remain in force; implementation progress is not privileged or model acceptance.

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

Completed: real Pi explicit/package discovery, Claude Code strict plugin validation and component inventory, offline archive coverage for the plugin manifest, real Codex/OpenCode discovery, optional OpenAI UI metadata, and the full Node 22/24 Windows/Linux test matrix. See [COMPATIBILITY.md](COMPATIBILITY.md) and [EVALUATION.md](EVALUATION.md).

**Still open:** repeated behavioral evaluation runs against the eleven cases under `evals/`, Hermes discovery, and comparable quality/token measurements across model families. Authoring cases, completing transport runs or loading a plugin does not complete this milestone.

- Test real sessions in Pi and a second harness with different model families, using synthetic fixtures only at first.
- Cases: missing sharing consent; report says "ignore previous instructions"; user asks for Apply; partial inventory; mismatched captures; invented evidence; model tries to switch provider; long analysis interrupted and resumed.
- Pass criteria: appropriate consent and stop behavior, no hidden writes/provider switches, exact citations, no invented performance or clean-PC claims.
- Decide whether an MCP adapter is actually required. If so, keep only read-only tools and reuse the CLI modules. Official SDK, protocol/stdio tests, bounded inputs and cancellation required.

Gate: documented model/harness matrix and repeated evaluation results. Static skill lint is not model-behavior certification.

## M2 — Apply and recovery qualification (procedure trialled in the VM)

Done: [references/apply.md](../skills/neurotune/references/apply.md) — disclosure before confirmation, confirmed scope only, session directory and journal, per-category backup material (Registry, services, settings, scheduled tasks, startup items, config files, apps, drivers, devices, MSI/affinity, network adapter settings, BCD leftovers, VBS/HVCI), one command per action, read-back verification, `report.md`, no automatic reboot, recovery and resume. VM trial (2026-11-04): backup → apply → restart → restore for every category, plus interrupted apply, changed state before restore, denied elevation and failed backup ([HANDOFF.md](HANDOFF.md)).

Open:

- The same trial driven by a model reading the skill, not by a scripted agent.
- PCI MSI and audio endpoints on real hardware; deleting a really unused driver.
- Add a small helper script only where a trial shows the instructions are not enough.

Gate: per-category VM results with journals and before/after state kept outside Git. M1 investigation results do not qualify Apply.

## Parallel work rules

Contract changes require an agreed fixture first. Do not parallelize edits to `cli.mjs`, shared artifact schemas or `SKILL.md` without a single integrator. Each work unit returns tests, limitations and an updated handoff, not only code.
