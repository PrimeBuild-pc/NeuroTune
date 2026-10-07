# NeuroTune v0.8.0-alpha.3

## Provider selection and diagnosis reliability

**Unsigned prerelease for controlled Windows 11 x64 testing, not a stable/general
release or a promise of performance gains.** These notes describe the upcoming
candidate; assets are built and published only after the reviewed PR is merged.
Keep independent backups and use a disposable VM for system-changing tests.
A green build and matching hashes do not certify recovery or compatibility.

## Downloads after publication

- `NeuroTune_0.8.0-alpha.3_x64-setup.exe`: unsigned per-machine NSIS installer.
- `NeuroTune-0.8.0-alpha.3-win-x64.zip`: complete portable app with both
  self-contained agents, WPR profile, license and user documentation.
- `SHA256SUMS`: verify these against the downloaded assets before running them.

Portable means no installation, not no elevation or persistent data. Extract the
complete folder. Windows 11 x64, WebView2 and normal UAC approval are required.
System Protection is required before Apply. SmartScreen may warn about unsigned
binaries; SHA-256 identifies bytes, not an authenticated publisher.
Published alpha.1 and alpha.2 assets remain unchanged.

## Changes since alpha.2

- **Explicit model catalogs:** API/local providers show an Available models
  dropdown containing returned IDs, independent of the current typed model.
  Manual exact-ID entry remains supported; discovery does not silently replace
  an API model. Endpoint/protocol/credential changes clear stale catalog choices.
  Model names and permissions are provider-controlled, never fabricated. Listing
  a model does not verify diagnosis/image support or available inference quota.
- **Provider failure clarity:** safe HTTP status and ChatGPT app/plan limit
  errors are shown before generic fallback explanations. No raw response bodies,
  automatic retries, alternate model/provider or API-billing fallback are added.
- **Retained incomplete reviews:** a failed investigation keeps its local findings
  and failure cause across restart, visibly marked Not completed. It grants no
  approval/Apply authority and closes through explicit Finish without changes.
- **Earlier recovery preflight:** legacy journals and retained unfinished runs
  block complete/advanced diagnosis before expensive scan/capture/provider work.
  Errors list account-specific blocking paths; saved provider settings survive
  journal-load failures. Refresh explicitly rechecks protected history.
- **PrimeBuild attribution:** visible, quiet "by PrimeBuild" credit accompanies
  the app identity, including compact navigation.
- **Public documentation cleanup:** user/provider/privacy/safety guides remain;
  internal planning, historical audits and machine reports stay local/untracked.
- **Coherent alpha.3 metadata:** frontend, npm/Cargo lockfiles, Tauri and .NET
  components agree. The public README screenshots use the candidate frontend.

## Preserved safety and privacy boundaries

AI output is untrusted advice. Only registered, reinspected capabilities enter
writes, with matching persisted approval, separate high-risk consent, workload
Baselines, verified restore point/backups and per-action verification/journaling.
Manual guidance and generated scripts remain inert. Failed writes attempt
verified rollback; incomplete recovery remains visible.

System-wide diagnostics, imported reports and screenshots never unlock Apply.
Scheduling/ISR/DPC traces do not measure FPS, ping or end-to-end input latency;
accepted comparisons require repeatable matching 3+3 workload captures. Advisory
audit has no workload benchmark or optimization Apply. Neither AI runs during
capture. Defender operations have separate consent and may quarantine/remove
files; restore points do not guarantee reversal.

Only consented prepared evidence reaches the selected provider. Redaction is
best-effort; pixels are not anonymized and may be resent each turn. Raw ETL stays
local and screenshot pixels are not persisted in run journals. Optional System
One remains default-off, with separate download/cloud consent. No SCEWIN binary,
vendor driver, model weights, credential or runtime journal belongs in packages.
No BIOS writer, security bypass or automatic cleanup suite is bundled.

## Upgrade safety

Settings, measurements and DPAPI credentials remain under
`%LocalAppData%\NeuroTune`; protected operation/run journals use
`%ProgramData%\NeuroTune-journals\<Windows user SID>`.
Nonempty legacy `operations`/`runs` are not imported or trusted automatically.
Use the previous build to resolve/review prior writes, preserve backups and
archive only reviewed journals. Never delete pending recovery or copy editable
JSON into protected storage. See [SECURITY.md](SECURITY.md).

## Verification scope

Source and offline browser regressions do not establish exact-installer
install/uninstall, ACL denial, upgrades, interruption/I/O recovery, actual
Defender behavior, DPI/Narrator, physical workload gains, live provider/vision
access or optional component compatibility. Artifact hashes and fresh build
results belong to the eventual GitHub release. Those remaining acceptance tests
need separately authorized Windows 11 execution on that exact candidate.

See [alpha testing](docs/TESTING.md), [providers](docs/PROVIDERS.md),
[privacy/security](docs/PRIVACY_SECURITY.md),
[execution boundaries](docs/DETERMINISTIC_CONTROLS.md) and
[optional components](docs/SCEWIN_SYSTEM_ONE.md).
