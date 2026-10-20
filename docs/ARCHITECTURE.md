# Architecture

## Goal

Make NeuroTune's evidence-led investigation usable through an existing AI harness and model, without duplicating that harness's orchestration, credentials, provider transport or UI. Preserve the hard distinction between observation, inference, approval and execution.

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

Future, separate: human approval UI -> privileged typed-action broker
                 (not reachable through today's CLI)
```

The **seam** is the versioned JSON command interface, not a model SDK. A skill is the first **adapter**: it teaches a harness when and how to invoke that interface. Pi and Claude Code manifests load the same skill; neither adds executable host-specific code. An MCP adapter is justified only by a second, concrete host that cannot conveniently run the CLI; it must reuse the same implementation and advertise the same authority limits.

Modules:

| Module | Responsibility | Test surface |
|---|---|---|
| `io.mjs` | Bounded reads, content IDs, exclusive report files | File fixtures and failure cases |
| `collect.mjs` + fixed PS1 | Local Windows inventory and private staging | Explicit live Windows smoke test |
| `evidence.mjs` | Allowlisted inventory and stable fact IDs | Synthetic collector reports |
| `frames.mjs` | CSV validation, numerical aggregation, comparison gates | Pure functions + CLI workflow |
| `plan.mjs` | Citation/shape checks; no executable authority | Valid and hostile proposals |
| `cli.mjs` | Strict arguments and JSON envelope | Subprocess tests |
| `SKILL.md` + references | Consent, investigation, interpretation and stop criteria | Packaging and real host discovery checks; model cases authored, execution pending |

The modules have depth: CSV interpretation, comparison validity and missing-value semantics live once rather than being restated by every model. No generic tool registry, service container or plugin loader is needed for six commands.

## Contract

`help` is the discoverable command reference. All runtime responses use `{ok:true,data:...}` or `{ok:false,error:{code,message}}`; errors exit 1 and do not echo raw input. Report schema is version 1. Unknown commands/options, duplicate scalar options and extra plan fields fail closed.

Inventory/frame artifacts are JSON with a content ID. `evidence` resolves a report to fact IDs. `plan-check` accepts only supplied reports as evidence, never a repository-wide search or external authority claim. The implementation is the authoritative validator; references document it and contract tests exercise the same code. Avoid maintaining a second hand-written validator/schema that can drift.

Comparisons are descriptive. A valid execution can return an inadmissible comparison with reasons. Absent data stays absent; invalid frames do not silently vanish; a content hash is never called an attestation.

## Context and operational efficiency

- One model loop: the host's. No nested Pi process, API key, OAuth store, classifier or provider fallback.
- Progressive disclosure: one short skill entry, branch-specific references, compact allowlisted evidence.
- Local numerical calculations and hashes; no LLM arithmetic or raw 10 MiB CSV in context.
- Stateless CLI startup; no daemon, open listening socket or idle model memory.
- Explicit artifacts let a new session resume without re-collecting the machine or inheriting opaque conversation state.
- Zero npm runtime/build dependencies; Node 22+ is the only portable runtime. The original fixed Windows collector remains PowerShell 5.1 compatible.

These are structural reductions, not measured model-quality/latency claims. Real token and task-completion comparisons remain an evaluation milestone.

## Migration, not a clean-room rewrite

The old repository remains authoritative for the desktop app and pending recovery. Its graph was queried for `OptimizationCatalog`, `OptimizationRun`, `LlmClient`, `SystemProfiler`, `MeasurementService`, `PresentMonImporter`, the Agent command switch and hardware collector.

The useful reusable seam was **not** the entire Agent executable: that entry point instantiates provider settings, action catalog, backups and workflow state for unrelated commands. Reusing it would carry unwanted authority and dependencies into a read-only skill. The hardware collector is already standalone and was copied byte-for-byte. Frame numerical conventions were ported, with stricter input rejection and no unsupported claim of full measurement parity.

Future Windows-native code should be extracted selectively from the existing C# core, retaining its tests and licensing, instead of translating privileged operations to JavaScript. Packaging Node for orchestration does not imply rewriting the executor in Node.

## Host vs OS portability

The workflow and offline analysis are model/harness-independent and portable. Live Windows inventory and future Windows writes are OS-specific. A Linux-hosted remote harness needs a separately reviewed Windows execution connection; this version does not provide one. “Any harness” means a tool-capable host can use the same skill/CLI contract, not that every model follows instructions reliably or every discovery mechanism is identical.
