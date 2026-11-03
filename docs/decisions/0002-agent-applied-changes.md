# ADR 0002: Agent-applied changes guided by instructions, not a typed broker

Status: accepted (2026-11-03).

## Context

ADR 0001 kept writes out until a dedicated, OS-enforced approval broker existed. A repository-only native Apply prototype was built in that direction and then removed: it duplicated what the harness already provides (tools, permissions, elevation) and turned the skill into an execution platform. The product's value is the investigation and the instructions around it, not a second runtime.

## Decision

The agent applies changes itself, through the harness's own command and file tools, after the user confirms a reviewed plan. [references/apply.md](../../skills/neurotune/references/apply.md) tells it how: disclose risk and how far each step can be undone, save the original state and restoration material in a local session directory before each change, run one explicit command per action, verify by reading back, report honestly, never reboot automatically, and restore on request after re-checking the actual state.

The CLI stays a reader/validator with no system writer. `execution-check`, which only produced inert intents for the removed prototype, is deleted. No NeuroTune UI, model loop, permission system or broker is added.

## Consequences

- Safety rests on written instructions plus the harness's permissions and Windows authority. A model can still ignore instructions; that is disclosed, not hidden.
- Recoverability differs by category (exact for Registry values, service start types and config files; partial for removed apps and drivers) and is stated before confirmation.
- Apply/recovery behavior needs its own trials in a disposable Windows VM, separate from the investigation evaluations.
- Small helper scripts are added only if a trial shows the instructions are not enough.
