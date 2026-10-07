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
   system-wide diagnostic; this cannot unlock Apply or establish FPS/input latency.
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

## Authorized host check: DeepSeek Flash, 2026-10-07

Published alpha.3's failure was reproduced through the real Windows desktop
UI with the configured `deepseek-flash` connection, not a browser/provider mock.
Subsequent source corrections produced both a validated explicit UI retry and
a fresh **Complete diagnosis** from scan through WPR, ETL analysis, provider
investigation and review. The fresh run completed 12 turns, with 15 findings and
10 proposals; no local fallback or stored error. These observations apply to
the **unreleased corrected source**, not the immutable alpha.3 assets.

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

```sh
dotnet test tests/NeuroTune.Tests -c Release --filter 'TestCategory!=WindowsIntegration'
dotnet format NeuroTune.sln --verify-no-changes
cd ui
npm test
npm run typecheck
npm run lint
npm run build
```

For browser regressions, start `npm run preview -- --host 127.0.0.1` in `ui/`
and run `node scripts/check-ui-polish.cjs`,
`node scripts/check-review-measurements.cjs`,
`node scripts/check-provider-models.cjs` and
`node scripts/check-provider-failure.cjs` from the repository root. Set
`PLAYWRIGHT_PATH`, optional `CHROME_PATH` and `UI_PREVIEW_URL` for local tools.
Every Agent/provider call in those browser scripts is mocked. These checks
cannot certify native Windows behavior, billing, recovery or performance.

## Feedback

Include release/version, Windows build, provider/model ID, selected mode,
phase and the sanitized error/status code. Do not attach credentials, raw ETL,
full profiles, private filesystem paths or pending recovery backups.
