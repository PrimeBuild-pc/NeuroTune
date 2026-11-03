# Source provenance

Original project: the NeuroTune desktop app by PrimeBuild (retired). Its source history is not published; github.com/PrimeBuild-pc/NeuroTune now hosts this skill.

Source revision: `15db2ed7c056fcbb70fca316a2a090c2d7f7f21b`

License: MIT, Copyright (c) 2026 PrimeBuild. The license is included in this directory.

## Byte-for-byte reuse

- Original: `tools/hardware-collector/Collect-NeuroTune-HardwareReport.ps1`
- Bundled: `scripts/Collect-Hardware.ps1`
- SHA-256: `e96f937e563348a9c5c20d05412647c8b9eecaa56d6ce574245379084367dd40`

The collector is unchanged. The surrounding Node module projects its output onto a smaller allowlist and controls private temporary staging. The collector's broader raw report is not exposed by the CLI.

## Adapted numerical conventions

`scripts/frames.mjs` adapts conventions from `src/NeuroTune.Core/PresentMonImporter.cs`: mean-derived present cadence, nearest-rank percentiles, at least 120 selected frames, 0.05–10000 ms valid intervals and a stutter threshold of max(50 ms, 2 * median).

Intentional differences: malformed/invalid selected input fails rather than being silently skipped; `DisplayedTime` alone is not accepted; instance/swap-chain attribution is checked; imported comparisons never imply Keep/Apply or equivalent native measurement quality. It is not a port of `MeasurementService`, ETW analysis or the optimization engine.

No original provider credentials, personal reports, proprietary AMD component, vendor binary, model weight, driver or journal is included.
