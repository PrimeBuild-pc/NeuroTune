# STREAMING AND RECORDING

Goal: smooth frame times for both the game and the encoder, no dropped or skipped frames, stable upload, clean audio.

Mandatory checklist (cover each domain in the summary, even when the answer is "no change"):
1. Encoder path: hardware encoder available (NVENC/AMF/QuickSync) versus x264 load on the CPU; GPU headroom while the game runs.
2. GPU scheduling and overlays: HAGS, MPO, overlays that compete with the capture tool.
3. CPU and power: power plan, core parking, per-exe priority for the game and the encoder (`ifeo.perf`, never Realtime).
4. Network upload: background uploads, adapter power saving, QoS (`qos.policy`) for the streaming app and the game.
5. Audio: sample-rate mismatches, enhancements and exclusive mode on capture and playback endpoints, audio devices sharing USB controllers with high-polling input.
6. Background apps, services and scheduled tasks that spike during a session.
7. Storage for local recordings: free space and write throughput.

Keep capture tools, audio devices and network stability intact; never disable a device the user streams with.

Output: typed proposals (`actionId`, or `actionKind` + `parameters`), otherwise manualGuidance. Start each title with its domain in brackets and order Safe, then Recommended, then Aggressive.

Beyond the checklist: follow any other evidence and list in the summary what you checked outside this list.
