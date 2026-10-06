# NeuroTune v0.8.0-alpha.1

## AI-led Investigation Preview

An **unsigned prerelease for controlled Windows 11 x64 testing**, not a promise
of optimal tuning or measured performance gains. Keep an independent backup;
use a disposable VM for system-changing acceptance tests.

## Highlights

- **One complete diagnosis:** local scan, workload preparation, three automatic
  matching WPR captures, local quality checks and then the selected AI's
  investigation. Without a running workload, a single system-wide diagnostic
  remains non-benchmark evidence and cannot unlock apply.
- **Real progress and cancellation:** actual phases, bounded operation logs,
  elapsed/countdown timers and owned-recorder cleanup. Both assistants stay
  inactive during measurements; the existing watchdog finalizes captures if
  the app closes. Completed reports are retained on investigation failure.
- **AI-led, bounded follow-ups:** the main model can challenge local hypotheses
  and request fixed read-only Windows readers for counters, pressure, storage,
  displays, devices, networking, services, recent event metadata and observed
  drivers. The default 12-turn/10-minute budget is configurable; no arbitrary
  shell, model-supplied query/path, web research or firmware operation is a tool.
- **Four prompt specializations:** overall performance, system latency, network
  optimization and system stability. These guide reasoning on every turn, not
  predetermined tweaks. Risk, evidence, budgets and approval stay independent;
  saved FPS/efficiency goals retain their original meaning.
- **Optional supporting files:** up to 8 reports/screenshots, including 4 images,
  with editable inert-text previews, local preparation and explicit privacy and
  model-vision consent. Reports are limited to 40,000 characters each / 80,000
  combined; prepared images are PNG, at most 1600 px per side and 768 KiB each.
  These remain unverified user-supplied sources. Images are not anonymized and
  may be sent on every investigation turn; pixels are not saved in local run
  history. No external application is launched and no image/model fallback or
  separate OCR provider is silently used.
- **Official ChatGPT browser authorization:** PKCE, verified identity, encrypted
  per-user credentials, saved accounts and a native model dropdown. Eligible
  plan/credits usage stays separate from API-key billing; no cookie/Codex
  impersonation or automatic billing fallback. Preview eligibility, models and
  usage availability are controlled by OpenAI, not guaranteed by NeuroTune.
- **Optional System One assistance:** default-off managed local Rizzo Flow topic
  classification with pinned downloads, explicit installation consent, a real
  install smoke test and one-shot non-admin workers, or a separately consented
  OpenRouter classifier with its own encrypted key/model and API costs. Advice
  is uncalibrated, non-causal and cannot replace evidence or authorize writes.
  Elevated launches require Windows' verified linked standard-user UAC token
  with the same user and medium integrity; otherwise they refuse (including
  environments without UAC split tokens). No desktop ACL changes or
  administrator fallback are attempted. Non-admin execution is not a
  filesystem/network sandbox.
- **Optional export-only firmware support:** explicitly approved local SCEWIN
  packages can export settings; offline text imports remain unverified. Hashes
  identify files, not vendor authenticity. Driver/elevation risks and unknown
  values are visible; there is no BIOS import/write or security-bypass command,
  automatic SCEWIN download, or claim of motherboard compatibility.
- **Review and decline:** risk-ordered proposals show evidence, trade-offs,
  uncertainty, prerequisites and reversal. Outside-catalog ideas remain manual
  guidance/inert scripts, not write authority. Finish without changes safely
  closes a prewrite run while preserving the report. AI failure never becomes
  a deterministic executable substitute plan.
- **Read-only comparison explanation:** the selected AI can explain valid
  aggregates in the chosen focus without altering metrics or keep/rollback
  decisions. Improved measurement charts preserve missing-evidence and
  interrupt-attribution caveats.
- **Readiness-driven dark startup:** branded pre-JS presentation, no artificial
  minimum delay and no wait for optional model discovery. Restrained CSS motion
  stops for reduced-motion users and observed recording intervals.
- **Calmer desktop interface and modern identity:** a geometric signal-path N
  across startup, overview, sidebar, favicon and Windows icons; grouped provider
  controls, clearer review selection and trace-history hierarchy. Motion's
  shared navigation indicator stops immediately for quiet/reduced policies.
  Printed reports retain manual/script proposals and provenance.
- **Native watchdog pipe isolation:** standard response handles are made
  non-inheritable before capture startup. The watchdog no longer holds the
  Rust IPC pipes open until the capture deadline, restoring timely Start,
  Stop and Cancel without removing the independent watchdog.

## Execution and privacy boundaries

NeuroTune still executes only registered, reinspected capabilities after
matching persisted approvals, separate high-risk consent, required restore
points/backups and verification. Manual scripts are inert. System-wide captures,
user reports and screenshots cannot substitute for valid workload baselines.
Scheduling/ISR/DPC traces do not measure FPS, ping/jitter or end-to-end input
latency; attribution, configuration, source grades and heuristics are not
causal proof.

Prepared report text and file names/hashes remain in local run evidence;
screenshot pixels are transient. Best-effort redaction is not guaranteed
anonymization. Review all previews before consenting to cloud transmission.

## Source security fixes — candidate validation pending

Current sources harden absolute Windows utility resolution, bounded process
output/deadlines, protected recovery journals and complete rollback validation.
GPU/custom-plan recovery survives missing disposable inputs; core parking
restores the captured scheme. Opposing actions and mismatched journal IDs are
rejected. PresentMon imports require process attribution but retain unverified
provenance; missing coverage/new regressions cannot justify favorable Keep.
NuGet CI now rejects reported vulnerabilities and incomplete audits.

These changes are not certified by existing installers. A final source commit,
fresh CI/audits and a rebuilt exact-candidate Windows 11 VM pass are required.
See [security and upgrade safety](SECURITY.md).

## Validation and remaining acceptance

- Latest local **mixed working-tree** validation: **115 selected .NET, 33 UI
  and 2 Rust tests passed**, plus format/build/typecheck/lint/clippy. Backend
  WindowsIntegration, persisted-session Stop and synthetic DPAPI persistence,
  and Rust subprocess-tree cancellation were excluded. Earlier 99-test/native
  checks are historical. These results do not certify the selectively committed
  sources, production journal ACLs or an installer. Elevated CI without a
  verified standard-user token checks safe worker refusal, not inference.
  Rust audit is a CI gate; fresh online audits remain pending. Vitest/mocker
  in the local lockfile is patched to 4.1.11 for GHSA-82fw-gwwq-j7x9.
- Mocked browser checks cover orchestration, four presets, separate consent,
  final review/decline, keyboard, reduced motion and a narrow/scaled layout.
  Supporting-file checks use the published agent for actual local normalization,
  PNG bounds, report redaction and hashes; recording and main AI are mocked.
- An instrumented Release WebView2 on Windows 11 build 26200 passed real
  rendering/light/dark/keyboard/reduced-motion checks and elevated WPR
  watchdog/analysis/Stop/Cancel smoke. The 30-second system-wide trace was
  quality-valid with zero lost events. Test-owned data/raw ETL were deleted;
  WPR was stopped and no Agent/Telemetry processes remained. Production was
  rebuilt without diagnostic browser flags; tested Agent/frontend hashes match.
  This predates the source security fixes and is not exact-candidate installer
  acceptance, a game benchmark, overhead evaluation or actual DPI acceptance.
- Real elevated complete diagnosis against a representative workload, all
  cancellation/app-close paths and native cold-start anti-flash presentation
  still need desktop acceptance. Historic VM results are not fresh validation
  of this build.
- Live main-provider/vision planning, fresh ChatGPT account/refresh/revocation
  variations, managed local installation/inference/resource use and authorized
  auxiliary OpenRouter calls remain separate acceptance work. Existing-account
  ChatGPT model discovery succeeded; this does not prove planning or eligibility
  for other accounts.
- No compatible approved SCEWIN package has established vendor authenticity,
  driver/export safety or board compatibility. Export execution remains untested.
- Full Narrator and physical 150%/200% scaling acceptance, thermal/measurement
  repeatability and performance comparisons against a generic CLI prompt remain
  pending. No benchmark benefit or optimality guarantee is claimed.
- CPU-Z/GPU-Z vendor-specific report adapters, GUI automation, automatic image
  acquisition, external application/driver catalogs, firmware writers and
  supervised GPU IRQ-affinity writes remain unsupported or gated. The first
  reviewed external artifact and version-feed adapters are still pending.

See [investigation and execution boundaries](docs/DETERMINISTIC_CONTROLS.md),
[optional components](docs/SCEWIN_SYSTEM_ONE.md), the
[test guide](docs/TESTING.md) and the
[historical validation matrix](docs/VALIDATION_MATRIX.md).

## Distribution and verification

Stable/general distribution remains blocked. Existing v0.8 assets predate the
security corrections; rebuild and validate one definitive candidate before
publishing replacement assets. Actual protected-store ACL creation/denial,
UAC/sidecar integrity, interruption/I/O recovery, missing dynamic inputs and
legacy upgrade handling remain VM gates.

The packaging outputs are an unsigned per-machine NSIS installer, a portable ZIP
with both self-contained agents and `SHA256SUMS`. Windows SmartScreen may warn:
PrimeBuild does not provide an Authenticode signature for this free alpha.
Verify the downloaded asset's SHA-256 against `SHA256SUMS` before running it.

"Portable" means installation is not required, not that elevation or persistent
data is unnecessary. Settings, measurements and DPAPI-encrypted credentials live
under `%LocalAppData%\NeuroTune`; privileged operation/run journals and Registry
exports now live under `%ProgramData%\NeuroTune-journals\<Windows user SID>`.
Legacy LocalAppData `operations`/`runs` are not imported: finish/review recovery
with the previous build, preserve independent backups and archive reviewed
journals only after resolving old writes. Never delete pending recovery or
copy legacy JSON into the protected store to bypass the block.

The ZIP/installer contains no user's credentials, optional model weights,
SCEWIN binaries or vendor drivers. Optional downloads require their own consent
and third-party license/trust review; NeuroTune's MIT license does not grant
redistribution rights to those packages.
