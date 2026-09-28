# Local latency implementation and validation — 2026-09-07

NeuroTune now records system-wide latency diagnostics without requiring a
target process, using its existing WPR/TraceEvent engine. It does not depend on
LatencyMon being installed. This is partial diagnostic coverage, not full
LatencyMon parity or proof that the machine has been optimized.

## Implemented and checked

- Start/stop/cancel system-wide captures and automatic deadline handling;
  live per-logical-processor Windows counter deltas with group/LP identity,
  actual sampling duration and busy/idle/DPC/ISR milliseconds.
- All processor entries in the analyzed report, scheduled busy/idle time,
  unobserved prefix time and separate DPC/ISR counts, totals, percentiles and
  maxima. Scheduled time includes interrupts and must not be added to them.
- Complete driver/core aggregates with UI pagination, the 50 longest interrupt
  events in temporal order, and local hard-pagefault counts/rates and resolution
  duration by captured PID. Ambiguous names stay Unknown; process names are
  excluded from normalized AI evidence.
- Corrected the original ISR/DPC completion-timestamp interpretation and union
  overlap calculation. Kernel image rundown now resolves modules which the
  old analyzer reported as Unknown. A trace's scheduling events must span 80%
  of its window; absent initial time is not invented as busy/idle.
- Versioned evidence survives frame import; comparisons reject mixed analyzer
  versions and unmatched diagnostics, and only compare metrics present in every
  run. Interrupt-share redistribution is not a gain. Repeated regressions lead
  to Rollback; no improvement leads to InsufficientEvidence.
- Opt-in BIOS/UEFI inspection in Settings and firmware consent for subsequent
  scans. No firmware write command is exposed.

WPR's documented [system keywords](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/keyword--in-systemprovider-)
include HardFaults. The [TraceEvent parser](https://github.com/microsoft/perfview/blob/main/src/TraceEvent/Parsers/KernelTraceEventParser.cs)
defines DPC/ISR elapsed duration relative to event completion and hard-fault
resolution duration. These are not equivalent to LatencyMon's separate
[interrupt-to-user-process measurement](https://www.resplendence.com/latencymon_interrupt2process).

## This machine

The physical host reports Ryzen 7 5800X3D, 16 logical processors, Radeon
RX 6950 XT, MSI MPG X570 GAMING EDGE WIFI (MS-7C37) revision 1.0 and AMI BIOS
1.S1 dated 2025-09-11. The collector's memory-profile conclusion remains a
heuristic, not proof of an XMP switch or exact timings.

`MSI_BiosSetting` exposes GetBiosSetting and SetBiosSetting method metadata.
A read-only GetBiosSetting probe for `SVM Mode` on both instances returned
no value. No setter was invoked. Production inspection reports the detected
interface separately from usable settings and does not guess item identifiers.
Exact setup reads/writes therefore remain unsupported for this BIOS. The
[MSI BIOS guide](https://www.msi.com/support/technical_details/MB_BIOS_Manual)
directs users to the product manual; it does not establish a version-specific
programmatic setup contract.

## Reproduce

Run in an elevated PowerShell after building the application:

```powershell
.\scripts\local-latency-validation.ps1 -AgentDirectory .\ui\src-tauri\target\release\agent -DurationSeconds 30 -Count 3
```

Full local results stay in `artifacts/`; a compact redacted result with exact
binary/profile hashes is stored in
[the validation summary](validation/local-latency-20260907.json).
The script verifies capture quality and default ETL deletion. These are
desktop/background diagnostic captures, with no controlled game scene, no
Candidate configuration and no system optimization applied. They do not
complete the physical AMD/NVIDIA DirectX 3+3 or affinity-rollback matrix.

Final captures on the packaged analyzer before the later watchdog pipe-isolation
fix (which does not change trace analysis):

| Capture | Actual duration | Lost events | Logical processors | Maximum DPC | Hard faults | Maximum hard-fault resolution |
|---|---|---|---|---|---|---|
| 1 | 30.398 s | 0 | 16 | 154.4 µs | 42 | 740.3 µs |
| 2 | 30.392 s | 0 | 16 | 178.4 µs | 20 | 787.1 µs |
| 3 | 30.460 s | 0 | 16 | 140.1 µs | 10 | 713.0 µs |

In all three, the largest observed DPC was attributed to `dxgkrnl.sys` on
logical processor 0. This identifies a repeatable observation in the Windows
graphics stack, not a proven faulty device or an optimal alternative core.
The next useful affinity experiment needs the user's actual game/workload.
The decline in pagefault counts is not a claimed gain: no setting changed.
Per-core busy + idle + unobserved time reconciled with each trace duration;
per-core and per-driver interrupt counts matched, live counter times reconciled
with their sampling windows, and WPR was no longer recording at completion.

## Remaining requirements

- A validated dedicated interrupt-to-process probe and observer-overhead
  characterization; firmware/SMI stalls are also not measured.
- Exact driver/device/service-stack causality, full process-lifetime handling,
  hard-fault timeline correlation and broader physical/multi-group coverage.
- Supervised IRQ/process/service writers with measured benefit, restart and
  exact rollback. This desktop's largest spike alone does not justify changing
  affinity, disabling a service or altering the BIOS.
- Exact board/firmware menu guidance and a supported OEM setup protocol with
  tested recovery before BIOS setting writes.

Validation updated 2026-09-28 after the watchdog fix: 70 .NET tests, 10 UI
tests and 2 Rust tests; .NET format/build, TypeScript/lint/UI build, Rust
format/clippy and native Tauri Release build passed. The earlier NuGet
vulnerability check passed; it was not repeated because dependency-feed access
was not authorized in this session. UI coverage includes server-rendered
evidence/escaping/pagination checks; privileged Start/live/Cancel and
interactive visual/scaling acceptance remain separate.
