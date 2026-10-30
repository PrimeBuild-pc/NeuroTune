# Guided assessment and intervention review

Use this branch after choosing the user's objective, when preparing a structured assessment, or when the user wants to review possible changes. The current build prepares and checks proposals; it cannot apply them. The product's future execution capability must not be simulated with harness shell access.

## Guide a non-expert

1. Run `goals`. Translate the objective choices into the user's language. They help users describe a problem, not select tweaks. Offer another description in chat if none fits; clarify the closest supported goal rather than inventing a goal ID.
2. Ask at most two relevant questions at a time using the harness's interactive question controls when available, otherwise numbered choices in chat. Offer Not sure. Obtain collection and sharing consent separately; an objective or survey answer grants neither.
3. Use facts already available rather than asking the user to identify hardware or driver versions. Ask about symptoms, actual usage and priorities. For cleanup, first discover which apps/services/functions are present through a supported consented collector; the bundled hardware collector does not inventory them. For a supplied registry/SCM snapshot, follow [app/service imports](apps-services.md); direct native collection and modification are not shipped. Explain functions in ordinary language. Not using a function is evidence to investigate, not permission to remove it.
4. Run `tools` before claiming an integration is available. The capability list does not scan installed tools. Direct game capture, FrameView-specific parsing, downloads, installs, DDU and RAPR execution are not implemented. Request a supported capture/export where needed. Do not silently treat another CSV schema as PresentMon.

For game-specific config discovery and hardware-derived `game-file` proposals, follow [game-configs.md](game-configs.md). The catalogue supplies paths/formats/key hints, not installed-game evidence, preset values or a writer.

## Save a structured assessment

After permission to disclose the selected intake and reports to the current model, create intake JSON with exactly:

- `schemaVersion`: `1`.
- `goal`: one ID returned by `goals`.
- `workload`: the game/use case and repeatable scene (1–1000 characters).
- `symptoms`: the user's description and relevant context (1–2000 characters).
- `answers`: entries with exactly `questionId` and `choice`, using applicable IDs returned by `goals`; keep unanswered questions absent, use `unknown` only when the user actually chose it.
- `hypotheses`: zero to ten hypotheses using the [advisory plan fields](plans.md). Zero is valid while gathering information. State what would falsify a hypothesis in its uncertainty/measurement descriptions. Do not label model-authored hypotheses as confirmed diagnoses.

Run `assess --input <intake.json> --evidence <inventory-or-frames.json> [--evidence ...] --out <new assessment.json>`. It accepts 1–20 validated, distinct reports and produces a local receipt, not a fact list. Do not repeatedly collect to populate every field. Read the assessment only with sharing consent; use `evidence` on original source reports for exact citations. The assessment itself is not an inventory/frame evidence report.

Use its `nextQuestions` as the next small batch; skip already answered questions. An unknown answer is a recorded uncertainty, not an invitation to repeat the same question. Use a new output filename for each revision; revalidate the exact reports on resume. Coverage flags describe supplied artifacts, not inspection completeness. Machine identity, freshness, app/service coverage, temperatures/power and actual input latency remain explicit gaps where unsupported.

## Prepare a final review

Only after assessment, prepare an evidence-linked proposal with the user's understandable effects, risks, restart needs, backup, recovery limits and outcome checks. A diagnostic experiment is a hypothesis test, not a promised fix. Prefer comparable repeated before/after measurements and one factor at a time. Keep unsupported observations unsupported.

The proposal has exactly `schemaVersion:1`, `assessmentId` (the exact saved assessment ID) and `steps` (1–20). Each step contains:

- `id`, `title`, `category`, `hypothesisId`, `evidenceIds`.
- `target`, `expectedState`, `proposedChange`: bounded descriptions, not executable code or authenticated snapshots.
- `risk`: `low`, `medium`, `high` or `unknown`; `requiresRestart`: a boolean.
- `dependsOn`: unique prerequisite step IDs; use an empty array for independent steps. The ID `all` is reserved.
- `backup`: exactly `method` and `verified:false`; proposing a backup does not mean it exists.
- `recovery`: exactly `mode` (`automatic`, `assisted`, `unavailable`) and `description`. These are declared intentions, not guarantees; automatic firmware recovery is unsupported.
- `verification`: exactly `method` and `successCriteria`.

Supported category labels are `windows-setting`, `registry`, `game-file`, `service`, `app`, `powershell`, `tool`, `driver`, `firmware`. These labels do not imply an executable adapter. Keep text single-line and within the CLI validator's limits (title 256; other descriptions 2000 characters). Do not put write-command templates in proposals as a workaround for the absent executor.

Run `proposal-check --input <proposal.json> --assessment <assessment.json> --evidence <source.json> [--evidence ...] --select all`. All steps are validated, including unselected ones. For a subset, repeat `--select <step-id>` and explicitly include every dependency; the validator will not add unapproved prerequisites. It returns dependency-ordered steps with retained uncertainty and a hash binding the proposal, assessment and selection.

Present the final list in plain language with these choices:

- **Approve all**: express intent to proceed with the whole reviewed plan.
- **Choose steps**: express intent for selected steps and required dependencies; show their effective order.
- **Other**: revise the plan, request more measurements or stop.

**In this build, those choices can only finalize a non-executable review.** State that limitation before asking for a choice. `approvalGranted:false`, `executable:false` and `executorAvailable:false` must remain visible in the technical result. Neither a chat Yes, a review hash nor a model-written approval field grants execution. Never announce an applied change, verified backup or completed rollback from this workflow.

## Optional native-integration validation

For development/integration, `execution-check --input <bundle.json>` revalidates a self-contained bundle. It is **not Apply** and obtains no approval. Obtain sharing consent before reading its inputs or returning its detailed output.

The bundle has exactly `schemaVersion:1`, `kind:"execution-bundle"`, `assessment`, `reports`, `proposal`, `selection` and `parameters`. Embed the actual artifacts, not file paths or a model-written review receipt. Retain artifact limits; aggregate input is at most 12 MiB. Parameters have exactly `stepId`, `keyPath`, `valueName`, `desiredDword`, once per selected step. Registry steps require matching canonical targets (`HKLM [Registry64]\<keyPath>\<valueName>`) and proposed text (`Set DWORD to <unsigned integer>`). Alternatively, select exactly one game-file step with parameters `stepId`, `path`, `expectedSha256`, `desiredUtf8Base64`: target `Windows file [UTF-8]\\<path>`, expected state `SHA-256 <original hash>` and proposed text `Replace UTF-8 content with SHA-256 <desired hash>`. Content is canonical Base64 UTF-8 without NUL, at most 128 KiB. Mixed/multi-file selections and other categories fail closed. These are assessment-selected intents, not a tweak catalog.

The Registry schema-2 or game-file schema-1 native intent binds the exact source bytes and validated selected review. It remains inert: no original-state backup, machine attestation, human approval or execution is produced. The installable skill does not launch the repository-only native prototype. Do not use its private stdin verifier, JSON output or hash as an approval credential.

## Future execution conditions

A separate native module must capture actual originals, follow ordinary harness tool permissions and OS authority, bind the selected plan and protect backups/journals before any system write. Keep final plan confirmation in the harness without adding another command-permission policy. Material plan/risk changes require renewed approval; required backup failures block writes. Driver regressions may justify a compatible earlier package, not automatic cleanup. Firmware rollback may be unsupported. Treat tool installation and system modification as distinct approved effects. These are prerequisites for future implementation, not available commands.
