# Frame analysis and comparison

Obtain sharing consent before these commands. Resolve the CLI relative to the skill directory.

```powershell
node "<skill-directory>/scripts/cli.mjs" frames --input "before.csv" --process "game.exe" --conditions "Same replay, 1080p, fixed graphics, no FPS cap" --out "before.json"
node "<skill-directory>/scripts/cli.mjs" frames --input "after.csv" --process "game.exe" --conditions "Same replay, 1080p, fixed graphics, no FPS cap" --out "after.json"
node "<skill-directory>/scripts/cli.mjs" compare --baseline "before.json" --candidate "after.json"
```

Repeat `--baseline` and `--candidate` for independent runs, up to 20 each. Prefer at least three each and alternate baseline/candidate captures where feasible to reduce order effects. No workload is started or recorded by this package; capture using a tool the user already trusts. The agent should not perform background work during capture.

## Input contract

- UTF-8, comma-separated PresentMon-compatible CSV, at most 10 MiB.
- Exactly one `Application` or `ProcessName` column; matches Windows basename case-insensitively, ignoring `.exe`.
- `MsBetweenPresents` or `CPUFrameTime`. If both exist, `MsBetweenPresents` takes priority. Their definitions are not interchangeable: comparison requires the same selected column.
- `DisplayedTime` alone is rejected because it measures a different phenomenon.
- At least 120 selected frames; every selected value must be a finite decimal in 0.05–10000 ms. Invalid selected frames fail the import instead of silently making results look better. If the capture format contains a startup zero or missing interval, prepare a reviewed copy excluding that non-frame row and disclose the exclusion.
- Duplicate headers, malformed quoting and row-width mismatches fail closed. Other processes are counted as excluded.
- If `ProcessID` or `SwapChainAddress` exists, all selected frames must belong to one instance/stream. Both columns are required for comparison, although an exploratory import may lack them. Pre-filter multiple streams deliberately; never concatenate them as one benchmark.
- Optional `PresentMode` is retained and must match across comparisons. Absent modes are not proof of a stable display path.

## Definitions

`durationMs` is the sum of selected intervals, not independently verified wall-clock capture time. `averageFps = 1000 / mean(frameTimeMs)` describes present/CPU cadence, not necessarily displayed FPS. Percentiles use nearest rank: sorted index `ceil(p * N) - 1`. A stutter is a frame above `max(50 ms, 2 * p50)`. This is a descriptive heuristic, not a physiological input-latency threshold.

Comparison uses the median of per-run metrics (equal weight per run). It rejects reused CSV hashes, mismatched process, conditions, metric definition or modes, missing instance/stream identity, and interval-sum durations differing by more than 10%. Numeric deltas appear only when these gates pass. A rejected comparison returns `comparable: false`, reasons and no metrics; it is a completed assessment, not a CLI execution failure.

Every comparison returns `recommendation: insufficient_evidence`: hardware identity, configuration, temperature, capture overlap and user-described scene are not independently verified. Repeated imported samples do not change that. Hashes are not signatures; modifying a file changes its hash without creating a genuinely independent experiment. Frame numbers cannot establish input latency, network latency, causality or permission to keep a tweak.
