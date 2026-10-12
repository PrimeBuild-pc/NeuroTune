# NeuroTune v0.8.0-alpha.5

## Pi agent, local CLM System One and reliable CI

**Unsigned prerelease for controlled Windows 11 x64 testing, not a stable/general
release or a promise of performance gains.** Keep independent backups and use a
disposable VM for system-changing tests. A green build and matching hashes do
not certify recovery or compatibility.

## Downloads after publication

- `NeuroTune_0.8.0-alpha.5_x64-setup.exe`: unsigned per-machine NSIS installer.
- `NeuroTune-0.8.0-alpha.5-win-x64.zip`: complete portable app with both
  self-contained agents, the bundled Pi runtime, WPR profile, license and docs.
- `SHA256SUMS`: verify these against the downloaded assets before running them.

Windows 11 x64, WebView2 and normal UAC approval are required. System
Protection is required before Apply. SmartScreen may warn about unsigned
binaries. Earlier alpha tags/assets remain unchanged.

## Changes since alpha.4

- **Pi CORE investigation agent:** the bounded investigation runs in a
  short-lived non-administrator Pi subprocess; credentials and provider calls
  stay in NeuroTune. First-launch setup offers ChatGPT, OpenRouter or Other.
- **Fixed: non-admin helpers from an elevated NeuroTune.** The UAC linked token
  is identification-level for a normal elevated process, so Pi and the optional
  workers could not start (Win32 1346) and every diagnosis silently used the
  local fallback. Helpers now run with a LUA-filtered, medium-integrity copy of
  NeuroTune's own token (Administrators deny-only, privileges removed).
- **Local CLM as the default System One assistant:** classification runs on
  this PC through `scripts/clm-stack.ps1 install` (about 9 GB). NeuroTune loads
  the encoder only for the classification that needs it and unloads it right
  after, inside a kill-on-close job, so it never occupies VRAM during
  measurements and stops when NeuroTune closes. Rizzo Flow and OpenRouter stay
  selectable.
- **Model failure guidance:** rate limits, missing credits, unavailable models,
  upstream errors, unusable output and timeouts name the model and say how to
  recover, without echoing provider text or retrying automatically.
- **Vulnerability & compromise review preset** with six fixed read-only readers
  and 12 hardening registry snapshots.
- **Reversible Device Manager controls** for the latency presets: 18 allowlisted
  `device.disable.*` capabilities, journaled and rolled back like every action,
  never pre-selected, refusing devices that host input, storage or display.
- **CI:** the test step hung for 6 hours on every run since the Pi agent landed
  (the bug above). Actions are now pinned to commit SHAs, `cargo-audit` to
  0.22.2, the job has a 60-minute limit and hung tests are reported.

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
local and screenshot pixels are not persisted in run journals. System One is
advisory only: local CLM by default once you install it, cloud sources need
separate consent. No SCEWIN binary,
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

See [alpha testing](docs/TESTING.md), [providers](docs/PROVIDERS.md),
[privacy/security](docs/PRIVACY_SECURITY.md),
[execution boundaries](docs/DETERMINISTIC_CONTROLS.md) and
[optional components](docs/SCEWIN_SYSTEM_ONE.md).
