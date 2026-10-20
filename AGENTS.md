# NeuroTune Skill contributor instructions

- Read `docs/ARCHITECTURE.md` before changing contracts and `docs/HANDOFF.md` when resuming work. The roadmap defines milestone gates; do not silently promote deferred capabilities into the current release.
- Keep code and documentation in English. Runtime is Node 22+ ESM plus one pinned Windows PowerShell collector; no build or runtime npm dependencies in M0.
- The installable skill is self-contained under `skills/neurotune/`. Runtime imports must not reach outside that directory. Root scripts are development/installation tooling only.
- Read `SECURITY.md` before changing subprocesses, filesystem writes, evidence validation or authority. Never replace an absent executor with arbitrary shell commands or model-asserted approval.
- Run `npm test` and `npm run check` after changes. Run `npm run test:collector` only for an explicitly intended read-only Windows smoke test. System-changing tests are forbidden on the host.
- When `graphify-out/graph.json` exists, run `graphify query "<question>"` before source exploration; use the wiki for broad navigation and follow located sources. After code changes, run `graphify update .`. AST-only graphs do not index document meaning; read the architecture and roadmap explicitly.
- Tests use synthetic hardware/capture fixtures. Keep real reports, traces, credentials, local installs and build archives out of Git and npm payloads.
- See `docs/ROADMAP.md` for separable work units. Delegate only on explicit human request, include these Graphify rules in exploration prompts, and appoint one owner for shared contracts.
