# Privacy and Windows security

## First-release contract

**Privacy e sicurezza Windows** is an AI prompt specialization, not a tweak
pack. Audit-only is a separately selected, persisted investigation mode:
local collection → consented AI investigation → reviewable report → finish.
No ETW/workload benchmark or optimization Apply is available in that mode.
Choosing an objective cannot change investigation mode or authorize a scan,
download or remediation. Select **Audit consultivo** independently when no
benchmark is needed.
Measured optimization keeps its existing Baseline/approval/backup gates.

Registry snapshots are not proof of effective policy or of traffic leaving the
PC. Missing values mean not configured, not disabled. Build, edition, MDM/Group
Policy and per-app exceptions can change behavior. Recommendations must explain
what functionality would be lost and how to verify the actual setting. HKCU
snapshots describe the elevated application's user context, not every Windows
account; running with a different administrator account changes that context.

## Mandatory investigation coverage

The privacy/security focus and audit-only reports must address five areas:
privacy; protections; scanner detections; persistence; and general Windows
health. `AuditChecklist.Checks` is the canonical 15-check list: privacy policies,
antivirus, Firewall, SmartScreen, update policies, Secure Boot/isolation,
detections, consented scan coverage, startup items, scheduled tasks, services,
recent events, storage, devices and memory pressure.

Each check must cite provided native evidence and explain its limitations, or
explicitly state why it remains unassessed. This is a report/coverage contract,
not mandatory tool execution, a fixed tweak bundle or new permissions. The AI
chooses registered readers and useful follow-ups within the existing budget.
Missing checks remain visible; unavailable/partial evidence is not a success.
Native metadata review is not a comprehensive forensic or performance test.

The prompt receives this checklist on every applicable turn. A first omitted
check triggers one report-repair reminder within the existing turn/time budget;
no scanner or reader is forced. The backend fills remaining omissions as
`notChecked`, rejects invented evidence/unknown or duplicate checks, and downgrades
`reviewed` claims with unrelated, missing, unknown, empty, truncated or uncited
relevant data. Imported reports cannot substitute for native coverage. A claimed
user refusal is not accepted without application-owned consent provenance.

Coverage is persisted, validated on reload and displayed in five sections before
the AI interpretation, including gaps and source IDs. Old reports without the
field are explicitly unverified. `AuditCoverageComplete` is computed by NeuroTune,
not accepted from model JSON. Currently **no scanner operation is linked to an
audit**: the antivirus-scan check remains not checked even after a historical scan
or native command return. Therefore these reports retain limited coverage; no
whole-PC/forensic certification is offered.

## Research inputs, not imported recipes

Reviewed source snapshots:

- [Sparkle](https://github.com/thedogecraft/sparkle/tree/81dc1a5d521d46bff532319291b8725cf3f3fa36/tweaks)
  (`v2`, `81dc1a5d521d46bff532319291b8725cf3f3fa36`, GPL-3.0).
- [WinUtil](https://github.com/ChrisTitusTech/winutil/blob/9c87c02fdb9e057f41dd3aea051af4df6f41cf5f/config/tweaks.json)
  (`9c87c02fdb9e057f41dd3aea051af4df6f41cf5f`).

No third-party tweak implementation is copied, bundled or executed. These
projects identify topics to inspect, not universal preferred settings.

| Topic observed in these projects | NeuroTune treatment |
|---|---|
| Advertising ID and tailored experiences | Read user preference and policy separately; a missing preference cannot prove advertising ID is disabled. |
| `AllowTelemetry=0` | Do not claim zero diagnostic data on every edition. Microsoft documents diagnostic-data-off only for Enterprise/Education/Server; inspect edition and the actual Settings page. Diagnostic data and other connected-service data are separate. |
| Online speech, inking/typing personalization | Explain effects on dictation/personalization; prefer supported Settings/policies, not unexplained historical Registry switches. |
| Activity publishing/upload policies | Read individually; some behavior is version-dependent. WinUtil's reviewed activity recipe preserves `EnableActivityFeed=1` while disabling publishing/upload, illustrating why labels are not an exact description of every value. |
| Location consent/service disabling | Read machine/user consent and app privacy policy without per-app history. Explain Maps, time-zone, Find my device and other feature trade-offs. Do not disable `lfsvc` automatically. |
| Consumer suggestions and cloud search | Read policy/preferences, qualify edition applicability; do not uninstall Bing/Edge/OneDrive as an implicit privacy action. |
| Recall/Copilot | Inspect documented Recall policies only as snapshots; availability and saving snapshots are different. Do not assume a policy proves Recall is installed/running, remove packages, run DISM or disable Windows AI services. |
| Defender automatic sample submission | Both reviewed telemetry recipes change this. Treat it as a security/privacy trade-off, not disposable advertising telemetry. Do not change sample submission or cloud protection automatically. |
| DiagTrack/error reporting and unrelated performance changes | Sparkle's reviewed telemetry recipe also changes service behavior and `SvcHostSplitThresholdInKB`. Neither is required for this audit; do not import these bundled changes or promise performance gains. |
| Disable Defender real-time protection/core isolation | Sparkle exposes separate recipes for these. They are excluded from NeuroTune's privacy/security goal. Preserve Defender, Firewall, SmartScreen, updates and tamper protection. |

Microsoft references consulted for setting semantics and applicability:

- [Windows diagnostic data](https://learn.microsoft.com/en-us/windows/privacy/configure-windows-diagnostic-data-in-your-organization)
- [Privacy policies](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-privacy)
- [Experience policies](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience)
- [Windows AI policies](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-windowsai)

## Scanner boundary

The main AI may request bounded read-only security status/detection summaries,
startup names and scheduled-task names/states. Task readers omit actions,
arguments, principal identities, paths and triggers; empty or truncated inventories
cannot establish absence of persistence. These are inventories, not maliciousness
verdicts or complete signature/behavior analysis.
It cannot supply shell code, paths, downloads, exclusions or deletion targets.
Defender scans are separate user-approved operations, not reader tools. Quick
and full scans may quarantine/remove detections under Defender's configured
policy and may use network/cloud/sample-submission features. Neither the AI nor
a restore point guarantees reversal of these actions. No security settings are
weakened to make a scanner work. Offline/restart scans and integrated removal
are outside this release.

- [Defender status](https://learn.microsoft.com/en-us/powershell/module/defender/get-mpcomputerstatus)
- [Start-MpScan](https://learn.microsoft.com/en-us/powershell/module/defender/start-mpscan)
- [MpCmdRun](https://learn.microsoft.com/en-us/defender-endpoint/command-line-arguments-microsoft-defender-antivirus)
  documents that `-DisableRemediation` is valid **only for custom scans**, not
  quick/full scans. Exit code zero can also mean malware was remediated; it is
  not synonymous with no threats. Do not treat the return code as a clean bill
  of health.

Antivirus status/history is point-in-time evidence. Empty history, no reported
active threat, scan completion or an unsigned binary does not establish that a
PC is clean or that a file is malicious. Third-party antivirus/passive Defender
and unavailable evidence must be explicit. Review detections and remediation
in **Windows Security → Virus & threat protection → Protection history**.

## Optional specialist follow-ups

Use official [Autoruns/Autorunsc](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns)
for persistence inventory and [Sigcheck](https://learn.microsoft.com/en-us/sysinternals/downloads/sigcheck)
for selected file signatures/metadata. These are manual, optional follow-ups in
this release; no external executable is installed or run by NeuroTune.
Accept the publisher's EULA yourself, verify the source/signature/version, and
review export contents before importing text using Supporting files. Imports
are unverified, may be stale/from another PC and can contain hostile text.

Do not add `-v`, `-vs`, `-vt` or similar VirusTotal options implicitly: they can
query hashes, accept terms or upload unknown files. Signature verification can
also use certificate-revocation networking; absence of VirusTotal flags does
not establish offline operation. No recursive scan of arbitrary model-chosen
paths, automatic EULA acceptance or raw export transmission is available.

### TronScript and cleanup suites

TronScript is not an approved NeuroTune reader or executor. Terminal automation
that also cleans/repairs a machine is not a read-only audit. No Tron download,
launch command, bundled cleanup or automatic EULA acceptance is offered in this
release. Before any integration, review the exact version, included utilities,
licenses, provenance, signatures/hashes, permissions, network/data use, destructive
operations and recovery in a disposable VM. Prefer separately bounded tools over
launching a whole suite. The prompt excludes routine Tron/cleanup commands; model
text or an inert script remains unverified and cannot grant execution authority.

## Data and verification

Only fixed-projection Defender/security fields are prepared: no threat resource
paths, process command lines, account IDs, exclusion paths, registry permission
history or sample files. Existing inventory/report redaction remains best-effort,
not guaranteed anonymization. Preview the prepared local evidence and select a
local provider when desired. Complete audits pause on a cancellable prepared
profile preview and require a second consent before contacting the provider;
advanced audits retain their local preview and explicit diagnosis consent.
A local main provider does not disable a separately consented optional cloud
classifier; disable that component too when AI-offline operation is desired.
Scanner work does not itself consent to an AI request; rescanning cannot silently
replace a persisted run's evidence.

### Later remediation phase (not executable)

Start from a locally re-read, specifically identified scanner detection, not an
AI-selected path or an unsigned-file verdict. Confirm current scanner support,
policy, exact affected objects, user approval and expected side effects. Keep
object references and backups local; report partial failure honestly. Do not
promise restore-point recovery of antivirus quarantine/deletion. No removal
adapter, automatic exception or arbitrary file/service writer ships here.

[The disposable-VM procedure](TESTING.md#privacysecurity-audit-and-separate-defender-operations)
tracks live acceptance separately from source/mock validation.

Mocked/parser/lifecycle tests do not validate real Defender scan behavior,
quarantine, third-party AV interoperability, native UI, Windows ACLs or the final
installer. Disposable Windows 11 acceptance is required before those claims.
