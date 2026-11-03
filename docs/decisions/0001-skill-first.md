# ADR 0001: New repository, skill-first, bounded local runtime

Status: accepted for the first read-only milestone. Revisit point 3 (writes) is superseded by [ADR 0002](0002-agent-applied-changes.md).

## Decision

Create `NeuroTune.Skill` beside the desktop repository. Ship a self-contained Agent Skills directory and dependency-free Node JSON CLI. Reuse the existing fixed Windows collector and numerical conventions. Leave privileged execution, ETW capture and vendor integrations out of the initial runtime. Keep all code and documentation in English.

## Alternatives challenged

| Option | Attraction | Why not now / counterargument |
|---|---|---|
| Turn the existing app into a plugin in place | Maximum reuse | UI, embedded agent, provider storage and elevated executor remain entangled in the deployable unit. Uninstall/migration could disrupt recovery. A separate product seam is safer. |
| Write only a Markdown skill | Fastest installation | Lets every model invent commands, math and safety checks. A prompt cannot provide deterministic evidence validation or recovery. Bundle the small runtime too. |
| Wrap every existing Agent command | Fast feature parity | Exposes provider and write operations to a general-purpose harness, including model-forgeable confirmations. Read-only commands also construct unrelated state. Audit and selectively extract instead. |
| Rewrite all C# in Node/Python | One runtime | Discards mature Windows-specific snapshots, policy checks and tests. Reuse native code when the privileged milestone arrives. |
| MCP server first | Broad tool discovery | Adds protocol/runtime and long-lived-tool overhead before a host requires it. The CLI already works in command-capable harnesses; MCP stays a thin future adapter. |
| Pi-only extension | Excellent local integration | Recreates lock-in. Pi package metadata is enough to install the same standard skill without host-specific executable code. |
| One command that collects and automatically diagnoses | Convenient | Immediately forwards machine facts to the model and hides the sharing decision. Keep collection receipt separate from evidence output. |

## Consequences

The first version is smaller than NeuroTune and deliberately cannot optimize settings. It can already collect evidence, compute and compare captures, and validate plans in a real harness. No claimed feature parity, performance benefit or security certification is inherited from the old app.

Node 22+ is a requirement rather than an embedded runtime. That is a real installation cost but common in coding-agent environments and smaller than shipping Tauri/.NET/Rust/Node together. Offline users can install from a reviewed source directory or local npm archive. There are no dependency lifecycle scripts.

Content IDs are references, not authentication. Consent is currently an agent workflow plus harness permission model; no technical claim of enforcement is made. Writes remain absent until an OS-enforced, human-controlled approval path exists.

## Revisit when

1. A supported host cannot run the CLI but can use MCP: implement and test a thin read-only adapter with an official maintained SDK, not a handwritten protocol.
2. Live ETW measurements are needed: extract a headless native measurement worker and preserve cancellation/watchdog/quiet-period behavior.
3. A first typed writer is requested: design the approval broker independently, then VM-test snapshot -> approval -> apply -> verify -> crash/restart recovery -> rollback. Do not treat chat consent as a broker token.
4. Maintenance demonstrates duplicated Windows behavior: move that behavior to a shared native module with original tests, not broad vendoring of the entire app.
