# Security and privacy boundaries

This is currently a read-only alpha with guided assessment and non-executable proposal review. The [build destination](docs/BUILD-STEPS.md) includes controlled system changes, but no optimizer executor is shipped. A repository-only Registry mechanism is tested on synthetic VM fixtures, not exposed to the skill or qualified as a security product.

## Actual authority

The CLI accepts fixed commands and file paths. Only `collect` launches a child process: a bundled, reviewed PowerShell file using an argument array without a shell. The child inherits the caller's privileges; the program neither requests nor drops elevation. Run the harness non-admin. PowerShell policy is not bypassed.

There are no HTTP clients, provider credentials, generic shell/script parameters, Registry/service writers, scanner commands, telemetry services or update downloads in the installable Node runtime. Filesystem writes are explicit new reports, private temporary staging and installer copies. Tests do not exercise system writers because none exist.

NeuroTune leaves the harness's permission configuration unchanged. The harness handles its own command approvals/denials, including allow-all/bypass behavior; NeuroTune adds no parallel per-command permission prompts. Context/privacy questions and final plan confirmation remain workflow steps, while OS privileges, target checks and mandatory backups remain real execution prerequisites.

The surrounding harness may still have unrestricted shell/file access, network access and malicious extensions. Skill instructions cannot revoke those capabilities. A model can ignore them, invent consent, read a report with another tool, or run unrelated commands. Restrict the harness at the OS/tool level. This package is not a sandbox, permission broker or prompt-injection immunity claim.

## Data flow

1. `collect` runs the inherited collector locally, with a 90-second process timeout and bounded stdout/stderr.
2. A temporary raw report is projected onto a compact allowlist. Temporary files are removed on normal completion or caught failure; termination/power loss can leave them in the user's temp directory.
3. The compact report is saved at the user's selected path. The receipt contains no hardware facts, but includes a content ID and warning count.
4. `evidence`, `frames`, `frames-capframex`, `compare` and file-reading tools can disclose results to the current model. Consent must precede those operations. A privacy review performed by the model is already a disclosure.

`assess` reads consented intake/evidence inputs and saves a new assessment with only a receipt on stdout. `proposal-check` returns selected plan details and uncertainty to the harness; obtain sharing consent before those inputs are read. `goals`, `tools` and `game-configs` return static objective/capability/discovery descriptions without inspecting apps, services, game files or third-party installations. Game paths/key hints are not installed-game evidence, safe values or authorization; the catalogue downloads nothing and does not expand native write support. `execution-check` reads a self-contained evidence/assessment/proposal bundle and derives inert typed intent; it grants no approval and starts no subprocess. `apps-services` imports a supplied bounded registry/SCM snapshot into an allowlisted content-sealed report and returns only a receipt. It never runs SCM, uninstall commands or the native probe. App/service evidence requires sharing consent; names/publishers/dependencies remain untrusted and do not prove usage or safe removal. Store/MSIX apps and complete dependency groups are absent. No download or installer was added to the skill CLI.

No report is automatically uploaded by this package. That does **not** mean tool results stay local when the harness uses a remote model. Model retention, logs, sync and billing belong to the harness/provider. File paths supplied in prompts may themselves identify the user.

Hardware/device strings are untrusted. Allowlisting omits common identifiers but does not anonymize arbitrary strings or defeat malicious metadata. Numeric fields and exact schemas are validated; prose remains inert and must not be treated as authority.

## Files and reports

- Input reads are size-bounded regular-file reads and UTF-8 validated. POSIX opens are non-blocking so named pipes are rejected before waiting for a writer; this is not a general I/O deadline or a Windows named-pipe guarantee. Raw imports have a 10 MiB budget, compact report reads 512 KiB (including worst-case JSON-escaped inventory text), and plans 128 KiB. Execution bundles allow 12 MiB aggregate while retaining each embedded artifact's limit. Schema and report-count bounds still apply.
- Reports require a `.json` extension; alternate-stream names and executable suffixes are rejected. JSON escaping is not safe shell-script escaping: naming a report `.cmd` can otherwise make its text executable by Windows. Only a harmless temporary echo probe was used to verify this risk. Paths are checked before collection; reports are created exclusively, flushed, and never replace existing files. Symlink/junction output ancestors are rejected before writing.
- On POSIX, newly created report files request mode 0600. Windows ACLs are inherited, **not hardened by this package**. Choose a private user-owned directory.
- The output-path check is not race-proof against a malicious same-user process swapping ancestors. The installer similarly assumes a trusted user-owned destination. Do not install or write reports into adversarial/shared directories.
- A disk failure may leave an incomplete new output file; validation will reject it. Existing reports remain unchanged. No privileged recovery depends on these files.
- SHA-256 content IDs detect accidental edits, bind citations and reject exact source reuse. They are not signatures, independent timestamps, proof of hardware identity or evidence authenticity. A user or model can fabricate and re-seal data.
- CapFrameX imports accept one bounded run, project only process/frame intervals and optional mode codes, and omit hardware/sensor strings. Missing PID/swap-chain identity stays absent and blocks comparison; source bytes and vendor hash fields are not authenticated.
- Plans validate structure and citation membership, not whether claims are supported. They never produce an executable capability or approval. Assessments validate exact source bindings and derived coverage, not machine identity, freshness, a complete diagnosis or consent. Proposal reviews validate all steps and dependency-complete selections; targets, expected states, risk and recovery descriptions remain untrusted claims. `approvalGranted:false` and `executable:false` remain fixed. A selection/hash cannot authorize writes, and proposed backups cannot assert `verified:true`.

## Repository-only native development prototype

The 2026-10-29 product clarification supersedes the custom-window experiment: user interaction must remain in the existing harness on Windows 11. Do not require or relaunch the preview as a manual gate. The intended native backend is headless; its trusted host/OS approval integration remains unimplemented. Harness-owned presentation does not make ordinary chat or model-authored flags authenticated approval. Unsupported authorization must block writes. OS UAC may still be necessary for elevation; no separate NeuroTune product UI is planned.

`native/` is excluded from the npm/standalone skill payload. It contains a pure typed-request/approval library and an elevated Windows approval **preview**, not an executor. The preview reads bounded local HKLM SOFTWARE Registry64 originals and native machine/user identity, displays exact parameters and rechecks bindings after a human test confirmation. It has no Registry/file-setting Apply or rollback adapter or shell endpoint. A separate native protected-checkpoint store is now implemented and VM-tested below; it is not invoked by the skill CLI. No native process is launched by the skill CLI.

The lab module/runtime are installed only in the designated VM with admin-owned protected ACLs; the model account cannot write those files or invoke the lab's high task. Confirmation remains an in-process capability, not a field, flag, receipt or reusable credential. A genuine test-only confirmation was exported for the earlier candidate; it authorizes no changes and cannot transfer to a new candidate.

The new preview accepts only a complete `--bundle`, not a model-authored native request or validation receipt. It reuses the authoritative Node validators in a fixed protected child, privately streaming bounded bytes over anonymous stdio pipes. The protected Node binary and imported skill code are part of this prototype's elevated trusted computing base; this is not a sandbox. No user-supplied program/arguments, shell, inherited Node/OpenSSL options, addons, downloads or provider calls are allowed. The child has a 30-second deadline, bounded output and a 256 MiB V8 heap limit. Installation ownership/ACL/link checks cover its binary and sources; the lab verifies the OpenJS Authenticode signature and payload hashes. The parent independently checks the exact bundle SHA-256 and native typed schema. The complete selected review is displayed, and validation runs again before grant consumption. Closed parameter mapping requires the reviewed Registry target and desired value to match exactly; text is never interpreted as commands.

Citation membership and content seals do not authenticate evidence provenance, support a diagnosis or prove native target identity. Candidate-specific human-path verification, a production elevation mechanism, key identity/link/race defenses and protected crash-safe recovery remain release blockers. Passing synthetic tests is not privileged execution qualification.

## Native recovery policy and protected storage

The native library includes a bounded recovery checkpoint/state protocol and a repository-only Windows protected store. No approval grant is persisted; unfinished work resumes into recovery, never forward execution. Commit failures poison live instances; altered values/types/key existence produce conflicts rather than overwrite authority. SHA-256 detects corruption, not authenticity. Forged/copied/resealed checkpoints must never authorize recovery.

The fixed NTFS store uses Administrators/SYSTEM-only protected owner/DACLs, pinned directories, single-link regular files, a cross-process lease and immutable contiguous generations. CreateNew, WriteThrough, Flush(true) and byte readback acknowledge each generation without overwriting earlier originals. Bad/missing/partial/replayed latest records block processing, not fallback or automatic repair. Product typed-command installation/wiring, maintenance and independent release qualification remain unavailable; protected native metadata fences and explicit cross-process Registry restore now have scoped VM acceptance. A separate live-lease Registry mechanism now has synthetic VM acceptance, not a public Apply endpoint. Actual filesystem/ACL/token/process-crash/power-cycle tests ran only in the designated VM; default native tests remain memory-only. They do not prove arbitrary hardware power-loss behavior or authenticate model-authored evidence. See [RECOVERY-DESIGN](docs/RECOVERY-DESIGN.md) before connecting any OS adapter.

## Repository-only Registry mechanism

`PreparedRegistryDword` consumes only an opaque in-process grant, binds request/native context/original value and a live name/security/time key fence, then persists originals/attempt intent before committing a KTM mutation. One existing SOFTWARE/Registry64 key is supported; keys are never created or deleted. Symbolic links, untrusted owners/write principals, missing/null DACLs and native-name mismatch, security/autostart targets and MachineGuid are refused. Native policy normalizes WOW64 aliases. Creator-owner inheritance placeholders are not treated as enabled model principals; actual owners remain checked.

KTM rechecks preconditions transactionally and aborts/conflicts on competing writes; no compare-and-write shell fallback is allowed. Raw original types/bytes and absent-value restoration are verified under a live lease. Identity/time/security drift prevents restore and preserves a protected conflict. Schema-2 protected journals now carry original/acknowledged native metadata fences; explicit `RegistryRecovery` restores only under the actual context and exact persisted fence. Legacy records, unacknowledged commits and drift remain blocked. Tests explicitly mark in-process lab grants as synthetic, never human authorization. The installable skill still cannot apply or roll back. See [REGISTRY-ADAPTER](docs/REGISTRY-ADAPTER.md).

## Repository-only game-file mechanism

One existing user-profile UTF-8 config file is supported by the native SDK, not the installable CLI. Actual NTFS file/ancestor handles, native paths, single-link regular-file checks, owner/write ACLs, volume/file IDs, creation/write times and security hashes constrain scope. Captures and prepared hashes are rechecked; originals and attempt intent are committed before in-place writes. Explicit flush/readback acknowledges changes. Reverse writes preserve original contents and last-write time only under the exact acknowledged identity; partial/ambiguous writes, replacements and drift retain protected conflicts. This is not atomic file replacement or a sandbox against the same OS-authorized caller/other administrators. Unsupported encodings/paths fail closed. See [GAME-FILE-ADAPTER](docs/GAME-FILE-ADAPTER.md).

## Deferred privileged execution

Adding Apply requires the headless typed executor to run through the harness's ordinary tool permissions and actual OS authority, with exact action binding, protected write-ahead snapshots, crash recovery, verification and honest recovery limits. Final plan confirmation remains in the existing harness; do not add another command-permission policy or custom approval UI. An `approved: true` field, CLI `--yes`, chat approval token generated by the agent, or MCP confirmation annotation is not sufficient authentication.

Do not share the desktop app's legacy journals or provider stores with this project. Keep its recovery path intact. Privileged tests belong only in the designated disposable Windows VM, restored to its existing clean checkpoint afterward.

## Reporting issues

This project is local and not yet published. Report suspected vulnerabilities privately to the maintainer; share a minimized synthetic reproducer, not personal hardware reports, credentials or full traces. Do not treat a future build badge as hardware or privilege-boundary acceptance.
