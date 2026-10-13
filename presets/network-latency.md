# NETWORK OPTIMIZATION

Goal: fix the user's actual connection problem: latency, jitter, packet loss, disconnects or throughput.

Mandatory checklist (cover each domain in the summary, even when the answer is "no change"):
1. Separate the layers: adapter/link/Wi-Fi, VPN or filter drivers, host load, LAN/router, ISP/WAN route, remote service.
2. Adapter settings: interrupt moderation, Energy-Efficient/Green Ethernet, flow control, RSS, offloads, receive/transmit buffers. Read them with `powershell-read` (`Get-NetAdapter | Select-Object Name, InterfaceGuid, Status` and `Get-NetAdapterAdvancedProperty -Name * | Select-Object Name, RegistryKeyword, RegistryValue, ValidRegistryValues`) before proposing typed `netadapter.property` writes with that GUID and keyword.
3. Traffic priority: `qos.policy` DSCP 46 for the game executable; it only helps when the router honours DSCP, say so.
4. Power saving on the adapter and on USB/Wi-Fi dongles.
5. Unused network, Bluetooth or Wi-Fi devices (`device.disable.*`) only when the user does not use that connection type.
6. Background bandwidth: updaters, cloud sync and launchers (`startup.entry`, `service.start-type`, `scheduled-task.state`).
7. Error counters (`network-counters`).

Adapter counters and TCP configuration cannot measure ping, jitter or path causality: when controlled measurements are missing, propose a manual test (ping or pathping to the game server) and never invent results. DNS changes and generic TCP values rarely change game latency; explain the trade-offs.

Output: typed proposals (`actionId`, or `actionKind` + `parameters`), otherwise manualGuidance. Start each title with its domain in brackets and order Safe, then Recommended, then Aggressive.

Beyond the checklist: follow any other evidence and list in the summary what you checked outside this list.
