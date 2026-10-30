# Security and privacy boundaries

This is currently a read-only alpha with guided assessment and non-executable proposal review. Controlled system changes (an Apply/recovery path) are a future direction, not yet designed or shipped.

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

## Deferred privileged execution

Adding Apply requires a typed executor that runs through the harness's ordinary tool permissions and actual OS authority, with exact action binding, protected write-ahead snapshots, crash recovery, verification and honest recovery limits — not yet designed, let alone implemented. Final plan confirmation remains in the existing harness; do not add another command-permission policy or custom approval UI. An `approved: true` field, CLI `--yes`, chat approval token generated by the agent, or MCP confirmation annotation is not sufficient authentication.

This project does not share the earlier desktop app's legacy journals or provider stores; that app's own recovery path, if still installed, remains separate and untouched.

## Reporting issues

This project is local and not yet published. Report suspected vulnerabilities privately to the maintainer; share a minimized synthetic reproducer, not personal hardware reports, credentials or full traces. Do not treat a future build badge as hardware or privilege-boundary acceptance.
