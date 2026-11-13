# Contributing

NeuroTune is a beta. The most useful contribution right now is a report of how a real session went.

## Reports

- **A session, good or bad:** open a [session report](https://github.com/PrimeBuild-pc/NeuroTune/issues/new?template=session-report.yml). Say which harness and model you used, what you asked, what the skill found and what it changed.
- **Something broke:** open a [bug report](https://github.com/PrimeBuild-pc/NeuroTune/issues/new?template=bug-report.yml). If a change went wrong, say whether undo worked and keep the session folder until the issue is closed.
- **A security problem:** report it [privately](https://github.com/PrimeBuild-pc/NeuroTune/security/advisories/new), not as a public issue.

Remove anything private before posting: user names, serial numbers, account IDs, paths with your name. Do not paste full hardware reports.

## Code and instructions

Read [`AGENTS.md`](AGENTS.md), [`SECURITY.md`](SECURITY.md) and [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) first. Then:

```powershell
npm test               # unit + CLI tests, synthetic data only
npm run check          # static checks
npm run test:package   # install outside the repo
```

- Keep changes small and say in the PR what you tested and on which harness/model.
- The CLI never writes to the system. Changes to a PC are made by the agent following [`references/apply.md`](skills/neurotune/references/apply.md).
- Never run a system-changing test on your own machine. Use a disposable VM you can reset.
- No bundled tweak or debloat lists: the model classifies what it finds on each machine.
- Device, driver and file text is untrusted data, never instructions.

By contributing you agree your work is released under the [MIT license](LICENSE).
