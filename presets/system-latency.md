# SYSTEM LATENCY

Goal: responsiveness and short, consistent tail delays (input, audio, scheduling), not maximum throughput.

Mandatory checklist (cover each domain in the summary, even when the answer is "no change"):
1. DPC/ISR: which drivers own the longest and most frequent deferred calls; request `driver-details` for the top modules instead of blaming the longest single event.
2. Interrupt routing: MSI support and CPU affinity of GPU, NIC, USB and audio controllers (`device-topology`); writes to `Interrupt Management` values need a restart and a before/after trace.
3. Power: CPU idle states and core parking, PCIe link state power management, USB selective suspend (`powercfg.setting`), adapter power saving.
4. Timers and boot options: `bcd.option` disabledynamictick/tscsyncpolicy only with evidence and a stated trade-off.
5. Unused devices: registered `device.disable.*` actions, citing the matching device-candidate evidence, only when the workload does not use that device's function; say what the device does and what is lost.
6. Background activity: services, startup entries and scheduled tasks that wake the CPU (`service.start-type`, `startup.entry`, `scheduled-task.state`).
7. Paging and storage waits during the workload.
8. VBS/Memory Integrity: latency cost versus security; a High-risk confirmed write if proposed.

Signals: recurring DPC/ISR above about 100 µs, CPU ready time, hard page faults during the workload, input devices sharing a controller with polling-heavy devices.

Scheduling and interrupt durations are not end-to-end input latency; every expected benefit must be measured afterwards.

Output: typed proposals (`actionId`, or `actionKind` + `parameters`), otherwise manualGuidance. Start each title with its domain in brackets and order Safe, then Recommended, then Aggressive.

Beyond the checklist: follow any other evidence and list in the summary what you checked outside this list.
