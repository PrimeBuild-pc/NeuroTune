# Harness compatibility

## Verified locally for 0.1.0-alpha.2

Environment: Windows, Node 24.20.0. These are integration/discovery checks, **not model-behavior evaluations**.

| Host / path | Result | What was actually checked |
|---|---|---|
| Pi 1.3.0, explicit skill | Passed | Real RPC `get_commands` returns exactly `skill:neurotune` from this directory |
| Pi 1.3.0, local package | Passed | Same command is discovered with `sourceInfo.origin=package` through the package manifest |
| Claude Code 2.1.304, local plugin | Passed | Official strict manifest validation; actual `plugin details` inventory reports one NeuroTune skill and no agents, hooks, MCP or LSP servers |
| Standalone copied skill | Passed | Installer and CLI run from an unrelated directory; existing installation is not overwritten |
| npm archive | Passed | Offline installation includes the hidden Claude manifest and the self-contained skill; CLI and copy installer run |
| Codex / other skill-capable hosts | Not verified | Standard skill layout and generic CLI are available; automatic discovery has not been exercised |
| Linux / Node 22 | Not run locally | Targeted by the CI definition; no remote CI run is claimed |
| Model compliance / quality | Not evaluated | Nine behavioral cases are prepared; see the evaluation guide |

No model was prompted by either host smoke test. Pi reports zero tokens, assistant messages and tool calls. Claude receives only fixed inspection commands, not `-p` or `plugin eval`. Both scripts withhold inherited credentials and use temporary home/configuration directories. They do not install packages or edit the user's harness settings.

The Claude validator can suggest adding a marketplace install line. This is informational: no marketplace or public repository exists yet. Do not invent a GitHub URL to silence it.

## Repeat the host checks

Use installed, reviewed host binaries; these commands do not download or upgrade them:

```powershell
npm run test:pi -- --cli "C:/path/to/pi-coding-agent/dist/cli.js"
npm run test:claude -- --cli "C:/path/to/claude.exe"
# An npm-based Claude installation may instead expose an absolute cli.js path.
```

The scripts use a 45-second subprocess deadline and bounded output. Pi's JSONL is split only on LF, and replies are correlated by ID. EOF requests orderly shutdown. Any missing skill, duplicate/unrelated resource, nonzero process exit or protocol mismatch fails the check.

For direct Claude inspection from the repository:

```powershell
claude plugin validate . --strict --json
claude --plugin-dir "C:/path/to/NeuroTune.Skill" plugin details neurotune
```

`plugin validate skills/neurotune/SKILL.md` is not a skill lint command in the tested Claude version: it tries to parse that file as a JSON manifest. Use the plugin root and actual component inventory instead.

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

Implementation was checked against the installed Pi 1.3.0 documentation and Claude's official documentation/2.1.304 CLI. These interfaces may change; rerun the smoke checks after a host update.
