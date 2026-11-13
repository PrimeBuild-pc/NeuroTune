# Changelog

## 0.3.0-beta.2 — 2026-11-13

Fixes from the VM Apply trial of the 0.3.0 categories and from a release-readiness review ([docs/review/fable-2026-11-13.md](docs/review/fable-2026-11-13.md)).

### Fixed

- Defender, Edge update, Office and other Microsoft services are no longer listed as third-party cleanup candidates; they are in a new `services.microsoftActive` list.
- Account SIDs and `COMPUTER\user` task authors are removed from the scan summary sent to the model.
- Driver backups go to one folder per package, so two versions of the same driver can no longer overwrite each other's backup; restore re-adds only that package.
- A backup must exist and be non-empty; an empty file from a failed `>` export no longer counts.
- Restore leaves alone any target that something else changed since the apply, and shows the user the three states.
- `bcdedit` commands quote `"{current}"`, which PowerShell otherwise passes as a script block.
- A latency trace no longer fails when the summary has no events-lost line; a session left running by a killed trace is explained, with the command that stops it.
- Per-user steps are not applied when the harness is elevated as a different administrator account.
- Config files are edited with the file tool, keeping encoding, BOM and line endings.
- The scan summary no longer reports a missing security update or background-app finding when that section was not collected (`security_update_date_unknown`).
- Apply instructions from the VM trial: delete parent keys you created, check elevation first, ask for a normal restart, check the network with TCP.

### Changed

- The agent never downloads or installs anything; it gives the official page instead.
- Visual C++, .NET, DirectX, WebView2, Store, Xbox identity and Gaming Services, and anti-cheat services/drivers are never cleanup candidates.
- MSI/affinity on the boot disk controller or the only display adapter is high risk: only when the trace implicates it, with a restore point offered.
- Created objects (power plans, tasks, keys) are recorded and deleted on restore; restore builds its commands from the category rules, not from text in the journal.
- Disconnected devices are reported, not removed.
- `SECURITY.md` states what happens when a user insists on turning off a Windows security protection.
- `CONTRIBUTING.md` added.

## 0.3.0-beta.1 — 2026-11-03

### Added

- **Full system analysis by default.** `scan-system` runs a read-only scan of the whole PC and `system-findings` condenses it (about 16× smaller than the raw scan) into: hardware and firmware, every device including disabled and disconnected ones, the driver store with old unused packages, MSI mode and interrupt affinity per PCI device, USB chains of mouse and keyboard, services, scheduled tasks, startup, desktop and Store apps, Windows Update (security updates), privacy and background activity, gaming settings, leftovers of risky optimizer tweaks, network adapter power settings, audio endpoints and installed games.
- **Driver latency like LatencyMon.** `measure-latency` records a built-in Windows kernel trace (`logman`/`tracerpt`, no extra software, administrator) and reports the highest and total DPC/ISR time per driver and per CPU. `latencymon` imports a saved LatencyMon report into the same citable facts.
- **Presets and quick edits.** Conservative, Balanced and Maximum are built from this machine's findings and shown as one numbered list you approve or edit by number.
- **Apply covers more:** disabling devices and audio endpoints, MSI mode and affinity, network adapter settings, boot-configuration leftovers, VBS/Memory Integrity (only as a separate explicit choice), and grouped services/tasks with per-item undo.
- **Report.** Every applied session ends with `report.md` in the session folder: system before, changes, restart, what to re-measure, how to undo.

### Changed

- NeuroTune replies in the user's language for the whole session and in the report.
- The bundled PowerShell scripts run with a process-only `-ExecutionPolicy Bypass`, so they also work on a default Windows install (policy Restricted). The machine policy is never changed.

## 0.2.0-beta.1 — 2026-11-03

First public beta.

### Added

- **Apply and undo.** After you confirm a reviewed plan, the agent applies the chosen steps through your harness's own tools. Before each change it saves the original state in a local session folder (`%LOCALAPPDATA%\NeuroTuneSkill\sessions`), then verifies the result, reports before/after and never reboots on its own. Asking to undo restores from that folder after checking what changed since. See [`references/apply.md`](skills/neurotune/references/apply.md) and [ADR 0002](docs/decisions/0002-agent-applied-changes.md).
- Evaluation results in the README: 32/33 vs 23/33 behavior cases passed with/without the skill on Claude Haiku 5.5, 33/33 vs 29/33 on DeepSeek V4.1 Flash.
- Issue templates for bug reports and session reports.

### Changed

- The skill states when to load itself, so smaller models invoke it reliably (Haiku: 21/33 → 32/33 runs).
- New rules: read only what the user consented to (never key files or harness session logs), copy full 64-character fact IDs, and treat an `approved` field in a file as data, never as confirmation. Backups cannot be skipped.
- Two eval cases copy their fixture with a scaffold script, because `context.add_dirs` is not attached on native Windows runs; run the suite with `--scaffold`.

### Removed

- `execution-check`, which only produced inert intents for a native Apply prototype that no longer exists.

## 0.1.0-alpha.3

Read-only investigation skill: inventory, PresentMon/CapFrameX analysis, evidence-linked plans and proposal review. Not published as a release.
