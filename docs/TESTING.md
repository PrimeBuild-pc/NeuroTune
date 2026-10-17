# Alpha testing guide

NeuroTune is an unsigned Windows 11 x64 alpha, not a stable/general release.
Use a disposable VM with an independent backup/checkpoint for installation,
updates and every system-changing test. Do not use your primary PC to validate
Apply, rollback, recovery, drivers or antivirus remediation.

A successful build, model discovery or matching checksum does not certify
recovery, compatibility, a clean PC or a performance gain.

## Install and upgrade

1. Download the installer or complete portable ZIP and `SHA256SUMS` from the
   same [GitHub release](https://github.com/PrimeBuild-pc/NeuroTune/releases).
2. Compare SHA-256 before running the unsigned package. SmartScreen warnings
   are possible; hashes identify bytes, not an authenticated publisher.
3. Install in the disposable VM and approve the normal Windows UAC prompt.
   WebView2 is required. Extract the complete portable folder, not only the EXE.
4. Verify the visible version, light/dark/system theme, navigation, keyboard
   focus and Windows 100/150/200% scaling. Test Narrator separately.
5. On upgrade, retain old recovery data. Nonempty legacy LocalAppData
   `operations`/`runs` must block collection with the blocking paths shown.
   Resolve/review previous writes using the previous build, then archive only
   reviewed data outside the old location. Never delete pending recovery or
   import editable JSON into privileged storage. See [upgrade safety](../SECURITY.md).

## Provider and model selection

1. Choose a provider and enter its supported credential locally. Never share
   keys, passwords, tokens or unredacted profiles in feedback.
2. Select **Test & discover models**. API/local connections show an explicit
   **Available models** dropdown with the endpoint's returned IDs. Choose one,
   or enter the exact ID manually, then **Save securely**.
3. Verify discovery preserves a manually entered API model, changing endpoint,
   protocol or credential clears stale catalog choices, and restart preserves
   the saved selection. Startup must not contact a provider automatically.
4. ChatGPT plan has its own official browser authorization and automatically
   saved account-model selection; API billing is separate. See [provider guide](PROVIDERS.md).
5. Model discovery is not an inference, quota, image-support or diagnosis test.
   Real inference consumes the selected provider's allowance/credits and sends
   consented evidence. Do not retry authentication/quota failures without
   addressing their cause. No alternate model/provider/billing fallback is automatic.

## Read-only diagnosis and review

1. Run a local scan without requesting AI: hardware/Windows/driver/software
   evidence and unavailable fields should be explicit. Review prepared evidence;
   redaction is best-effort, not guaranteed anonymization.
2. For **Measured optimization**, select an already-running workload and keep
   a repeatable scene active. Complete diagnosis collects three matching traces
   and validates quality before AI. Without a workload it collects one
   system-wide diagnostic, which cannot establish FPS/input latency. Measurements
   are context for the AI, not a gate before Apply.
3. For **Advisory audit**, no ETW/workload benchmark or Apply is allowed.
   Complete audit pauses on the prepared evidence and requires explicit
   transmission consent. A preset alone never starts collection or scanning.
4. Verify actual phases, countdown, bounded logs and cancellation. During
   capture all decorative motion must stop and neither AI may run. Cancellation
   affects only the owned capture; completed reports remain local.
5. An explicitly requested AI diagnosis may use registered read-only readers.
   Unknown/unavailable observations are not zeros, proof of causes or executable
   commands. Inspect citations, uncertainty, risk, trade-offs and reversibility.
6. A provider failure must show its safe cause and **AI investigation unavailable /
   Not completed**, retaining local findings for review across restart. No
   automatic retry, success badge or Apply authority is allowed. Select
   **Finish without changes** to close only that prewrite run explicitly.
7. Supporting reports/images are optional unverified data, not native baselines.
   Review privacy and exact model image support before consenting. Scripts and
   downloads in AI proposals remain inert. See [investigation boundaries](DETERMINISTIC_CONTROLS.md).

## Development branch: setup and before/after measurements

These additions are source changes, not replacements for the published alpha.4
packages. First-launch **Guided setup** offers **ChatGPT**, **OpenRouter** and
**Other** (API-key/local settings) for the main connection. Signing in saves
only that connection, not optional permissions or diagnosis-transmission consent.
Verify identity-only ChatGPT retries preserve the account registration, catalog
failures retain the saved login, and **Load available models** retries the
current authenticated provider without another OAuth flow. Authentication/model
operations must freeze guide controls, including optional choices and dismissal.

The guide stages optional sensor/firmware choices;
only **Save choices and continue** persists them. Skip preserves existing
permissions. PawnIO and BIOS writing remain unavailable. Configuring System One
is not installing or enabling it: its separate consent/download controls remain.
The guide is postponed during recording, active runs and journal/recovery errors.

System One **OpenRouter Free** uses fixed `openrouter/free`, bounded zero
`max_price` provider routing and a randomly selected available compatible free
model. Rate/availability limits remain; no paid/local fallback or install is
allowed. Both cloud modes require a separate API key and explicit cloud consent;
old cloud configurations without that flag require reconsent. Verify changing
a paid-enabled source to a Free/local draft makes no settings write and still
shows the actual persisted enabled source/model/detail and cost boundary. Saving
a key alone must not enable cloud usage or alter main credentials.

Record the same user-described idle conditions or repeatable workload scene.
Complete diagnosis saves its initial report before contacting AI. Reports retain
conditions and investigation preset; **Measurements** shows saved Before on the
left and After on the right, survives restart, and exports local JSON on request.
Exports can contain process/driver information: review before sharing. Session
history can be searched by conditions and preset. Comparison and AI selection
remain separate. Imported PresentMon evidence is still required for FPS claims.

The after-capture button reuses the saved duration, conditions and workload,
analyzes only that owned capture once, and leaves failed analysis explicitly
retryable. For an active optimization run, restart verification and three matching
reports on each side still gate evaluation. Idle-to-idle comparisons are diagnostic
observations only, never evidence for Apply/Keep or input-latency/FPS gains.
Mismatched conditions, quality, coverage or environments reject comparison.

Run `node scripts/check-contextual-workflow.cjs` against the UI preview with
Playwright available, along with the existing polish/review checks. It mocks every
Agent command and covers both themes, setup/skip, owned capture analysis, export,
restart persistence and narrow layout. Backend regressions cover exact action
references, unknown/unprovided executors, Microsoft inbox-driver dates, virtual
HAGS/firmware limits and diagnostic decision rejection. These checks do not
establish native MSI support, actual provider acceptance or installer safety.
The same browser check also simulates MSI Windows-reader results and imports
synthetic UTF-8/UTF-16 setup exports, with explicit **SIMULATED** provenance,
unknown/conflicting current values, secret omission and decode-error rejection.
Real offline-parser tests complement, but do not turn, these fixtures into host
BIOS observations. PawnIO is not required for either existing read/import path;
no driver is installed, loaded or made an application prerequisite.

## Authorized host check: DeepSeek Flash, 2026-10-07

Published alpha.3's failure was reproduced through the real Windows desktop
UI with the configured `deepseek-flash` connection, not a browser/provider mock.
Subsequent source corrections produced both a validated explicit UI retry and
a fresh **Complete diagnosis** from scan through WPR, ETL analysis, provider
investigation and review. The fresh run completed 12 turns, with 15 findings and
10 proposals; no local fallback or stored error. These observations apply to
the **reviewed corrections included in alpha.4**, not the immutable alpha.3
assets or acceptance of the exact alpha.4 installer.

Testing used an instrumented Release WebView with a private profile and
loopback-only diagnostics. The one 30-second system-wide capture had valid
quality and zero lost events; raw ETL was removed after analysis. Capture motion
was quiet, Windows operation history and inspected action values were unchanged,
and no approval, Apply, rollback, scanner remediation, provider/model switch or
budget increase was performed. Runs were explicitly finished without changes.
Private host profiles, journals and screenshots are not public test fixtures.

This is not a workload Baseline, measured gain, unattended/provider-wide
reliability guarantee or exact-installer/VM Apply/recovery acceptance. Those
checks remain separate. Host ChatGPT quota failures from the earlier test do
not explain the DeepSeek failure.

## VM-only privileged acceptance

With separate explicit authorization and the exact candidate installer:

- Record three matching quality-valid workload Baselines, approve one low-risk
  registered action, verify restore point/backups, apply and inspect its journal.
- Record three matching Candidates, compare, then test Keep or verified rollback.
  Repeat restart/interruption/I/O recovery while retaining original journals.
- Verify protected journal ownership and medium-integrity denial of
  read/write/rename/delete, unsafe/reparse roots and malformed records.
- Test install/upgrade/uninstall and retained user data; do not infer safety
  from historical results or a green source CI run.
- Defender scanning requires separate resource/remediation/network consent and
  confirmation. It may quarantine/remove files; a restore point does not
  guarantee reversal. Read [privacy/security scope](PRIVACY_SECURITY.md#scanner-boundary).
- Optional firmware tools, model downloads and non-admin workers need their
  own compatibility, security and resource acceptance. They are not part of
  routine diagnosis. See [optional components](SCEWIN_SYSTEM_ONE.md).

## Contributor checks (no live provider or system writers)

The default investigator uses Pi CORE (`@earendil-works/pi-agent-core`), not
the full Pi coding CLI/TUI. Host transport/OAuth, fixed readers and validations
remain authoritative; there is no shell/file tool, persisted Pi session or agent
fallback. Pi inherits the host token (administrator when elevated), not a filtered
UAC token. It is not an OS sandbox; a compromised runtime has the host's privileges.
Verify a complete mocked turn and cancellation from both normal and elevated
hosts, including a host that cannot launch non-admin helpers. Optional workers
and read tools must retain their existing reduced-privilege policy.
Release packages bundle Node/CORE and notices: end users need no npm, global Pi
or local model installation.

On Windows x64 with a complete official Node distribution, build the bridge
before .NET tests so the copied test runtime matches the source:

```sh
cd ui
npm ci --ignore-scripts
npm run pi:build
cd ..
dotnet test tests/NeuroTune.Tests -c Release --filter 'TestCategory!=WindowsIntegration'
dotnet format NeuroTune.sln --verify-no-changes
cd ui
npm test
npm run typecheck
npm run lint
npm run build
```

`pi:build` copies the build machine's Node runtime, bundles pinned CORE/Pi AI
and dependency notices, checks runtime version/syntax and runs the packaged
bridge through one local generate/handle/done roundtrip with a minimal environment.
That roundtrip uses a synthetic host response, no credentials, provider or system
tools; it does not certify host-token startup on another PC. If the official Node
license is already beside `node.exe`, no override is needed. **Only when it is missing**, obtain the official license matching the
exact Node version/distribution (official distribution or its matching
[nodejs/node](https://github.com/nodejs/node) release tag), review it, and set
`NEUROTUNE_NODE_LICENSE` to that file before `npm run pi:build`. Do not use an
arbitrary license or a different release's notices. The build downloads neither
runtime nor license and refuses a missing license. Developer npm/dependency
installation is a build prerequisite, not an end-user requirement.

`npm test` includes UI callback regressions and the mocked Pi CORE protocol/turn
checks in `scripts/pi-agent.test.mjs`. No live auth/inference or system writer
belongs in these contributor checks.

For browser regressions, start `npm run preview -- --host 127.0.0.1` in `ui/`
and run `node scripts/check-ui-polish.cjs`,
`node scripts/check-review-measurements.cjs`,
`node scripts/check-provider-models.cjs`,
`node scripts/check-system-one-free.cjs` and
`node scripts/check-provider-failure.cjs` from the repository root. Set
`PLAYWRIGHT_PATH`, optional `CHROME_PATH` and `UI_PREVIEW_URL` for local tools.
Every Agent/provider call in those browser scripts is mocked. These checks
cannot certify native Windows behavior, billing, recovery or performance.

### Native UI check (`check-native-ui.cjs`)

This one attaches to the real Release build, so it needs a packaged window with a debug port:

1. Build it once: `cd ui && npm run tauri -- build --bundles nsis` (with `NEUROTUNE_NODE_LICENSE` set as above).
2. Close **every** NeuroTune window. WebView2 shares one browser process per profile, so a running instance
   silently ignores the debug flag and the port never opens.
3. Start the Release executable with the port, from PowerShell:
   `$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS="--remote-debugging-port=9224"; Start-Process ui/src-tauri/target/release/neurotune.exe`
   (the app elevates itself; accept the Windows prompt, or run an already elevated shell).
4. From the repository root: `$env:PLAYWRIGHT_PATH="<path to playwright-core>"; node scripts/check-native-ui.cjs`.
   `NATIVE_CDP_URL` overrides the endpoint. Without `NATIVE_CAPTURE=1` it performs no system-wide capture and
   verifies that operation history is unchanged; `NATIVE_CAPTURE=1` additionally runs real WPR smoke captures.

A development build (`tauri dev`, `localhost:1420`) is rejected: the page URL must be `http://tauri.localhost/`.

## Feedback

Include release/version, Windows build, provider/model ID, selected mode,
phase and the sanitized error/status code. Do not attach credentials, raw ETL,
full profiles, private filesystem paths or pending recovery backups.
