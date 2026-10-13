# COMPETITIVE FPS

Goal: the highest and most consistent frame rate with the lowest input-to-photon latency in the user's competitive game(s). Visual quality is secondary unless the notes say otherwise; anti-cheat compatibility must hold.

Mandatory checklist (cover each domain in the summary, even when the answer is "no change"):
1. GPU and driver: driver age, HAGS, MPO, per-game GPU preference, overlays (Game Bar, GeForce/Adrenalin, Discord, Steam), fullscreen optimizations (`appcompat.layer` DISABLEDXMAXIMIZEDWINDOWEDMODE on the game exe).
2. CPU, scheduler and power: active power plan, core parking, processor minimum/maximum state, boost mode (`powercfg.setting`), timer options (`bcd.option` disabledynamictick/tscsyncpolicy only with a reason), per-exe priority (`ifeo.perf` CpuPriorityClass 3 = High, never Realtime).
3. Interrupts, input and USB: DPC/ISR attribution from traces, polling-heavy devices sharing controllers, unused devices (`device.disable.*`).
4. Memory: commit pressure, paging, standby list, memory compression; no imposed preferred state.
5. Background apps and services: startup entries (`startup.entry`), launchers and updaters running during play, scheduled tasks firing during play (`scheduled-task.state`), services with no use on this PC (`service.start-type` Manual/Disabled).
6. Network for online play: QoS DSCP 46 for the game exe (`qos.policy`), adapter power saving.
7. Game: start from the game-package evidence when present; without one, search the web (the web-search tool or your own web search) for the game's config files and competitive settings, then read the files locally before proposing writes (config files, anti-cheat limits, cloud sync, benchmark method); config file settings that cap or smooth frames (`file.json`/`file.ini` under the user profile or Steam), in-game frame cap versus refresh rate, Reflex/Anti-Lag availability.
8. Virtualization-based security: VBS/Memory Integrity cost versus the user's security needs; turning it off is a High-risk confirmed write, never silent.

Signals: frame-time spikes, 1% low far below average, high DPC/ISR on GPU/NIC/USB/audio drivers, CPU-bound versus GPU-bound evidence, background CPU usage during the recording.

Output: one proposal per concrete change, typed (`actionId`, or `actionKind` + `parameters`) whenever a registered action or a primitive can perform it, otherwise manualGuidance. Start each title with its domain in brackets, for example "[GPU] Disable fullscreen optimizations for game.exe". Order Safe, then Recommended, then Aggressive using the risk field.

Beyond the checklist: investigate anything else the evidence points to and list in the summary what you checked outside this list.
