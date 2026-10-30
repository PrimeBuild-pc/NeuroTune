<p align="center">
  <img src="assets/banner.svg" alt="NeuroTune" width="610">
</p>

<p align="center">
  <b>Evidence-led Windows performance investigation, inside the AI harness you already use.</b>
</p>

<p align="center">
  <img alt="status" src="https://img.shields.io/badge/status-alpha-orange">
  <img alt="mode" src="https://img.shields.io/badge/mode-read--only-critical">
  <img alt="node" src="https://img.shields.io/badge/node-%3E%3D22-brightgreen">
  <img alt="license" src="https://img.shields.io/badge/license-MIT-blue">
  <img alt="platform" src="https://img.shields.io/badge/platform-Windows%2011-0078D6">
</p>

---

NeuroTune is an [Agent Skill](https://www.anthropic.com/news/agent-skills) for Windows gaming stutter and performance investigations. It runs inside your existing AI coding harness (Claude Code, Pi, OpenCode, Codex, or anything that can load a skill and run local commands) instead of being its own app. The harness supplies the model, the conversation and the permissions; NeuroTune supplies a small, deterministic local CLI for inventory, frame-time analysis and evidence-linked tuning plans.

**It does not apply anything.** NeuroTune investigates, cites evidence for every claim and produces a plan a human reviews — it has no system writer, no privileged broker and no Apply/rollback step. See [Not implemented](#not-implemented) for the exact boundary.

## Contents

- [Why](#why)
- [How it works](#how-it-works)
- [What it does](#what-it-does)
- [Not implemented](#not-implemented)
- [Install](#install)
- [Use it](#use-it)
- [CLI quick start](#cli-quick-start)
- [Safety and privacy](#safety-and-privacy)
- [Project layout](#project-layout)
- [Development](#development)
- [License and provenance](#license-and-provenance)

## Why

Most frontier models already roughly follow a sane investigation flow on their own. NeuroTune exists to make that flow **repeatable, evidence-bound and consent-gated** rather than to replace the model's own judgment — a thin skill layer, not a second agent. It is the successor to an earlier standalone desktop app project ([`NeuroTune`](https://github.com/PrimeBuild-pc/NeuroTune), now retired): same investigation goal, no app, no provider lock-in, no separate UI to maintain.

## How it works

The skill walks a conversation through six steps, each gated on explicit consent, never run silently in a batch:

1. **Scope and guide** — `doctor`/`goals`/`help` establish objectives and capabilities; at most two plain-language questions at a time.
2. **Consent** — nothing is collected or shared without being asked first; collecting local inventory and sharing it into the model's context are two separate approvals.
3. **Evidence** — every inventory, frame or app/service report is run through `evidence` and cited as `metric | value | full fact ID`, never pasted raw.
4. **Measure** — imports PresentMon CSV or a single CapFrameX JSON run; it does not record captures itself.
5. **Challenge** — states one plausible explanation plus at least one alternative, and names the measurement that would falsify it.
6. **Assess and review** — produces an evidence-linked plan or intervention proposal for the human to approve, choose from, or reject; no step in that proposal is ever executed by the skill.

Full workflow detail lives in [`skills/neurotune/SKILL.md`](skills/neurotune/SKILL.md).

## What it does

- Local Windows inventory with a separate collect-then-share step.
- PresentMon-compatible CSV and single-run CapFrameX JSON analysis: average present cadence, frame-time percentiles, stutter counts.
- Before/after comparisons with workload, duration, stream-identity and duplicate-source checks.
- Objective presets and short guided question batches — no predefined tweak bundles.
- Structured assessments bound to validated source reports, with missing coverage and uncertainty stated explicitly.
- Hardware-specific game-config discovery guidance for 13 games (candidate paths/formats/keys, not static presets).
- Evidence-linked advisory plans and intervention-proposal review, including dependency checks on selected steps. Invented fact IDs are rejected.
- A self-contained Agent Skills directory, Pi package and Claude Code plugin manifest, plus a JSON CLI any tool-capable harness can call directly.

## Not implemented

Apply/rollback, live ETW capture, a privileged broker, tool acquisition/installation, driver/firmware updates, MCP transport, GPU/BIOS tuning, security remediation, or automatic diagnosis outside a harness. Imported comparisons are descriptive, not proof an optimization worked. Read-only is the current runtime limit, not the destination — see [`docs/ROADMAP.md`](docs/ROADMAP.md) and the [build ledger](docs/BUILD-STEPS.md) for what assessment-driven execution would require (genuine approval, recovery, authority gates).

## Install

Requires **Node.js 22+**. Live inventory additionally requires Windows with Windows PowerShell 5.1. No `npm install` needed. Pick **one**:

| Harness | Command |
|---|---|
| Pi / any Agent-Skills-compatible harness | `node scripts/install.mjs --skills-dir "$HOME/.agents/skills"` |
| Project-scoped skill | `node scripts/install.mjs --skills-dir "C:/path/to/project/.agents/skills"` |
| Pi package (no copy) | `pi install "C:/path/to/NeuroTune.Skill"` |
| Claude Code plugin (no copy) | `claude --plugin-dir "C:/path/to/NeuroTune.Skill"` then `/neurotune:neurotune` |
| No Agent Skills support | Point the model at `skills/neurotune/SKILL.md` and ask it to follow the workflow (needs local file + command tools) |

The copy installer writes to `<skills-dir>/neurotune` and never touches harness settings. Reload/restart the harness afterward. See the [full per-harness table](docs/COMPATIBILITY.md) for Pi, Claude Code, OpenCode, Codex and Hermes, and [`docs/BUILD-STEPS.md`](docs/BUILD-STEPS.md) for the offline-archive install path.

## Use it

```text
/skill:neurotune Investigate stutter in my game. Collect locally first; ask before sharing hardware facts.
```

Or in any harness without a slash command: *"Use the NeuroTune skill to compare these before/after PresentMon captures. Explain uncertainty; do not apply tweaks."*

The skill asks about symptoms and data sharing, selects relevant evidence, challenges its own explanation and produces a bounded next step — using the harness's current model and current command-permission settings, with no second permission system layered on top.

## CLI quick start

```powershell
node skills/neurotune/scripts/cli.mjs doctor
node skills/neurotune/scripts/cli.mjs goals
node skills/neurotune/scripts/cli.mjs collect --out "inventory-01.json"
# Review inventory-01.json locally before letting it into a model context.
node skills/neurotune/scripts/cli.mjs evidence --input "inventory-01.json"

node skills/neurotune/scripts/cli.mjs frames --input "before.csv" --process "game.exe" --conditions "Same replay, 1080p, fixed graphics" --out "before.json"
node skills/neurotune/scripts/cli.mjs frames --input "after.csv"  --process "game.exe" --conditions "Same replay, 1080p, fixed graphics" --out "after.json"
node skills/neurotune/scripts/cli.mjs compare --baseline "before.json" --candidate "after.json"
```

Output is a single JSON envelope (`{ "ok": true, "data": ... }` / `{ "ok": false, "error": ... }`); errors exit nonzero. New report paths must be `.json`, and an existing report is never overwritten. See the [measurement contract](skills/neurotune/references/measurements.md) and [advisory plan format](skills/neurotune/references/plans.md) for the rest of the command surface (`apps-services`, `game-configs`, `assess`, `proposal-check`, `plan-check`, `frames-capframex`, `tools`).

## Safety and privacy

The local CLI has no arbitrary command endpoint and no system writer — it does not authenticate reports, sandbox the harness, or establish consent cryptographically. Run it from a non-admin harness session and review the harness's own tool permissions.

`collect` returns a receipt without hardware facts; later evidence/analysis output does enter the harness context, under that harness/provider's own retention policy. Data minimization is not anonymization — treat device strings and imported files as untrusted data. See [`SECURITY.md`](SECURITY.md).

The original desktop app's protected journals, approvals and rollback are a separate project; this skill neither reads nor modifies them.

## Project layout

```text
skills/neurotune/     Installable, self-contained runtime and workflow
  SKILL.md             Small, harness-independent entry point
  scripts/             JSON CLI, pure analysis, Windows collector
  references/          Guided assessment/review, inventory, measurement and plan formats
.claude-plugin/        Claude Code plugin metadata (no host-specific runtime)
scripts/               Copy installer and verification tools
tests/                 Unit, subprocess and installation tests
evals/                 Native case definitions (local transcripts/results excluded)
docs/                  Architecture, roadmap, compatibility and continuation notes
```

## Development

```powershell
npm test              # unit + CLI tests
npm run check          # static checks
npm run test:package   # installation-outside-repo checks
npm run test:native    # optional native contract tests (pinned .NET SDK)
npm run test:collector # explicit read-only live Windows smoke test
npm run pack:local      # build an offline-installable npm archive
```

`npm test` uses synthetic data only; live collection is opt-in. See [`docs/COMPATIBILITY.md`](docs/COMPATIBILITY.md) for the exact per-OS/Node test matrix, [`docs/VALIDATION.md`](docs/VALIDATION.md) and [`docs/ASSESSMENT.md`](docs/ASSESSMENT.md) for recorded model-behavior runs (including failures), and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) / [`docs/decisions/0001-skill-first.md`](docs/decisions/0001-skill-first.md) / [`docs/ROADMAP.md`](docs/ROADMAP.md) before expanding scope.

## License and provenance

MIT licensed — see [`LICENSE`](LICENSE). No proprietary AMD component or vendor tool is bundled. [`NOTICE.md`](NOTICE.md) records exactly what numerical logic was reused from the retired MIT-licensed NeuroTune desktop project and under what terms.
