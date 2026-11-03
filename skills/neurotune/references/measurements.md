# Frame analysis and comparison

Obtain sharing consent before these commands. Resolve the CLI relative to the skill directory. Frame times and driver latency are separate measurements; both are covered here.

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

## Driver latency (DPC and ISR)

Deferred procedure calls (DPCs) and interrupt service routines (ISRs) are driver code that runs ahead of normal threads. When one runs too long, the game, audio or input waits: this is what LatencyMon measures.

**Record.** `measure-latency --seconds 60 --out latency.json` records a built-in Windows kernel trace (no extra software) and reduces it to per-driver and per-CPU facts; the raw trace is deleted. It needs administrator rights and fails, without stopping anything, if another tool (LatencyMon, WPR, xperf) is tracing. Record the situation where the problem happens: ideally the game running in a repeatable scene, otherwise a normal desktop with the user's usual background apps. Ask the user not to start other heavy work during the recording. One trace is one sample: repeat it with the same scenario after changes to compare.

**Or import.** If the user prefers LatencyMon, ask them to run it for a few minutes in the same situation, then **File → Save report**, and run `latencymon --input "<report.txt>" --out latency.json`. Both produce the same `latency` report; LatencyMon adds interrupt-to-process latency and hard page faults, the kernel trace adds a per-driver table.

**Read.** Run `evidence` and use these rules of thumb, not hard limits:

| Fact | Usually fine | Worth investigating | Likely audible/visible |
|---|---|---|---|
| `highestDpcUs` | < 500 | 500–1000 | > 1000, especially if `dpcOver1000UsCount` is more than a few |
| `highestIsrUs` | < 100 | 100–250 | > 250 |
| `dpcTimePercent` (all CPUs) | < 1 | 1–3 | > 3 |
| a single CPU's `dpcPercent` in `perCpu` | < 3 | 3–10 | > 10: that core is overloaded by interrupts |
| `highestInterruptToProcessUs` (LatencyMon) | < 1000 | 1000–2000 | > 2000 |

A single spike matters less than repeated ones. `eventsLost` above zero or `invalidEvents` means the trace is incomplete: say so.

**Connect.** Map the driver to its device and settings before proposing anything:

- `ndis.sys`, `tcpip.sys`, Wi-Fi or Ethernet drivers: adapter power saving, interrupt moderation, outdated network driver.
- `nvlddmkm.sys`, `amdkmdag.sys`, `igdkmd*.sys`, `dxgkrnl.sys`: GPU driver version and its power management; MSI mode of the GPU.
- `storport.sys`, `stornvme.sys`, `iaStor*.sys`: storage driver, power management, MSI mode of the NVMe controller.
- `USBXHCI.SYS`, `USBPORT.SYS`, `HidUsb`: high-polling devices, hubs, the USB controller's MSI mode, selective suspend.
- `HDAudBus.sys` and audio drivers: unused audio endpoints, vendor audio suites.
- `ACPI.sys`, `intelppm.sys`, `amdppm.sys`: power plan and processor idle states; a BIOS setting the user changes.
- `Wdf01000.sys`: a framework hosting another vendor driver; look at which devices use it.
- `unknown`: the routine was not inside any driver image captured in the trace; do not guess.

A busy CPU 0 in `perCpu` with one device behind it is the case for testing interrupt affinity or MSI mode on that device. Then record again and compare the same facts.

Live counters (`Get-Counter '\Processor(*)\% DPC Time','\Processor(*)\% Interrupt Time'`) are a quick check while the user plays, but they do not name drivers.

## Definitions

`durationMs` is the sum of selected intervals, not independently verified wall-clock capture time. `averageFps = 1000 / mean(frameTimeMs)` describes present/CPU cadence, not necessarily displayed FPS. Percentiles use nearest rank: sorted index `ceil(p * N) - 1`. A stutter is a frame above `max(50 ms, 2 * p50)`. This is a descriptive heuristic, not a physiological input-latency threshold.

Comparison uses the median of per-run metrics (equal weight per run). It rejects reused CSV hashes, mismatched process, conditions, metric definition or modes, missing instance/stream identity, and interval-sum durations differing by more than 10%. Numeric deltas appear only when these gates pass. A rejected comparison returns `comparable: false`, reasons and no metrics; it is a completed assessment, not a CLI execution failure.

Every comparison returns `recommendation: insufficient_evidence`: hardware identity, configuration, temperature, capture overlap and user-described scene are not independently verified. Repeated imported samples do not change that. Hashes are not signatures; modifying a file changes its hash without creating a genuinely independent experiment. Alpha.3 preserves a UTF-8 BOM when hashing capture bytes; older CLI imports stripped it. Reimporting an old capture with the new reader does not create an independent run, even if the old and new source hashes differ. Frame numbers cannot establish input latency, network latency, causality or permission to keep a tweak.
