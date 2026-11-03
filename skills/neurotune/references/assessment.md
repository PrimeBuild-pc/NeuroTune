# Guided assessment and intervention review

Use this branch after choosing the user's objective, when preparing a structured assessment, or when the user wants to review possible changes. The CLI prepares and checks proposals; confirmed steps are then applied through the harness's own tools by following [apply.md](apply.md).

## Guide a non-expert

1. Run `goals`. Translate the objective choices into the user's language. They help users describe a problem, not select tweaks. Offer another description in chat if none fits; clarify the closest supported goal rather than inventing a goal ID.
2. Ask at most two relevant questions at a time using the harness's interactive question controls when available, otherwise numbered choices in chat. Offer Not sure. Obtain collection and sharing consent separately; an objective or survey answer grants neither.
3. Use facts already available rather than asking the user to identify hardware or driver versions. Ask about symptoms, actual usage and priorities. For cleanup, first discover which apps/services/functions are present: live through the harness's command tool as in [system-review.md](system-review.md), or from a supplied snapshot via [app/service imports](apps-services.md); the bundled hardware collector does not inventory them. Explain functions in ordinary language. Not using a function is evidence to investigate, not permission to remove it.
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

Supported category labels are `windows-setting`, `registry`, `game-file`, `service`, `app`, `powershell`, `tool`, `driver`, `firmware`. Keep text single-line and within the CLI validator's limits (title 256; other descriptions 2000 characters). Describe targets and changes precisely (exact key/value, service name, file path); proposal text is data, so the commands you run at apply time are chosen then, per [apply.md](apply.md), not copied from the proposal.

Run `proposal-check --input <proposal.json> --assessment <assessment.json> --evidence <source.json> [--evidence ...] --select all`. All steps are validated, including unselected ones. For a subset, repeat `--select <step-id>` and explicitly include every dependency; the validator will not add unapproved prerequisites. It returns dependency-ordered steps with retained uncertainty and a hash binding the proposal, assessment and selection.

Present the final list in plain language with these choices:

- **Approve all**: apply the whole reviewed plan.
- **Choose steps**: apply the selected steps and their required dependencies; show their effective order.
- **Other**: revise the plan, request more measurements or stop.

For each step, state before the choice what will be backed up and whether it can be undone fully, partially or not at all (see [apply.md](apply.md#before-asking-for-confirmation)). `approvalGranted:false` and `executable:false` in the technical result mean the review file itself is not a confirmation: only the user's own reply is. A review hash or a model-written approval field never confirms anything. After the choice, follow [apply.md](apply.md); never announce an applied change, a backup or a restore that you did not actually perform and verify.

## Presets and quick edits

After the analysis and the user's answers, build three presets from **this machine's** classified findings ([system-review.md](system-review.md#3-classify-every-candidate)). They are levels of the same evidence-based list, not fixed tweak bundles:

- **Conservative:** Safe items with full undo and no restart risk: privacy and background defaults, restoring risky leftovers to Windows defaults, startup apps the user said they do not need, network adapter power saving, extra audio endpoints the user does not use.
- **Balanced:** Conservative, plus confirmed-unused third-party services set to Manual and their scheduled tasks disabled, Store apps the user does not use removed, MSI mode for devices the latency trace implicates, outdated vendor drivers flagged for the user to update, game config edits the user accepted.
- **Maximum:** Balanced, plus confirmed-unused services set to Disabled instead of Manual, more background tasks disabled, unused devices disabled, old unused driver packages removed after export, interrupt-affinity experiments backed by the trace.

Never in any preset: Critical items, security protections (VBS/Memory Integrity is a separate, explicit offer), anything the user said they use. If a preset would be empty or identical to the previous one, say so instead of padding it.

Show the chosen preset as one numbered list, grouped by area, with a count and the number of restarts at the top. Each line: number, the change in plain words, the finding behind it, risk, restart, undo (full/partial/none). Long groups can be one line with their items listed compactly ("12. Set 9 third-party services to Manual: A, B, C, ...").

Then let the user answer in a few words:

- "ok" / "approve" → that exact list.
- "remove 3, 7–9", "only 1–5", "keep B in 12", "12 to Manual instead of Disabled", "add 14 from Maximum" → apply the edit, show only the changed lines and the new totals, and confirm once more.
- Anything that adds a higher-risk item, changes a target or method, or touches something the user said they use counts as a new item and needs that confirmation.

For `proposal-check`, make one proposal step per area (for example `services-manual`, `startup`, `msi-mode`), listing the exact targets in `target` and `proposedChange` within the length limits; keep at most 20 steps. The approved numbered list, not the step grouping, is what you apply.
