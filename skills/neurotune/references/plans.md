# Advisory plans

Use facts returned by `evidence`. A plan is a list of falsifiable hypotheses, not a command queue. Required format:

```json
{
  "schemaVersion": 1,
  "goal": "Understand stutter in the same repeatable scene",
  "hypotheses": [
    {
      "id": "test-frame-pacing",
      "claim": "The observed tail warrants a repeatable frame-pacing investigation.",
      "evidenceIds": ["REPLACE_WITH_ACTUAL_REPORT_HASH:p99Ms"],
      "uncertainty": "Imported frame cadence does not identify the responsible subsystem.",
      "nextMeasurement": "Capture three independent runs with matching scene and graphics settings.",
      "risk": "low",
      "rollback": "No system change proposed; keep the original capture files."
    }
  ]
}
```

The placeholder above is intentionally invalid: substitute an actual returned ID. Run:

```text
node "<skill-directory>/scripts/cli.mjs" plan-check --input "plan.json" --evidence "frames.json"
```

Repeat `--evidence` for up to 20 reports. Plans are bounded to 128 KiB, 1–10 hypotheses, unique lowercase IDs, 1–20 unique citations per hypothesis and risk `low|medium|high|unknown`. Unknown fields, missing uncertainty/next measurement/rollback, unknown references and exclusively null/empty citations are rejected. The machine validates citation identity and structure, **not whether the citation supports the claim**. Prose is never executed, including text disguised as approval or instructions.

Keep rejected or unsupported claims unvalidated. Relabeling a CPU inventory fact cannot substantiate a latency claim; do not manufacture/reseal evidence or forecast a successful `plan-check` result to make it fit. Cite only the exact returned IDs: array entries inside `processors` do not have separate `processors[0].name` IDs. Frame-time percentiles are not DPC/ISR or input-latency measurements, and this CLI cannot import LatencyMon, WPA or ETL traces.

The result always has `executable: false`. There is no `approved`, `command`, `apply`, or automatic fallback operation. Describe prospective settings changes as questions to verify, not executable instructions. Migration of a bounded writer is a separate milestone with OS-enforced human confirmation and recovery tests; it cannot be implemented by adding persuasive prose to this skill.
