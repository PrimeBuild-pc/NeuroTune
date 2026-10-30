# Architecture

## Goal

Make NeuroTune's evidence-led investigation usable through an existing AI harness and model, without duplicating that harness's orchestration, credentials, provider transport or UI. Preserve the hard distinction between observation, inference, approval and execution.

The product destination now includes assessment-driven execution and recovery; see the [active build ledger](BUILD-STEPS.md) and [execution design](EXECUTION-DESIGN.md). Current runtime capabilities remain separate from that destination: guided assessment and proposal review are implemented, but approval/execution/recovery are not.

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

Future: harness-owned approval -> headless Windows 11 typed-action broker
        (not reachable through today's CLI; no NeuroTune product UI)
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
| `execution.mjs` + fixed `verify-execution.mjs` | Revalidate bundled artifacts and map selected Registry or one game-file parameter set to inert native intent; private stdin adapter for the development preview | Parameter/source substitution, inherited options and native private-pipe tests |
| `cli.mjs` | Strict arguments and JSON envelope | Subprocess tests |
| `SKILL.md` + references | Consent, investigation, interpretation and stop criteria | Packaging/discovery checks and scoped real model trials; failures retained in docs/VALIDATION.md |

The modules have depth: CSV interpretation, comparison validity and missing-value semantics live once rather than being restated by every model. No generic tool registry, service container or plugin loader is needed for this fixed command set.

## Contract

`help` is the discoverable command reference. All public CLI responses use `{ok:true,data:...}` or `{ok:false,error:{code,message}}`; errors exit 1 and do not echo raw input. Report schema is version 1. Unknown commands/options, duplicate scalar options and extra plan fields fail closed.

Inventory/frame artifacts are JSON with a content ID. Compact report reads allow 512 KiB to cover the bounded inventory schema including JSON-escaped Unicode; raw imports allow 10 MiB and plans 128 KiB. `evidence` resolves a report to fact IDs. `plan-check` accepts only supplied reports as evidence, never a repository-wide search or external authority claim. The implementation is the authoritative validator; references document it and contract tests exercise the same code. Avoid maintaining a second hand-written validator/schema that can drift.

Assessment/proposal schema version 1 is distinguished by `kind`; it does not change inventory/frame schema version 1. Intake and proposals are bounded to 128 KiB; assessment reads to 512 KiB. `assess` validates 1–20 distinct supplied source reports, reuses advisory hypothesis checks and creates only a new artifact. `proposal-check` reconstructs/validates the assessment from those sources, validates the whole proposal and checks the exact chosen subset. Its content ID binds the proposal and dependency-ordered selection, not approval. The separate `apps-services` import accepts bounded native registry/SCM snapshot JSON and creates an allowlisted `apps-services` report/evidence source. Store apps, usage and group/missing dependencies remain explicit gaps. The pinned hardware collector is unchanged; native app/service collection is repository-only. Proposal text and expected-state descriptions are data, never commands or protected snapshots. No `approved` field is accepted.

`execution-check` accepts schema-1 `execution-bundle` data: `assessment`, `reports`, `proposal`, `selection`, `parameters` (plus `schemaVersion` and `kind`). The 12 MiB aggregate bound retains 512 KiB/report and assessment and 128 KiB/proposal limits. Each selected step must map once to explicit `{stepId,keyPath,valueName,desiredDword}`; category, canonical target (`HKLM [Registry64]\<keyPath>\<valueName>`) and desired text (`Set DWORD to <unsigned integer>`) must match. Those labels are compared with typed values, never parsed as commands. One selected game-file step instead maps `{stepId,path,expectedSha256,desiredUtf8Base64}` to inert schema-1 `game-file-request`. The reviewed target must be `Windows file [UTF-8]\\<path>`, expected state `SHA-256 <original hash>` and change `Replace UTF-8 content with SHA-256 <desired hash>`. Exact canonical UTF-8/Base64 bytes are limited to 128 KiB; mixed/multi-file batches are unsupported. Unsupported categories and missing dependencies fail closed. Neither native intent kind is approval.

Comparisons are descriptive. A valid execution can return an inadmissible comparison with reasons. Absent data stays absent; invalid frames do not silently vanish; a content hash is never called an attestation.

## Context and operational efficiency

- One model loop: the host's. No nested Pi process, API key, OAuth store, classifier or provider fallback.
- Progressive disclosure: one short skill entry, branch-specific references, compact allowlisted evidence.
- Local numerical calculations and hashes; no LLM arithmetic or raw 10 MiB CSV in context.
- Stateless CLI startup; no daemon, open listening socket or idle model memory.
- Explicit artifacts let a new session resume without re-collecting the machine or inheriting opaque conversation state.
- Zero npm runtime/build dependencies; Node 22+ is the only portable runtime. The original fixed Windows collector remains PowerShell 5.1 compatible.

These are structural reductions, not measured model-quality/latency claims. Real token and task-completion comparisons remain an evaluation milestone.

## Native development seam

The separate repository-only `native/` projects do not add imports or dependencies to the installable skill. `NeuroTune.Execution` contains pure request/approval/recovery policy, a Windows-only protected checkpoint store, existing-file/Registry mechanisms and a read-only app/service probe; `NeuroTune.ApprovalPreview` is a Windows-only read/confirm/recheck test, without a writer or recovery store. Its native source references only the standard library, not the old desktop core. The existing SDK is pinned and NuGet package sources are cleared. `npm run test:native` is opt-in developer tooling, not part of the default portable tests or npm payload.

The in-process approval object has no external serialization/approval endpoint. It binds raw request bytes and bounded raw original-state snapshots to local machine/user identity, expiry and one use. The preview's `--bundle` path privately invokes the same protected Node validators instead of maintaining a second report/proposal validator in C#. This fixed, elevated Node child is explicitly in the prototype's trusted computing base, with a clean environment, no addons, bounded pipes/heap and a deadline; it is not exposed as an arbitrary subprocess endpoint. Schema-2 intent binds the exact bundle SHA-256, actual validated review/assessment IDs and full selected review. Only the native parent accepts its privately derived output, parses typed scope, captures originals and obtains human confirmation; standalone intent/receipts are rejected as input. Production host authorization/elevation and shipped setting/restore adapters remain unavailable; the native protected checkpoint store has separately scoped VM acceptance. Keep `systemWrites:false` and the existing skill stop behavior until those gates are accepted.

The pure `RecoveryJournal` adds bounded checkpoints and write-ahead/reverse-recovery policy without OS writes. `ProtectedJournalStore` supplies its durable-commit boundary on fixed Windows NTFS storage, with protected ACLs, a cross-process lease and immutable verified generations; that boundary is not approval. Default tests use memory only; explicit VM tests qualify the filesystem implementation without a Registry/setting writer. Restarted unfinished work cannot inherit permission to continue applying. See [RECOVERY-DESIGN.md](RECOVERY-DESIGN.md) for ordering, poisoning on ambiguous persistence, drift conflicts and outstanding Windows storage/identity gates.

Repository-only `RegistryTarget`/`PreparedRegistryDword` now implement one existing-key DWORD action under a live native identity lease and KTM transaction, consuming an in-process grant and persisting protected intent before a value change. This is a mechanism, not a released executor: no installable command reaches it and VM grants are synthetic. Security exclusions reuse the desktop parametric policy rather than defining tweaks. Schema-2 checkpoints now retain original and acknowledged key name/security/time fences; explicit protected-record recovery from another process and after a VM power cycle is implemented. Missing acknowledgement, legacy records without native fences and target drift block automatic writes. These metadata fences are not immutable object IDs or hardware attestation. See [REGISTRY-ADAPTER.md](REGISTRY-ADAPTER.md). The existing-game-file mechanism shares that protected store through a separate closed codec, with exclusive native handles, exact UTF-8 bytes/timestamps and drift-aware fresh-process/power-cycle recovery; see [GAME-FILE-ADAPTER.md](GAME-FILE-ADAPTER.md).

## Migration, not a clean-room rewrite

The old repository remains authoritative for the desktop app and pending recovery. Its graph was queried for `OptimizationCatalog`, `OptimizationRun`, `LlmClient`, `SystemProfiler`, `MeasurementService`, `PresentMonImporter`, the Agent command switch and hardware collector.

The useful reusable seam was **not** the entire Agent executable: that entry point instantiates provider settings, action catalog, backups and workflow state for unrelated commands. Reusing it would carry unwanted authority and dependencies into a read-only skill. The hardware collector is already standalone and was copied byte-for-byte. Frame numerical conventions were ported, with stricter input rejection and no unsupported claim of full measurement parity.

Future Windows-native code should be extracted selectively from the existing C# core, retaining its tests and licensing, instead of translating privileged operations to JavaScript. Packaging Node for orchestration does not imply rewriting the executor in Node.

## Windows 11 and harness-owned interaction

Windows 11 is the only current product target. macOS/Linux support and remote cross-OS execution are out of scope; retain existing pure-code portability and defensive checks without expanding or deleting historical acceptance evidence.

### Default harness permissions

NeuroTune inherits the harness's existing tool/command permission policy. It does not configure, detect or reproduce permission modes. The harness handles any command approval it requires; in allow-all/bypass mode NeuroTune adds no second command-permission prompt. Context/privacy questions and the final human review of the proposed plan remain workflow steps, not replacements for OS/tool authority. Standard Windows elevation may still be required. Internal lab binding objects do not justify a separate product permission system. This clarification does not enable an unimplemented executor or make a model-authored approval field authoritative.

All user interaction belongs to the existing harness: CLI agents, GPT apps or Claude Code desktop when the particular host actually exposes the required local tools. There is no separate NeuroTune window, wizard, dashboard or frontend. Use native question/approval controls where available and concise numbered chat choices otherwise. Keep machine-readable CLI JSON separate from user-facing summaries. Ask at most two relevant questions at once, offer Not sure, then present selected effects, evidence/uncertainty, risk/restart, backup/recovery and verification before Approve all / Choose steps / Other.

The native backend remains headless and uses the ordinary harness tool authorization and required OS privileges; NeuroTune does not implement a second command-permission policy. The harness presents the final plan for confirmation as workflow, while typed execution independently checks exact bytes, current state and protected recovery. Model-authored Yes/JSON flags cannot grant OS/tool authority. Standard Windows UAC, when necessary, is an OS prompt, not a NeuroTune application UI. Hosts without local execution/elevation must report unsupported actions. The product typed-command installation/interface is not shipped yet. The WinForms preview is a superseded repository-only lab experiment, not a product dependency or a remaining manual prerequisite.
