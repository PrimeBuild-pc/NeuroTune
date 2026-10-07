# NeuroTune v0.8.0-alpha.4

## DeepSeek Flash diagnosis reliability

**Unsigned prerelease for controlled Windows 11 x64 testing, not a stable/general
release or a promise of performance gains.** Assets are built and published only
after the release metadata PR is reviewed and merged. Keep independent backups
and use a disposable VM for system-changing tests. A green build and matching
hashes do not certify recovery or compatibility.

## Downloads after publication

- `NeuroTune_0.8.0-alpha.4_x64-setup.exe`: unsigned per-machine NSIS installer.
- `NeuroTune-0.8.0-alpha.4-win-x64.zip`: complete portable app with both
  self-contained agents, WPR profile, license and user documentation.
- `SHA256SUMS`: verify these against the downloaded assets before running them.

Portable means no installation, not no elevation or persistent data. Extract the
complete folder. Windows 11 x64, WebView2 and normal UAC approval are required.
System Protection is required before Apply. SmartScreen may warn about unsigned
binaries; SHA-256 identifies bytes, not an authenticated publisher.
Published alpha.1, alpha.2 and alpha.3 tags/assets remain unchanged. Alpha.3 does
**not** contain the following corrections; use the complete alpha.4 package.

## Changes since alpha.3

- **DeepSeek JSON output:** the built-in connection uses documented JSON mode
  and an explicit system-level planner contract. This is not assumed for
  arbitrary custom/local models, and no provider/model is switched automatically.
- **Exact evidence references:** DeepSeek receives stable short local evidence
  aliases, resolved to original IDs before validation and storage. This prevents
  copying long identifiers incorrectly without fuzzy matching, extra evidence
  permission or altered privacy classification. Unknown/unprovided IDs and
  oversized/repeated requests remain rejected. Already-provided facts stay
  citable but are excluded from the new-evidence request catalog.
- **Clear investigation envelopes:** prompts distinguish ordinary read-only
  follow-ups from driver-details, the only tool accepting an observed `.sys`
  basename as `module`; no command or write authority is granted.
- **Locally resolved observations:** findings may omit `currentValue`; it is
  filled from the exact validated provided fact. Explicit fabricated values
  still fail. Model interpretation remains unverified assessment, not a new
  measured observation or proof of a cause.
- **Actionable, private failures:** allowlisted local validation reasons name
  the failed stage without exposing raw provider responses or arbitrary
  exception text. Failure still retains an incomplete, nonexecutable review;
  no automatic inference retry or billing fallback is added.
- **Successful retry state:** a validated retry clears its previous attempt's
  stored error instead of leaving a misleading failure on the successful run.
- **Coherent alpha.4 metadata:** .NET, Tauri, Cargo/npm lockfiles, UI and public
  documentation agree. Alpha.3's model picker, PrimeBuild attribution, early
  legacy-journal preflight and retained incomplete reviews are preserved.

## Verification and limits

On 2026-10-07 the configured `deepseek-flash` connection was tested through the
real Windows desktop UI, native Agent, local scans and WPR/ETL analysis. The
reviewed functional corrections produced a validated explicit UI retry and a
fresh Complete diagnosis: 12 accepted turns, 15 findings, 10 proposals, no
fallback, no stored error and Apply disabled. Real catalog discovery preserved
the selected model. These were not browser/provider mocks or a ChatGPT test.

All five owned system-wide diagnostic captures had valid quality and zero lost
events; raw ETL was removed after analysis. Runs were explicitly closed without
changes. Recording stopped, inspected action values/Windows operation history
were unchanged, settings were byte-identical and the selected 12-turn/10-minute
budget was not increased. Private host profiles and screenshots are not shipped.
The app was instrumented with a private WebView profile and loopback-only CDP;
standard production binaries were rebuilt afterward with diagnostics removed.

This demonstrates the corrected read-only host flow, **not** every model/provider,
unattended reliability, a matching workload Baseline or measured performance
gain. It does not certify this exact alpha.4 installer, install/uninstall/upgrade,
ACL denial, interruption/recovery, Defender, DPI/Narrator, live vision or optional
components. Artifact hashes and fresh build results belong to the GitHub release;
remaining acceptance needs separately authorized execution of that exact
candidate in a disposable Windows 11 VM. No Apply, rollback, installation,
optional-component installation or Defender remediation was run on the host.

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

See [alpha testing](docs/TESTING.md), [providers](docs/PROVIDERS.md),
[privacy/security](docs/PRIVACY_SECURITY.md),
[execution boundaries](docs/DETERMINISTIC_CONTROLS.md) and
[optional components](docs/SCEWIN_SYSTEM_ONE.md).
