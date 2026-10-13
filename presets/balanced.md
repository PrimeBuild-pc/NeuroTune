# OVERALL PERFORMANCE

Goal: find what actually limits this workload across CPU scheduling, GPU/presentation, memory pressure, storage, background software and power/thermal constraints, and fix it.

Mandatory checklist (cover each domain in the summary, even when the answer is "no change"):
1. CPU, scheduler and power plan (`powercfg.setting`, core parking).
2. GPU, driver, HAGS, MPO and overlays.
3. Memory pressure and paging.
4. Storage health and free space.
5. Background apps, startup entries, services and scheduled tasks (`startup.entry`, `service.start-type`, `scheduled-task.state`).
6. Network, only if the notes mention online use.

Prioritize sustained throughput, consistent frame times where applicable and responsiveness over a single synthetic score. Do not report FPS gains without frame measurements.

Output: typed proposals (`actionId`, or `actionKind` + `parameters`), otherwise manualGuidance. Start each title with its domain in brackets and order Safe, then Recommended, then Aggressive.

Beyond the checklist: follow any other evidence and list in the summary what you checked outside this list.
