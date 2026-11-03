# Changelog

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
