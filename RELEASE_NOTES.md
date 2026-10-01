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
  Elevated non-interactive Windows sessions are explicitly refused; no desktop
  ACL changes or administrator fallback are attempted. Non-admin execution is
  not a filesystem/network sandbox.
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

## Validation and remaining acceptance

- Local automated validation: **99 .NET, 28 UI and 2 Rust tests passed**, plus
  typecheck/lint, formatting and production/native builds. Headless elevated
  CI checks safe worker refusal, not desktop inference; local unelevated tests
  exercise Unicode/environment isolation and cancellation. Rust audit is a CI
  gate. Vitest/mocker is patched to 4.1.11 for GHSA-82fw-gwwq-j7x9.
- Mocked browser checks cover orchestration, four presets, separate consent,
  final review/decline, keyboard, reduced motion and a narrow/scaled layout.
  Supporting-file checks use the published agent for actual local normalization,
  PNG bounds, report redaction and hashes; recording and main AI are mocked.
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

The release assets are an unsigned per-machine NSIS installer, a portable ZIP
with both self-contained agents and `SHA256SUMS`. Windows SmartScreen may warn:
PrimeBuild does not provide an Authenticode signature for this free alpha.
Verify the downloaded asset's SHA-256 against `SHA256SUMS` before running it.

"Portable" means installation is not required; settings, reports, encrypted
credentials and rollback data still live under `%LocalAppData%\NeuroTune`.
The ZIP/installer contains no user's credentials, optional model weights,
SCEWIN binaries or vendor drivers. Optional downloads require their own consent
and third-party license/trust review; NeuroTune's MIT license does not grant
redistribution rights to those packages.
