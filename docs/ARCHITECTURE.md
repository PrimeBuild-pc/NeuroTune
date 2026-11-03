# Architecture

## Goal

Make NeuroTune's evidence-led investigation usable through an existing AI harness and model, without duplicating that harness's orchestration, credentials, provider transport or UI. Preserve the hard distinction between observation, inference, approval and execution.

The CLI reads, analyzes and validates. Confirmed changes are applied by the agent through the harness's own tools, following the written Apply/recovery instructions; see [ADR 0002](decisions/0002-agent-applied-changes.md).

## Modules and interface

```text
Human <-> existing harness/model
                   |
           Agent Skills workflow
                   |
            fixed JSON CLI interface
             |       |         |
   local inventory  frame math  advisory plan validation
         |
  fixed Windows collector

After the user confirms a reviewed plan:
  agent -> harness command/file tools -> Windows
  (backup + journal + verify per references/apply.md; the CLI never writes)
```

The **seam** is the versioned JSON command interface, not a model SDK. A skill is the first **adapter**: it teaches a harness when and how to invoke that interface. Pi and Claude Code manifests load the same skill; neither adds executable host-specific code. An MCP adapter is justified only by a second, concrete host that cannot conveniently run the CLI; it must reuse the same implementation and advertise the same authority limits.

Modules:

| Module | Responsibility | Test surface |
|---|---|---|
| `io.mjs` | Bounded reads, content IDs, exclusive report files | File fixtures and failure cases |
| `collect.mjs` + fixed PS1 | Local Windows inventory and private staging | Explicit live Windows smoke test |
| `evidence.mjs` | Allowlisted inventory and stable fact IDs | Synthetic collector reports |
| `frames.mjs` + `capframex.mjs` | CSV/single-run CapFrameX validation, shared numerical aggregation, comparison gates | Synthetic fixtures + CLI workflow |
| `plan.mjs` | Advisory hypothesis checks; no executable authority | Valid and hostile proposals |
| `assessment.mjs` | Goal/question choices, evidence-bound intake and explicit coverage gaps; tool capability descriptions without discovery or installation | Partial/hostile intake, source substitution and derived-field tampering |
| `apps-services.mjs` | Bounded import of unattested registry/SCM snapshots; registered desktop app/service facts and unresolved dependencies, no usage/removal inference | Synthetic/hostile imports, derived-field tampering, CLI privacy/overwrite and native VM projection |
| `game-configs.mjs` + game-config reference | Static discovery paths/formats/key hints for 13 games; hardware-derived assessment guidance, no presets, local inspection or writer | Complete catalogue, policy/format exceptions, immutable lookups, CLI and standalone-package checks |
| `proposal.mjs` | Inert intervention review with hypothesis/evidence binding, dependency ordering and explicit subset selection | Cycles, missing prerequisites, spoofed authority and changed inputs |
| `cli.mjs` | Strict arguments and JSON envelope | Subprocess tests |
| `SKILL.md` + references | Consent, investigation, interpretation, apply/backup/recovery instructions and stop criteria | Packaging/discovery checks, scoped real model trials and VM apply/recovery trials |

The modules have depth: CSV interpretation, comparison validity and missing-value semantics live once rather than being restated by every model. No generic tool registry, service container or plugin loader is needed for this fixed command set.

## Contract

`help` is the discoverable command reference. All public CLI responses use `{ok:true,data:...}` or `{ok:false,error:{code,message}}`; errors exit 1 and do not echo raw input. Report schema is version 1. Unknown commands/options, duplicate scalar options and extra plan fields fail closed.

Inventory/frame artifacts are JSON with a content ID. Compact report reads allow 512 KiB to cover the bounded inventory schema including JSON-escaped Unicode; raw imports allow 10 MiB and plans 128 KiB. `evidence` resolves a report to fact IDs. `plan-check` accepts only supplied reports as evidence, never a repository-wide search or external authority claim. The implementation is the authoritative validator; references document it and contract tests exercise the same code. Avoid maintaining a second hand-written validator/schema that can drift.

Assessment/proposal schema version 1 is distinguished by `kind`; it does not change inventory/frame schema version 1. Intake and proposals are bounded to 128 KiB; assessment reads to 512 KiB. `assess` validates 1–20 distinct supplied source reports, reuses advisory hypothesis checks and creates only a new artifact. `proposal-check` reconstructs/validates the assessment from those sources, validates the whole proposal and checks the exact chosen subset. Its content ID binds the proposal and dependency-ordered selection, not approval. The separate `apps-services` import accepts bounded native registry/SCM snapshot JSON and creates an allowlisted `apps-services` report/evidence source. Store apps, usage and group/missing dependencies remain explicit gaps. The pinned hardware collector is unchanged; native app/service collection is repository-only. Proposal text and expected-state descriptions are data, never commands or protected snapshots. No `approved` field is accepted.


Comparisons are descriptive. A valid execution can return an inadmissible comparison with reasons. Absent data stays absent; invalid frames do not silently vanish; a content hash is never called an attestation.

## Context and operational efficiency

- One model loop: the host's. No nested Pi process, API key, OAuth store, classifier or provider fallback.
- Progressive disclosure: one short skill entry, branch-specific references, compact allowlisted evidence.
- Local numerical calculations and hashes; no LLM arithmetic or raw 10 MiB CSV in context.
- Stateless CLI startup; no daemon, open listening socket or idle model memory.
- Explicit artifacts let a new session resume without re-collecting the machine or inheriting opaque conversation state.
- Zero npm runtime/build dependencies; Node 22+ is the only portable runtime. The original fixed Windows collector remains PowerShell 5.1 compatible.

These are structural reductions, not measured model-quality/latency claims. Real token and task-completion comparisons remain an evaluation milestone.

## Provenance

NeuroTune Skill is the successor to an earlier standalone desktop app (abandoned in favor of this skill-based approach; see [ADR 0001](decisions/0001-skill-first.md)). The fixed Windows hardware collector and frame-analysis numerical conventions were carried over from it, with stricter input rejection and no claim of full measurement parity; see [NOTICE.md](../NOTICE.md) for exact provenance. No other code, UI or execution path from that app is part of this skill.

## Windows 11 and harness-owned interaction

Windows 11 is the only current product target. macOS/Linux support and remote cross-OS execution are out of scope; retain existing pure-code portability and defensive checks without expanding or deleting historical acceptance evidence.

### Default harness permissions

NeuroTune inherits the harness's existing tool/command permission policy. It does not configure, detect or reproduce permission modes. The harness handles any command approval it requires; in allow-all/bypass mode NeuroTune adds no second command-permission prompt. Context/privacy questions and the final human review of the proposed plan remain workflow steps, not replacements for OS/tool authority. Standard Windows elevation may still be required. Internal lab binding objects do not justify a separate product permission system. A model-authored approval field is never authoritative.

All user interaction belongs to the existing harness: CLI agents, GPT apps or Claude Code desktop when the particular host actually exposes the required local tools. There is no separate NeuroTune window, wizard, dashboard or frontend. Use native question/approval controls where available and concise numbered chat choices otherwise. Keep machine-readable CLI JSON separate from user-facing summaries. Ask at most two relevant questions at once, offer Not sure, then present selected effects, evidence/uncertainty, risk/restart, backup/recovery and verification before Approve all / Choose steps / Other.

Applying confirmed steps uses the ordinary harness tool authorization and required OS privileges; NeuroTune does not implement a second command-permission policy. Before each write the agent reads and saves the current state, and afterwards reads it back. Model-authored Yes/JSON flags cannot grant OS/tool authority. Standard Windows UAC, when necessary, is an OS prompt, not a NeuroTune application UI. Hosts without local execution/elevation must report that the step cannot be applied.
