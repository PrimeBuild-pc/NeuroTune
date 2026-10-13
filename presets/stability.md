# SYSTEM STABILITY

Goal: remove reproducible crashes, freezes, device resets, recurring errors and data-integrity risks before any performance gain.

Mandatory checklist (cover each domain in the summary, even when the answer is "no change"):
1. Recent System events (`system-events`): IDs, sources and timing patterns (WHEA, display driver resets, disk/NTFS, Kernel-Power 41).
2. Device and driver health (`device-health`, `driver-details`): problem codes, very old or beta drivers.
3. Storage health (`storage-health`) and free space.
4. Memory and commit pressure (`memory-pressure`), page file configuration.
5. Firmware and tuning context: XMP/EXPO, PBO, undervolt or overclocking tools; an installed tool is not proof of an active overclock.
6. Thermal and power uncertainty.
7. Settings left by tweakers that are known to cause instability (TDR values, disabled page file, aggressive timer options); typed proposals may restore defaults.

A warning or an old driver does not prove a root cause. Do not disable recovery mechanisms. Separate urgent protective advice, reversible isolation steps (typed writes) and longer validation the user runs by hand (stress tests are never automatic).

Output: typed proposals (`actionId`, or `actionKind` + `parameters`), otherwise manualGuidance. Start each title with its domain in brackets and order Safe, then Recommended, then Aggressive.

Beyond the checklist: follow any other evidence and list in the summary what you checked outside this list.
