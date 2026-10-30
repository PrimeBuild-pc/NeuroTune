# Harness compatibility

## Verified locally for the alpha.3 source continuation

The source remains version `0.1.0-alpha.3` but is newer than the prior archive/shared installation. The current 30-file candidate-input manifest and four-cell verification snapshot are identified in [VALIDATION.md](VALIDATION.md#continuation-acceptance--2026-10-21-utc). Model results remain historical; no inference tested the revised eleven-case suite.

Harness checks: Windows, Node 24.20.0. Runtime checks also cover Windows Node 22 and Ubuntu under WSL2 with Node 22/24, as detailed below. These are integration/discovery checks, **not model-behavior evaluations**.

| Host / path | Result | What was actually checked |
|---|---|---|
| Pi 1.3.0, explicit skill | Passed | Real RPC `get_commands` returns exactly `skill:neurotune` from this directory |
| Pi 1.3.0, local package | Passed | Same command is discovered with `sourceInfo.origin=package` through the package manifest |
| Claude Code 2.1.307, local plugin | Passed | Official strict manifest validation; actual `plugin details` inventory reports one NeuroTune skill and no agents, hooks, MCP or LSP servers |
| Standalone copied skill | Passed | Installer and CLI run from an unrelated directory; existing installation is not overwritten |
| npm archive | Passed | Offline installation includes the hidden Claude manifest and the self-contained skill; CLI and copy installer run |
| OpenCode 1.18.16, project skill | Passed | Real `debug skill` from an isolated temporary project loads the copied skill from `.opencode/skills`; exactly one non-built-in skill, source description/path verified; no model prompt |
| Codex 0.153.4, project skill | Passed | Real app-server `skills/list` discovers the repository skill and OpenAI UI metadata; no thread or inference request |
| Hermes | Not verified | Official discovery paths documented below; no installed host available |
| Windows / Node 22.23.3 | Passed | Copied source snapshot: 55 passing tests, one POSIX-only skip, static checks and offline archive installation |
| Linux / Node 22.23.3 | Passed | Ubuntu 22.04.5 on WSL2, native Linux binary and ext4 source snapshot: 56 passing tests plus static/package checks |
| Linux / Node 24.22.0 | Passed | Same 56-test Linux checks, including correct `liveInventory=false` capability reporting |
| Model compliance / quality | Partial evidence, not certified | Real Pi/OpenAI and ordinary restricted Claude/Anthropic runs are recorded separately, including failures; see [validation](VALIDATION.md) |

No model was prompted by the host smoke tests. Pi reports zero tokens, assistant messages and tool calls. Claude receives only fixed inspection commands, not `-p` or `plugin eval`. All four discovery scripts withhold inherited credentials and use temporary home/configuration directories. They do not install packages or edit the user's harness settings.

The Claude validator can suggest adding a marketplace install line. This is informational: no marketplace or public repository exists yet. Do not invent a GitHub URL to silence it.

## Earlier alpha.3 runtime acceptance (historical)

Windows Node 22.23.3/24.20.0: 51 tests pass and one POSIX-only FIFO test is skipped. Ubuntu/WSL2 Node 22.23.3/24.22.0: all 52 tests pass, including that regression. Each environment also passed static checks and offline archive/install checks. See [VALIDATION.md](VALIDATION.md) for source-snapshot identity, exact scope and model-behavior limits. No global Node installation was replaced.

Alpha.3 restricts new reports to `.json` paths without alternate data streams. Previously created JSON reports remain readable regardless of their suffix; do not execute reports as scripts. Capture hashes now preserve a UTF-8 BOM, unlike older CLI imports. Numerical rules and report schema remain unchanged; no reports are rewritten during upgrade.

## Historical alpha.2 runtime acceptance

Tested source: `e7400154cbdb9f1fdc610cfc399454a6ac4e1489` (alpha.2).

| Environment | Node | Unit/CLI tests | Static checks | Offline archive/install |
|---|---|---|---|---|
| Windows x64 | 22.23.3 | 42/42 | Passed | Passed |
| Windows x64 | 24.20.0 | 42/42 | Passed | Passed |
| Ubuntu 22.04.5, WSL2 x64 | 22.23.3 | 42/42 | Passed | Passed |
| Ubuntu 22.04.5, WSL2 x64 | 24.22.0 | 42/42 | Passed | Passed |

Linux used kernel `6.18.33.2-microsoft-standard-WSL2`, UID 1000, and a clean Git clone on ext4 under `/tmp`, not the Windows-mounted checkout. Both runtimes reported `process.platform=linux`. Windows Node 22 reported `process.platform=win32`; the existing Windows Node 24 installation was not replaced.

Temporary official binaries were downloaded from `https://nodejs.org/dist/` and checked against the corresponding `SHASUMS256.txt`. HTTPS and checksum verification were not disabled. Windows curl initially failed its certificate revocation check; that download was retried through WSL's HTTPS client, not with weakened TLS flags. Checksums from the same origin detect byte mismatches; they are not independent publisher attestation.

For each runtime, the clean checkout ran:

```sh
node --version
npm test
npm run check
npm run test:package
```

Linux also ran `node skills/neurotune/scripts/cli.mjs doctor`: offline analysis is available, live Windows inventory is not, and system writes/provider calls remain disabled. npm versions were 10.9.9 for Linux Node 22 and 11.19.1 for Linux Node 24. Downloaded runtimes and temporary clones were removed afterward; no system package, shell profile or global Node installation was changed.

Local detailed logs are retained under ignored `artifacts/linux-acceptance.txt` and `artifacts/windows-node22-acceptance.txt`. This closes the local OS/Node matrix, **not** remote GitHub Actions execution, other Linux distributions, macOS, ARM64, live Windows collection on Linux, or model-behavior acceptance.

## Repeat the host checks

Use installed, reviewed host binaries; these commands do not download or upgrade them:

```powershell
npm run test:pi -- --cli "C:/path/to/pi-coding-agent/dist/cli.js"
npm run test:claude -- --cli "C:/path/to/claude.exe"
npm run test:opencode -- --cli "C:/path/to/opencode.exe"
npm run test:codex -- --cli "C:/path/to/codex.exe"
# An npm-based Claude installation may instead expose an absolute cli.js path.
```

The scripts use a 45-second subprocess deadline and bounded output. Pi's JSONL is split only on LF, and replies are correlated by ID. EOF requests orderly shutdown. Any missing target skill, unexpected enabled resource, nonzero process exit or protocol mismatch fails the check. Host built-ins are allowed where documented below.

For direct Claude inspection from the repository:

```powershell
claude plugin validate . --strict --json
claude --plugin-dir "C:/path/to/NeuroTune.Skill" plugin details neurotune
```

`plugin validate skills/neurotune/SKILL.md` is not a skill lint command in the tested Claude version: it tries to parse that file as a JSON manifest. Use the plugin root and actual component inventory instead.

## Portable installation by harness

Official documentation checked on **2026-10-21**. Paths are documentation-backed, not a claim that every host was executed. Use the existing copy installer; the destination is the **parent skills directory**, not `SKILL.md` or the `neurotune` subdirectory.

| Harness | User skills parent | Project skills parent | Invocation |
|---|---|---|---|
| Pi | `~/.agents/skills` or `~/.pi/agent/skills` | `.agents/skills` or `.pi/skills` | `/skill:neurotune` |
| Claude Code | `~/.claude/skills` | `.claude/skills` | `/neurotune`; plugin alternative below |
| Codex | `~/.agents/skills` | `.agents/skills` (working directory through repository root) | `$neurotune` or `/skills` |
| OpenCode | `~/.config/opencode/skills`; also shared `~/.agents/skills` | `.opencode/skills` or `.agents/skills` | Ask to use NeuroTune through the native `skill` tool |
| Hermes | `~/.hermes/skills` (active profile) | `.hermes/skills` or `.agents/skills` at the project root | `/neurotune` |

For example, run **one** of these from the source/archive root in PowerShell, choosing the host you use:

```powershell
node scripts/install.mjs --skills-dir "$HOME/.agents/skills"          # Pi / Codex / OpenCode
node scripts/install.mjs --skills-dir "$HOME/.claude/skills"          # Claude standalone
node scripts/install.mjs --skills-dir "$HOME/.config/opencode/skills" # OpenCode native
node scripts/install.mjs --skills-dir "$HOME/.hermes/skills"          # Hermes default profile
```

Hermes does not scan the shared user directory by default: configure its documented `skills.external_dirs` yourself if reusing `~/.agents/skills`. For a named profile or custom Hermes home, use that profile's actual skills directory. Project skills require Hermes project trust; review the complete bundle before granting it. Shared/external skill directories are not write-protected: Hermes can modify writable skills. Keep them in trusted, user-owned storage and review updates.

**Copy/archive installation is the supported Hermes path here.** Its URL/GitHub hub installers can select only explicitly referenced support files. Referencing `scripts/cli.mjs` alone does not guarantee that all ESM imports and the PowerShell collector are copied. Do not advertise a single-file URL installation without testing the complete installed bundle. No hub source or marketplace is published for NeuroTune.

OpenCode's slash-command metadata is optional; this skill does not require it. Use natural-language selection rather than assuming `/neurotune` exists in every OpenCode version. Its permissions still govern the `skill` tool. The smoke test deliberately uses the native project directory with external skill scanning disabled. An exploratory shared-directory check found that home environment overrides alone did not isolate the installed Windows host's global `.agents` scan; the final test rejects any unrelated non-built-in skill. Built-in OpenCode skills are allowed, not counted as NeuroTune components.

Codex's Windows profile lookup also scans global `.agents` paths despite temporary HOME/USERPROFILE variables. Its check first inventories metadata, then disables unrelated paths in a **temporary CODEX_HOME config**, re-lists and asserts exactly one enabled non-system skill. No user config is edited, credentials are withheld, and neither pass sends a thread/model request. System skills are permitted. This is enabled-skill isolation, not a claim that no global metadata was scanned.

## Model portability and authoring choices

- One standard `SKILL.md`, matching directory/name, concise trigger description, standard frontmatter, short ordered workflow and branch-specific references. No provider-specific prompt forks.
- `agents/openai.yaml` supplies only optional OpenAI UI labels and a `$neurotune` default prompt. It grants no tools, adds no dependency and selects no model. Other hosts use the same workflow without this file.
- No `model`, forked agent, hook, dynamic shell interpolation or `allowed-tools` grant in the shared entry point. Host tool names differ; host policy controls actual access. Skill text is not a permission boundary.
- Use the human-selected Anthropic/OpenAI model through the existing harness. Check that it can load references and execute the fixed CLI. A chat-only or remote host without local command access cannot perform local collection. Never repair missing capabilities by switching providers or uploading evidence.
- Resume from selected reports and human consent, validating reports before citing their IDs. An assistant-authored handoff is not approval. Short metadata and progressive disclosure reduce repeated context, but no token, latency or quality improvement is claimed without measured runs.

Tests enforce the current standard-field subset, metadata limits, local static ESM imports and optional UI defaults. They are targeted repository checks, not a general YAML parser or the upstream `skills-ref` validator. Observed model failures and current acceptance gaps are retained in [VALIDATION.md](VALIDATION.md); the repeatable native-evaluator procedure remains in [EVALUATION.md](EVALUATION.md).

## Invocation

- Pi: `/skill:neurotune` after installing the standard skill or Pi package.
- Claude local plugin: start `claude --plugin-dir "C:/path/to/NeuroTune.Skill"`, then use `/neurotune:neurotune`.
- Claude standalone copy: install with `--skills-dir "$HOME/.claude/skills"`, then use `/neurotune`.
- Other hosts: install in the host's documented skills directory or explicitly ask it to read the bundled `SKILL.md`. The host needs local command/file tools and Node 22+.

Use one installation method per host. A namespaced plugin and an unnamespaced copy can coexist, but retaining two versions increases the chance of invoking stale instructions.

Live Windows inventory still runs on the local Windows machine. A cloud/container host does not acquire access to that machine just by loading this plugin. Skill portability is not OS portability or a remote execution bridge.

## Primary references

- [Pi skills documentation](https://github.com/earendil-works/pi/blob/main/packages/coding-agent/docs/skills.md)
- [Pi package documentation](https://github.com/earendil-works/pi/blob/main/packages/coding-agent/docs/packages.md)
- [Pi RPC commands](https://github.com/earendil-works/pi/blob/main/packages/coding-agent/docs/rpc-commands.md)
- [Claude plugin manifest](https://code.claude.com/docs/en/plugins-reference)
- [Claude skill locations and invocation](https://code.claude.com/docs/en/skills)

- [Agent Skills specification](https://agentskills.io/specification)
- [OpenAI skill authoring and Codex discovery](https://developers.openai.com/codex/skills) (currently redirects to the shared skill-authoring guide)
- [OpenCode skill discovery and permissions](https://opencode.ai/docs/skills/)
- [OpenCode official documentation source](https://github.com/anomalyco/opencode/blob/dev/packages/web/src/content/docs/skills.mdx)
- [OpenCode CLI environment controls](https://opencode.ai/docs/cli/)
- [Hermes skills, profiles, external directories and trust](https://hermes-agent.nousresearch.com/docs/user-guide/features/skills/)

Implementation was checked against the installed Pi 1.3.0 documentation, Claude's official documentation/2.1.306 CLI (2.1.307 discovery subsequently rerun), OpenCode 1.18.16 discovery and the Codex 0.153.4 app-server protocol. OpenCode documentation was retrieved from its official repository when the documentation site rejected direct fetching. These interfaces may change; rerun the smoke checks after a host update.
