# NeuroTune v0.8.0-alpha.2

## Privacy, Security and Localized UI Preview

**Unsigned prerelease for controlled Windows 11 x64 testing, not a stable/general
release or a promise of performance gains.** Keep an independent backup and use
a disposable VM for system-changing tests. A successful build and matching
checksums do not certify installer safety, native recovery or actual optimization.

## Downloads

- `NeuroTune_0.8.0-alpha.2_x64-setup.exe`: unsigned per-machine NSIS installer.
- `NeuroTune-0.8.0-alpha.2-win-x64.zip`: portable app with both self-contained
  agents, the WPR profile, MIT license and documentation. Extract the complete
  folder; do not move just the main executable.
- `SHA256SUMS`: SHA-256 checksums for the installer and portable archive.

"Portable" means no installation, **not** no elevation or persistent data.
Windows 11 x64 and WebView2 are required. Windows UAC approval is required for
the desktop app; System Protection must be enabled before applying changes.
Windows SmartScreen may warn because these alpha binaries are not Authenticode
signed. Hashes identify bytes, not an authenticated publisher.

## Changes since alpha.1

- **Privacy/security investigation:** a fifth prompt specialization covers
  privacy policies, protections, detections, persistence and Windows health.
  Reports expose 15 checks, cited sources, limitations and missing evidence.
  Model omissions, unknown/unavailable evidence and imported reports cannot
  silently become a verified whole-PC audit.
- **Separate Advisory audit mode:** local evidence, a consented AI investigation,
  reviewable report and Finish. No workload Baseline or optimization Apply.
  Complete audits pause on a cancellable prepared-evidence preview before any
  provider transmission. A preset cannot change mode or grant scanner consent.
- **Separate Defender operations:** quick/full scans have independent resource,
  remediation and network/cloud/sample-submission consent plus confirmation.
  Protected pre-command journals preserve uncertain/interrupted operations.
  Scan return or empty history does not prove a clean PC; scan operations are
  not linked as verified audit coverage. Defender may quarantine/remove threats
  under its configured policy; restore points do not guarantee reversal.
- **Privileged recovery hardening:** Administrators/SYSTEM-owned operation/run
  storage under ProgramData, bounded reads and flushed atomic replacement.
  Unsafe ACLs, reparse paths, unsupported schemas, mismatched identities and
  malformed snapshots fail closed. GPU/custom-plan recovery survives missing
  disposable source inputs; core parking restores the captured scheme.
- **Stricter execution and evidence gates:** opposing actions and mismatched
  journal/run identities are rejected. Missing comparison coverage or new
  regressions cannot justify favorable Keep. PresentMon imports require process
  attribution but remain evidence with unverified session provenance.
- **Windows utility/process boundaries:** absolute utility resolution, bounded
  output/deadlines and stricter child handling. Native response pipes are made
  non-inheritable so the independent capture watchdog does not hold IPC open.
- **Localized desktop:** English, Simplified Chinese, Japanese, Spanish and
  Russian UI and application-owned response-language preferences. Grouped
  navigation, clearer diagnosis/review, light/dark/system appearance, keyboard
  focus and quiet/reduced-motion behavior remain part of the interface.
- **Current N-logo branding:** startup, overview, sidebar, favicon and Windows
  icons use the purple/white mark. The README adds plastic technology badges,
  a redesigned banner, current-frontend screenshots and workflow/trust diagrams.
- **Dependency correction:** `source-map-js` 1.2.2 addresses
  GHSA-68fv-2mgg-jv7q without relaxing the dependency-audit gate.
- **Consistent release metadata:** UI, npm/Cargo lockfiles, Tauri and .NET
  components now declare `0.8.0-alpha.2`. The release-metadata check also rejects
  mismatched application versions in both lockfiles.

## Preserved boundaries

The selected AI investigates evidence through bounded, registered read-only
follow-ups. Its output is untrusted advice, not arbitrary shell/Registry/firmware
write authority. Apply requires registered, reinspected actions, matching
persisted approval, separate high-risk consent, valid workload Baselines,
mandatory verified restore point/backups and per-action verification/journaling.
Manual guidance and generated scripts stay inert. Failed writes attempt
reverse-order rollback; incomplete recovery remains visible.

Scheduling/ISR/DPC traces do **not** measure FPS, ping/jitter or end-to-end input
latency. A 1+1 comparison is exploratory; the repeated gate needs at least 3+3
matching, quality-valid workload captures. System-wide diagnostics and imported
reports/screenshots cannot unlock Apply. Neither assistant runs during captures.

Provider choice and inference costs remain explicit. OpenRouter, OpenAI,
Anthropic, DeepSeek and compatible/custom or local loopback models are supported;
eligible ChatGPT plan/credits access is provider-controlled. No automatic provider
or API-billing fallback is offered. Prepared evidence requires consent; raw ETL
stays local. Redaction is best-effort, not guaranteed anonymization. Screenshot
pixels are not anonymized and may be resent each turn; they are not persisted in
run journals.

Optional System One advice is default-off and cannot approve writes. Local
installation downloads multi-GB third-party assets; cloud classification has
separate credentials/consent/costs. Non-admin workers are not network/filesystem
sandboxes. SCEWIN export needs an explicitly approved user-supplied package;
no AMI binaries, BIOS writer, security bypass, TronScript or arbitrary cleanup
executor is bundled.

## Upgrade safety

Settings, measurements and DPAPI-encrypted credentials remain under
`%LocalAppData%\NeuroTune`. Privileged operation/run journals and Registry
exports use `%ProgramData%\NeuroTune-journals\<Windows user SID>`; Defender
operations use a separate protected `defender-scans` store.

Legacy LocalAppData `operations`/`runs` are not imported. Use the previous build
to finish/review recovery, preserve independent backups and resolve prior writes,
then archive only reviewed journals. **Never delete pending recovery or copy old
JSON into protected storage to bypass the gate.** See [SECURITY.md](SECURITY.md).

## Verification and remaining acceptance

The release is rebuilt from the version-aligned sources; packaging checks cover
complete agent/profile inclusion and exact document copies. The published
`SHA256SUMS` must be verified against the downloaded files before running them.
Fresh build/test/audit and artifact-check results are recorded on the GitHub
release; historical test results are not current-candidate certification.

Real installer install/uninstall, elevated token/ACL denial, legacy upgrades,
interruption/I/O recovery, actual Defender behavior, native DPI/Narrator,
physical AMD/NVIDIA repeatability, provider/vision calls and optional component
installation remain separately authorized Windows 11 acceptance work. No such
acceptance or measured performance gain is implied by publishing this alpha.
The packages contain no user credentials, runtime journals, model weights,
SCEWIN binaries or vendor drivers.

See the [test guide](docs/TESTING.md),
[privacy/security scope](docs/PRIVACY_SECURITY.md),
[execution boundaries](docs/DETERMINISTIC_CONTROLS.md) and
[optional components](docs/SCEWIN_SYSTEM_ONE.md).
