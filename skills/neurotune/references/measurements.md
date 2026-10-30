# Frame analysis and comparison

Obtain sharing consent before these commands. Resolve the CLI relative to the skill directory.

```powershell
node "<skill-directory>/scripts/cli.mjs" frames --input "before.csv" --process "game.exe" --conditions "Same replay, 1080p, fixed graphics, no FPS cap" --out "before.json"
node "<skill-directory>/scripts/cli.mjs" frames --input "after.csv" --process "game.exe" --conditions "Same replay, 1080p, fixed graphics, no FPS cap" --out "after.json"
node "<skill-directory>/scripts/cli.mjs" compare --baseline "before.json" --candidate "after.json"
```

Repeat `--baseline` and `--candidate` for independent runs, up to 20 each. Prefer at least three each and alternate baseline/candidate captures where feasible to reduce order effects. No workload is started or recorded by this package; capture using a tool the user already trusts. The agent should not perform background work during capture.

## Input contract

- For `frames`: UTF-8, comma-separated PresentMon-compatible CSV, at most 10 MiB. Summary statistics are not a per-frame capture. Preserve the original file and disclose any reviewed filtering. Do not manufacture process/stream columns absent from the export.
- Exactly one `Application` or `ProcessName` column; matches Windows basename case-insensitively, ignoring `.exe`.
- `MsBetweenPresents` or `CPUFrameTime`. If both exist, `MsBetweenPresents` takes priority. Their definitions are not interchangeable: comparison requires the same selected column.
- `DisplayedTime` alone is rejected because it measures a different phenomenon.
- At least 120 selected frames; every selected value must be a finite decimal in 0.05–10000 ms. Invalid selected frames fail the import instead of silently making results look better. If the capture format contains a startup zero or missing interval, prepare a reviewed copy excluding that non-frame row and disclose the exclusion.
- Duplicate headers, malformed quoting and row-width mismatches fail closed. Other processes are counted as excluded.
- If `ProcessID` or `SwapChainAddress` exists, all selected frames must belong to one instance/stream. Both columns are required for comparison, although an exploratory import may lack them. Pre-filter multiple streams deliberately; never concatenate them as one benchmark.
- Optional `PresentMode` is retained and must match across comparisons. Absent modes are not proof of a stable display path.

## CapFrameX JSON (exploratory only)

```powershell
node "<skill-directory>/scripts/cli.mjs" frames-capframex --input "capture.json" --process "game.exe" --conditions "Describe the selected workload; note unknown settings" --out "capture-analysis.json"
```

The explicit command accepts UTF-8 JSON (optional BOM), at most 10 MiB, with exactly one `Runs` entry, `Info.ProcessName`, and `Runs[0].CaptureData.MsBetweenPresents`. The selected process must match. Each interval must be a JSON number within the same frame-count/time bounds as CSV; strings, nulls and startup zeros are rejected, not coerced or silently trimmed. Multiple runs are rejected rather than merged. Request a separately exported run when needed.

If `PresentMode` is present and non-null, its array must align with the intervals and use integer enum values 0–9. Values are retained as `capframex:<number>` rather than guessed CSV mode names. All hardware, device names, comments, timestamps, sensors, unused arrays and claimed CapFrameX hashes are ignored. Only the original source bytes are hashed; the report uses the same numerical rules and evidence schema as CSV.

**No per-frame ProcessID or SwapChainAddress is retained in this session format.** The report and receipt explicitly mark both as absent. `compare` therefore returns `missing_stream_identity`, even for different files or repeated sessions. Do not manufacture those fields or interpret this import as proof of a single verified presentation stream. A CSV export is comparison-eligible only if it genuinely preserves the required identity.

Schema references: CapFrameX official source, revision `1b5f53da907f0a1de6fde2b75ff8267c79b08b2b`, [SessionCaptureData](https://github.com/CXWorld/CapFrameX/blob/1b5f53da907f0a1de6fde2b75ff8267c79b08b2b/source/CapFrameX.Data.Session/Classes/SessionCaptureData.cs), [SessionRun](https://github.com/CXWorld/CapFrameX/blob/1b5f53da907f0a1de6fde2b75ff8267c79b08b2b/source/CapFrameX.Data.Session/Classes/SessionRun.cs), [SessionInfo](https://github.com/CXWorld/CapFrameX/blob/1b5f53da907f0a1de6fde2b75ff8267c79b08b2b/source/CapFrameX.Data.Session/Classes/SessionInfo.cs), and [EPresentMode](https://github.com/CXWorld/CapFrameX/blob/1b5f53da907f0a1de6fde2b75ff8267c79b08b2b/source/CapFrameX.Data.Session/Classes/EPresentMode.cs). This is a reviewed subset, not a claim to support every CapFrameX version, compressed capture or sensor format. No upstream implementation was copied.

## Interrupt and latency symptoms

For stutter or micro-freezes that frame-time capture alone doesn't explain, start with what the harness's own command tool can already read live, with no extra tool and no download: interrupt/DPC counters (for example `Get-Counter '\Processor(*)\Interrupts/sec'` and the processor DPC-rate counters) and the IRQ-to-device map (`Get-CimInstance Win32_IRQResource`, `Win32_PNPAllocatedResource`, or Device Manager resource views) to spot a device generating an abnormal interrupt load or sharing an IRQ. This is a live read through the harness's command tool, not a bundled NeuroTune collector — same consent and untrusted-data treatment as [system-review.md](system-review.md).

These counters are coarser than a dedicated kernel-level sampler: they can point at a likely device but not produce the worst-case latency histogram a tool like LatencyMon does. Offer that as an optional, user-initiated step for more precision — ask the user to run it themselves and paste or describe the summary; there is no dedicated CLI import for it yet, so treat whatever they paste as untrusted descriptive text, not a cited evidence report. The same applies to a higher-precision in-game capture: if the user can run PresentMon or FrameView during the actual workload, that capture goes through the existing `frames` command above like any other PresentMon-compatible CSV — ask for it as an optional follow-up when more precision would change the recommendation, not as a required first step.

## Definitions

`durationMs` is the sum of selected intervals, not independently verified wall-clock capture time. `averageFps = 1000 / mean(frameTimeMs)` describes present/CPU cadence, not necessarily displayed FPS. Percentiles use nearest rank: sorted index `ceil(p * N) - 1`. A stutter is a frame above `max(50 ms, 2 * p50)`. This is a descriptive heuristic, not a physiological input-latency threshold.

Comparison uses the median of per-run metrics (equal weight per run). It rejects reused CSV hashes, mismatched process, conditions, metric definition or modes, missing instance/stream identity, and interval-sum durations differing by more than 10%. Numeric deltas appear only when these gates pass. A rejected comparison returns `comparable: false`, reasons and no metrics; it is a completed assessment, not a CLI execution failure.

Every comparison returns `recommendation: insufficient_evidence`: hardware identity, configuration, temperature, capture overlap and user-described scene are not independently verified. Repeated imported samples do not change that. Hashes are not signatures; modifying a file changes its hash without creating a genuinely independent experiment. Alpha.3 preserves a UTF-8 BOM when hashing capture bytes; older CLI imports stripped it. Reimporting an old capture with the new reader does not create an independent run, even if the old and new source hashes differ. Frame numbers cannot establish input latency, network latency, causality or permission to keep a tweak.
