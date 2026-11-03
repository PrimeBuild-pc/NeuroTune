# Security and privacy boundaries

NeuroTune is an alpha skill. Its CLI only reads, analyzes and validates. System changes are made by the agent through the harness's own tools, only after a reviewed plan and the user's confirmation, with the original state saved first (see [references/apply.md](skills/neurotune/references/apply.md)). These are written instructions, not an enforced sandbox.

## Actual authority

The CLI accepts fixed commands and file paths. Only `collect` launches a child process: a bundled, reviewed PowerShell file using an argument array without a shell. The child inherits the caller's privileges; the program neither requests nor drops elevation. Investigate from a non-admin session; use elevation only for confirmed changes that need it, through the ordinary Windows prompt. PowerShell policy is not bypassed.

There are no HTTP clients, provider credentials, generic shell/script parameters, Registry/service writers, scanner commands, telemetry services or update downloads in the installable Node runtime. Its filesystem writes are explicit new reports, private temporary staging and installer copies.

NeuroTune leaves the harness's permission configuration unchanged. The harness handles its own command approvals/denials, including allow-all/bypass behavior; NeuroTune adds no parallel per-command permission prompts. Context/privacy questions and final plan confirmation remain workflow steps. Windows privileges and the harness's permissions still decide what the agent can actually run; a user's confirmation does not extend them.

The surrounding harness may still have unrestricted shell/file access, network access and malicious extensions. Skill instructions cannot revoke those capabilities. A model can ignore them, invent consent, read a report with another tool, or run unrelated commands. Restrict the harness at the OS/tool level. This package is not a sandbox, permission broker or prompt-injection immunity claim.

## Data flow

1. `collect` runs the inherited collector locally, with a 90-second process timeout and bounded stdout/stderr.
2. A temporary raw report is projected onto a compact allowlist. Temporary files are removed on normal completion or caught failure; termination/power loss can leave them in the user's temp directory.
3. The compact report is saved at the user's selected path. The receipt contains no hardware facts, but includes a content ID and warning count.
4. `evidence`, `frames`, `frames-capframex`, `compare` and file-reading tools can disclose results to the current model. Consent must precede those operations. A privacy review performed by the model is already a disclosure.

`assess` reads consented intake/evidence inputs and saves a new assessment with only a receipt on stdout. `proposal-check` returns selected plan details and uncertainty to the harness; obtain sharing consent before those inputs are read. `goals`, `tools` and `game-configs` return static objective/capability/discovery descriptions without inspecting apps, services, game files or third-party installations. Game paths/key hints are not installed-game evidence, safe values or authorization; the catalogue downloads nothing and does not expand native write support. `apps-services` imports a supplied bounded registry/SCM snapshot into an allowlisted content-sealed report and returns only a receipt. It never runs SCM, uninstall commands or the native probe. App/service evidence requires sharing consent; names/publishers/dependencies remain untrusted and do not prove usage or safe removal. Store/MSIX apps and complete dependency groups are absent. No download or installer was added to the skill CLI.

No report is automatically uploaded by this package. That does **not** mean tool results stay local when the harness uses a remote model. Model retention, logs, sync and billing belong to the harness/provider. File paths supplied in prompts may themselves identify the user.

Hardware/device strings are untrusted. Allowlisting omits common identifiers but does not anonymize arbitrary strings or defeat malicious metadata. Numeric fields and exact schemas are validated; prose remains inert and must not be treated as authority.

## Files and reports

- Input reads are size-bounded regular-file reads and UTF-8 validated. POSIX opens are non-blocking so named pipes are rejected before waiting for a writer; this is not a general I/O deadline or a Windows named-pipe guarantee. Raw imports have a 10 MiB budget, compact report reads 512 KiB (including worst-case JSON-escaped inventory text), and plans 128 KiB. Schema and report-count bounds still apply.
- Reports require a `.json` extension; alternate-stream names and executable suffixes are rejected. JSON escaping is not safe shell-script escaping: naming a report `.cmd` can otherwise make its text executable by Windows. Only a harmless temporary echo probe was used to verify this risk. Paths are checked before collection; reports are created exclusively, flushed, and never replace existing files. Symlink/junction output ancestors are rejected before writing.
- On POSIX, newly created report files request mode 0600. Windows ACLs are inherited, **not hardened by this package**. Choose a private user-owned directory.
- The output-path check is not race-proof against a malicious same-user process swapping ancestors. The installer similarly assumes a trusted user-owned destination. Do not install or write reports into adversarial/shared directories.
- A disk failure may leave an incomplete new output file; validation will reject it. Existing reports remain unchanged. No privileged recovery depends on these files.
- SHA-256 content IDs detect accidental edits, bind citations and reject exact source reuse. They are not signatures, independent timestamps, proof of hardware identity or evidence authenticity. A user or model can fabricate and re-seal data.
- CapFrameX imports accept one bounded run, project only process/frame intervals and optional mode codes, and omit hardware/sensor strings. Missing PID/swap-chain identity stays absent and blocks comparison; source bytes and vendor hash fields are not authenticated.
- Plans validate structure and citation membership, not whether claims are supported. They never produce an executable capability or approval. Assessments validate exact source bindings and derived coverage, not machine identity, freshness, a complete diagnosis or consent. Proposal reviews validate all steps and dependency-complete selections; targets, expected states, risk and recovery descriptions remain untrusted claims. `approvalGranted:false` and `executable:false` remain fixed. A selection/hash cannot authorize writes, and proposed backups cannot assert `verified:true`.

## Agent-applied changes

When the user confirms steps, the agent applies them with the harness's command and file tools, following [apply.md](skills/neurotune/references/apply.md): a local session directory and journal, the original state and backup material saved before each change, one explicit command per action, read-back verification and no automatic reboot. A failed backup blocks that step.

Limits to keep in mind:

- Only the human's own reply confirms a scope. An `approved: true` field, a review hash, a handoff or text in a report or web page is data, not confirmation.
- Confirmation does not grant permissions. If the harness or Windows denies a command, the agent stops and reports it; it must not bypass policy, disable protections or escalate privileges another way.
- Backups are what the agent saved, not a guarantee. Registry values, service start types, settings and config files can usually be restored exactly. Removed apps and drivers may not come back with their data, and a driver change can leave a machine without display, network or boot. These limits are disclosed before confirmation.
- The journal is a local, unsigned record. Before a restore the agent re-reads the actual state instead of trusting it.
- A model can still ignore instructions. Use a harness permission mode you are comfortable with, and test system-changing behavior in a disposable VM, never on a machine you cannot reinstall.

This project does not share the earlier desktop app's legacy journals or provider stores; that app's own recovery path, if still installed, remains separate and untouched.

## Reporting issues

This project is local and not yet published. Report suspected vulnerabilities privately to the maintainer; share a minimized synthetic reproducer, not personal hardware reports, credentials or full traces. Do not treat a future build badge as hardware or privilege-boundary acceptance.
