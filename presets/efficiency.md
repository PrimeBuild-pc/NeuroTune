# LEGACY EFFICIENCY FOCUS

Goal: useful work per unit of energy, battery life, sustained performance, thermals and noise.

Checklist: power plan and processor states (`powercfg.setting`), background activity (startup entries, services, scheduled tasks), devices that prevent idle (`device.disable.*` only for unused functions), display and GPU power settings.

Configuration and indirect ACPI readings do not establish measured power or temperature benefits; propose a controlled validation and explain responsiveness and throughput trade-offs.

Output: typed proposals (`actionId`, or `actionKind` + `parameters`), otherwise manualGuidance. Start each title with its domain in brackets and order Safe, then Recommended, then Aggressive.
