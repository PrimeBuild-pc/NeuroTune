# NeuroTune Skill

**Evidence-led Windows performance investigation, inside the AI harness you already use.**

NeuroTune Skill removes the desktop UI, embedded agent and provider configuration from the investigation loop. Your harness supplies the model, conversation and permissions. A small local CLI supplies deterministic evidence and calculations.

**Status: 0.1.0-alpha.2 — read-only foundation with verified Pi/Claude loading, not feature parity with the desktop app.** No system settings are changed. No model/API credentials, cloud calls, background service or npm dependencies are required by this package. Your harness's inference may still incur costs and transmit tool results.

## What works

- Local Windows inventory with a separate collect-then-share workflow.
- PresentMon-compatible CSV analysis: average present cadence, frame-time percentiles and stutter counts.
- Before/after comparisons with workload, duration, stream identity and duplicate-source checks.
- Evidence-linked advisory plan validation. Invented fact IDs and authority fields are rejected.
- A self-contained Agent Skills directory, Pi package and Claude Code plugin manifests, and a JSON CLI usable by other tool-capable harnesses.

**Not implemented:** Apply/rollback, live ETW capture, privileged broker, MCP transport, GPU/BIOS tuning, security remediation or automatic diagnosis outside a harness. Imported comparisons are descriptive, not proof that an optimization worked.

## Install locally

Requires **Node.js 22+**. Live inventory additionally requires Windows and Windows PowerShell 5.1. No `npm install` is needed.

From this repository, choose **one** installation method:

### Shared Agent Skills directory (Pi / compatible harnesses)

```powershell
node scripts/install.mjs --skills-dir "$HOME/.agents/skills"
```

The installer copies the complete skill to `<skills-dir>/neurotune`, without changing harness settings. Reload/restart your harness. For a harness-specific location, pass that harness's documented skills directory instead, for example `$HOME/.claude/skills`. Discovery paths differ by harness/version; generic support means the directory and command interface are portable, not that every harness has been live-tested.

To use a project-scoped skill instead:

```powershell
node scripts/install.mjs --skills-dir "C:/path/to/project/.agents/skills"
```

### Pi package (alternative, no copy)

```powershell
pi install "C:/path/to/NeuroTune.Skill"
```

Pi loads the local package in place. Keep that directory available. Do not also install a copy into a discovered skill directory: duplicate names can shadow each other.

### Claude Code plugin (alternative, no copy)

```powershell
claude --plugin-dir "C:/path/to/NeuroTune.Skill"
```

Then invoke `/neurotune:neurotune`. This loads the same skill with a namespace, not another agent or provider. No hook, MCP server, startup command or permission grant is installed. For persistent standalone skill discovery, use the copy installer with `$HOME/.claude/skills` instead.

See the [compatibility matrix](docs/COMPATIBILITY.md) for what was actually tested. Loading a skill successfully does not certify that a model follows it.

### Offline archive

A local build produces `neurotune-skill-0.1.0-alpha.2.tgz`. Extract it in a new directory, then run the same copy installer:

```powershell
tar -xzf "neurotune-skill-0.1.0-alpha.2.tgz"
node package/scripts/install.mjs --skills-dir "$HOME/.agents/skills"
```

No registry access or dependency installation is needed. Verify a supplied checksum before extracting; a checksum identifies bytes, not publisher trust.

### Harness without Agent Skills support

Ask it to read `skills/neurotune/SKILL.md` and follow the workflow. It needs local file and command tools. A chat-only model cannot execute this package. No provider-specific SDK or plugin is necessary.

### Upgrade / uninstall

The copy installer refuses an existing `neurotune` directory. Review changes, move the previous installation aside, then install the new version. Reports are stored only at paths you explicitly chose, never inside a hidden application database.

For a copied skill, remove only the installed `neurotune` directory. For Pi package installation, use `pi remove "C:/path/to/NeuroTune.Skill"`. Neither operation should delete your evidence files.

## Use it

In Pi, reload and invoke:

```text
/skill:neurotune Investigate stutter in my game. Collect locally first; ask before sharing hardware facts.
```

In another harness: “Use the NeuroTune skill to compare these before/after PresentMon captures. Explain uncertainty; do not apply tweaks.”

The skill asks for the symptom and permissions, selects relevant evidence, challenges the explanation and produces a bounded next step. It uses your current model, without starting another agent loop.

### CLI quick start

From the repository (substitute the installed skill directory after installation):

```powershell
node skills/neurotune/scripts/cli.mjs doctor
node skills/neurotune/scripts/cli.mjs collect --out "inventory-01.json"
# Review inventory-01.json locally before allowing it into a model context.
node skills/neurotune/scripts/cli.mjs evidence --input "inventory-01.json"

node skills/neurotune/scripts/cli.mjs frames --input "before.csv" --process "game.exe" --conditions "Same replay, 1080p, fixed graphics" --out "before.json"
node skills/neurotune/scripts/cli.mjs frames --input "after.csv" --process "game.exe" --conditions "Same replay, 1080p, fixed graphics" --out "after.json"
node skills/neurotune/scripts/cli.mjs compare --baseline "before.json" --candidate "after.json"
```

Output is one JSON envelope. Errors exit nonzero. Reports never overwrite existing files; output parent directories must exist. A comparison can successfully return `comparable: false`: read its rejection reasons. See the [measurement contract](skills/neurotune/references/measurements.md) and [advisory plan format](skills/neurotune/references/plans.md).

## Safety and privacy

The local CLI has no arbitrary command endpoint or system writer. It does not authenticate reports, sandbox the harness, enforce model behavior or establish human consent cryptographically. Use a non-admin harness session and review its tool permissions.

Collection returns a receipt without hardware facts. Subsequent evidence/analysis outputs enter your harness context; remote models receive them under the harness/provider policy. Data minimization is not guaranteed anonymization. Treat device strings and imported files as untrusted data. See [SECURITY.md](SECURITY.md).

The original desktop app's protected journals, approvals and rollback remain separate. This project neither imports nor modifies them. Keep the old app available for recovery of changes it made.

## Development

```powershell
npm test
npm run check
npm run test:package
# Explicit read-only live Windows smoke test; no inventory facts are printed:
npm run test:collector
# Build an offline-installable npm archive:
npm run pack:local
```

The test suite uses synthetic data, invokes the actual CLI, and verifies installation outside the repository. Live collection is opt-in, not part of `npm test`. All 42 tests, static checks and offline package installation passed locally on Windows and Ubuntu/WSL2 with Node 22/24; see the [exact runtime matrix](docs/COMPATIBILITY.md). Remote CI runs have not occurred yet. Hardware acceptance and model behavior require separate tests.

```text
skills/neurotune/     Installable, self-contained runtime and workflow
  SKILL.md           Small harness-independent entry point
  scripts/           JSON CLI, pure analysis, fixed Windows collector
  references/        Inventory, measurements and advisory plans
.claude-plugin/      Claude Code metadata; no host-specific runtime
scripts/             Copy installer and verification tools
tests/               Unit, subprocess and installation tests
evals/               Nine native behavioral cases (source only; not yet run)
docs/                Architecture, migration gates and continuation notes
```

Optional host checks, using already-installed binaries, are documented in [compatibility](docs/COMPATIBILITY.md). They issue no model prompts and do not access user credentials. The [behavioral evaluation guide](docs/EVALUATION.md) describes the separate, explicitly approved model runs; `npm test` never starts them.

Read [architecture](docs/ARCHITECTURE.md), [decisions](docs/decisions/0001-skill-first.md), [roadmap](docs/ROADMAP.md) and [handoff](docs/HANDOFF.md) before expanding scope. [Provenance](NOTICE.md) records exactly what was reused from the MIT-licensed NeuroTune desktop project.

**No GitHub repository or npm publication is assumed.** Local artifacts and source installation work now. MIT licensed; no proprietary AMD component or vendor tool is bundled.
