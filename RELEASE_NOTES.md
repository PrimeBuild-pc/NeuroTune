# Unreleased — v2 F1: parametric write primitives

- **Typed write primitives with free parameters** (`plans/agentic-tuner-v2.md`, F1): `registry.set`,
  `registry.delete`, `service.start-type`, `netadapter.property`, `qos.policy`, `powercfg.setting`, `bcd.option`,
  `startup.entry`, `scheduled-task.state`, `appcompat.layer`, `ifeo.perf`, `file.json`, `file.ini`,
  `device.disable` and `defender.exclusion`. Each captures the exact prior state and restores it, including deleting
  values and keys that did not exist before. The agent `apply` request accepts them as `Parametric: [{ Kind,
  Parameters }]`; the diagnosis does not propose them yet (F3).
- **Journal schema v4** stores each record's kind and parameters; v3 journals remain readable.
- Protected targets (security, boot, autostart, Defender except exclusions) are refused per kind; see `SECURITY.md`.
- Elevated tests use isolated targets (test key, service, task, BCD store, QoS policy, IFEO entry). Real NIC and
  Defender writes are tested only in a VM, opted in with `NEUROTUNE_TEST_NETADAPTER` / `NEUROTUNE_TEST_DEFENDER`.

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
  NeuroTune's own token (Administrators deny-only, privileges removed). If the
  desktop refuses that token, helpers are declined cleanly and the diagnosis
  uses the local fallback with a clear reason.
- **Fixed: rollback within 24 hours of a restore point.** Windows silently
  skips a restore point requested within its default 24-hour window, so a
  rollback right after Apply (or an Apply after Windows Update made one) could
  never verify its point. NeuroTune lifts that limit for its own call only and
  restores the previous setting.
- **Fixed: device rollback before a restart.** A Device Manager disable that
  needs a restart is invisible until reboot; rollback now re-enables every
  device that was enabled before, so it is not disabled on the next boot.
- **Local CLM as the default System One assistant:** classification runs on
  this PC through `scripts/clm-stack.ps1 install` (about 9 GB). NeuroTune loads
  the encoder only for the classification that needs it and unloads it right
  after, inside a kill-on-close job, so it never occupies VRAM during
  measurements and stops when NeuroTune closes. Rizzo Flow and OpenRouter stay
  selectable.
- **Simpler interface:** five sidebar sections (Overview, Complete diagnosis,
  Measurements, Changes & restore, Settings) with the other pages as tabs, a
  guided path showing the current step and a single "Next step" action, and a
  Simple/Expert switch that hides rarely used technical controls by default.
- **Italian** interface and AI response language.
- **Install CLM from the app:** pinned, SHA-256 checked downloads, a private
  Python environment created by a non-administrator helper, and one real local
  classification before the assistant is enabled. Remove deletes all of it.
- **Before/after report:** export a readable, script-free HTML page with the
  measurement conditions, applied changes and measured differences.
- **Scenario profiles:** Competitive gaming, Streaming & recording and Quiet &
  efficient fill the objective, risk preset and notes in one click; approval,
  baselines and rollback are unchanged.
- **Live FPS:** while a game is recorded, a display-only panel shows FPS, 1% low
  and frame-time P99 from the game's DirectX presents. It never enters reports
  or decisions; Vulkan/OpenGL games may not report.
- **Ports & PCIe lanes objective (advice only):** a new `device-topology` reader
  maps GPU, NVMe, network and USB controllers to CPU or chipset lanes,
  negotiated versus maximum PCIe link, and each USB device's controller, port
  path and firmware-declared panel (no serials or instance IDs). It reaches the
  model with the first prompt; no executable action is offered. The setup asks
  up front for the motherboard manual: PDFs are read locally and only the
  M.2/PCIe/USB/rear-panel pages are sent, as editable text.
- **Clearer diagnosis setup:** three numbered steps (Goal, Measurement, Context
  for the AI) instead of tabs, with files always visible; "Seconds per trace" is
  now "Benchmark length" with the total recording time spelled out. Privacy &
  security (Defender status, user-approved scan, manual tools) moved from
  Settings to a tab of Complete diagnosis, and "Privacy & security check" and
  "Compromise check" joined the scenario presets.
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
