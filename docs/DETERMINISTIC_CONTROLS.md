# AI-led investigation, bounded execution

The main provider conducts the investigation and proposes improvements. NeuroTune supplies local observations, measurements, safe follow-up readers and recoverable execution. Optional System One is contestable topic advice, never an authority. Neither the model nor the application establishes universal optimality.

## Default agent: Pi CORE, host-owned authority

The main investigation uses `@earendil-works/pi-agent-core`, not the entire
Pi coding CLI/TUI. CORE schedules sequential bounded turns and tools in memory;
NeuroTune owns the selected provider transport/OAuth, prompt/evidence preparation,
fixed read-only readers and all response, citation, approval and execution
validation. Model envelopes cross a bounded correlated host protocol and cannot
be rewritten by the bridge. There is no write-capable shell/file tool, built-in
coding-tool set or additional provider credential store. Model-written code runs
only as the read-only `powershell-read` command described below or, in Expert
mode, as a declared script (`script.declared`, see Execution vetoes).

The packaged Node/CORE process is short-lived, non-admin and cancelled with its
owner, not a filesystem/network sandbox. It does not require user npm, global
Pi or a local model, and never substitutes another agent/provider if startup,
protocol, budget or inference fails. Local observations may remain available
but are explicitly incomplete, not a successful agent diagnosis or Apply authority.
Builds bundle pinned dependencies/runtime/license notices before .NET tests;
see [contributor checks](TESTING.md#contributor-checks-no-live-provider-or-system-writers).

Optional System One remains separate. OpenRouter Free uses `openrouter/free`
with bounded zero `max_price` provider routing, randomly choosing an available
compatible free model subject to rate/availability limits. It requires a separate
API key and cloud consent, not installation; no paid/local fallback exists.
Old cloud configurations lacking separate consent require reconsent. A draft
source selection cannot disable or conceal the actual persisted paid source;
its enabled state/model/detail and costs remain authoritative until explicitly
saved/changed. Neither this classifier nor CORE expands write authority.

## Complete diagnosis

`ui/src/diagnosisFlow.ts` owns one cancellable lifecycle: local scan → workload preparation → automatic named WPR captures → local ETL quality checks → model investigation → final proposal. A selected running workload produces three matching baselines; without one, a single system-wide diagnostic is collected instead. Scan, traces and numerical analysis do not invoke either AI. The selected provider and optional assistant run only after these stages finish. During each measured interval, the workflow uses cheap UI timers and the existing recorder watchdog rather than repeatedly spawning native agents; state retrieval occurs after the capture deadline.

The user chooses objectives/context and consents to sanitized provider transmission when starting. The UI reports actual stages, operation logs and elapsed/countdown times, not fabricated percentages. A ten-second preparation period lets the user return to the workload. The application cannot guarantee identical game scenes, record absent games, or derive FPS/end-to-end input latency from interrupt traces. Frame metrics need their own supported acquisition/import path.

Cancellation waits for noncancellable recorder startup/finalization so the owned session is known, then releases only that session if still recording. Completed reports remain local. The existing independent watchdog finalizes captures on app closure; captured traces can be analyzed in advanced measurements. An unfinished recoverable run prevents another diagnosis. AI failure is not presented as a completed deterministic substitute plan.

## Analysis presets are prompts

Each objective is a Markdown prompt in `presets/` (embedded in the app) that `LlmClient.AnalysisFocus` adds to **every planner turn** and to the read-only comparison explanation: a mandatory checklist per domain, the signals to look for, the typed primitives that fit and the expected output. `presets/targets.md` lists known high-impact targets (for example MSI/IRQ keys, audio endpoints, NVIDIA and AMD settings) and is shown only when writes are possible.

| Preset file | Objective |
|---|---|
| `fps.md` | Competitive FPS: highest, most consistent frame rate and lowest input latency, anti-cheat compatible. |
| `streaming.md` | Streaming and recording: smooth game plus encoder, no dropped frames, low-risk changes. |
| `balanced.md` | Overall performance and responsiveness with resource/power trade-offs. |
| `system-latency.md` | Stalls, tail delays, CPU ready time, interrupts, paging/storage waits. |
| `network-latency.md` | Latency, jitter, loss or disconnects; host versus link, WAN or remote service. |
| `efficiency.md` | Less power, heat, fan noise and background work. |
| `stability.md` | Crashes, freezes and resets, with data-integrity risks first. |
| `privacy-security.md`, `compromise-review.md` | Advisory audits: data collection, Windows protections, Defender detections and signs of compromise, as hypotheses. |
| `hardware-layout.md` | CPU versus chipset attachment, PCIe link widths and a USB port plan; advice only. |

Presets are **not tweak packages**, prescribed policy states or extra permissions. They change reasoning priorities and the minimum checklist; the model may investigate beyond it when evidence points elsewhere, and may propose no change. Measurements are context, not a gate. Workload choice, available readers, investigation budget, provider/attachment consent, quality checks, risk profiles and explicit approval stay independent of the preset. `ConflictAnalyzer` hypotheses remain contestable.

Objective enum values are append-only, so a persisted run keeps its objective; unknown objectives and risk values are rejected before investigation. Changing the visible goal cannot rewrite a persisted run.

## Investigation freedom and limits

- The model may request existing registered evidence or new local read-only observations: per-core counters, paging pressure, disk/free-space health metadata, display configuration, device errors, adapter error/traffic counters, service state, recent System warning/error IDs, driver associations/version metadata, and the PCIe/USB device topology (link width/generation, CPU or chipset attachment, USB controller and port path, without serials or instance IDs).
- Fixed readers have fixed projections and bounded output. Model-written reads go through `powershell-read`: PowerShell's own parser describes the command, NeuroTune refuses any AST node, command or argument outside its read-only allowlist (no method calls, assignments, file redirection, `$( )`, `&`/`.` invocation, remote parameters or UNC paths), then runs it in Constrained Language mode as a non-admin process (admin only for `bcdedit`, `Get-MpPreference`, `Get-StorageReliabilityCounter`), with a 30 s limit and bounded, redacted output. Writes, driver loading, network probes and firmware export are not tools. Unsupported requests produce explicit unavailability and can become manual follow-up proposals. These readers do **not** provide arbitrary Codex CLI powers. Web search (the provider's own search on Anthropic, OpenRouter and ChatGPT, or Tavily with a saved key) is a separate planner tool; its results are untrusted data that can only motivate validated proposals.
- New observations receive application-owned `investigation:` IDs, collection time/limitations and per-attempt/turn provenance; they are persisted with the run/audit and cannot replace original facts. Post-capture snapshots are not capture-time causal proof. Unknown is not zero.
- The default budget is **12 turns / 10 minutes after measurements**, configurable in provider settings within **2–32 turns / 1–30 minutes**. Existing-evidence requests remain bounded to 40 facts per turn. Evidence/output size limits and exact citations remain in force. The final turn is reserved by prompt for a diagnosis; exhaustion/failure is reported rather than inventing completion.
- `ConflictAnalyzer` still produces goal-conditioned, opinionated hypotheses. The main prompt explicitly permits challenging/dismissing them; there is no prescribed preferred MemoryCompression/PageCombining state. Local hypotheses are collapsed and labelled separately from AI conclusions.
- Executable proposals use a registered `ActionId` or a typed primitive (`actionKind` + `parameters`). NeuroTune validates the type, parameters, protected targets and availability, derives the action ID and risk itself and shows the exact write; refused proposals stay manual guidance with the reason. Unknown executable/resource/update identifiers cannot gain write authority; they are downgraded to unverified manual proposals rather than rejecting an otherwise cited analysis.
- Startup entries, service start types and scheduled tasks use the typed `startup.entry`, `service.start-type` and `scheduled-task.state` primitives when the target is listed in the evidence. Other background-software and installed-app proposals are evidence-cited manual review only: no commands, removal/debloater scope inference or auto-execution; uncertain roles/dependencies require verification rather than disablement or uninstall.
- The final view orders proposals by risk and shows evidence, expected impact, trade-offs, prerequisites, uncertainty and reversibility. Missing model explanations remain explicitly unspecified. Users can select some, all executable recommendations, or none; nothing is auto-applied.

## Optional user-supplied reports and screenshots

The diagnosis page accepts **8 optional files including at most 4 images**. Text export formats are TXT/LOG/CSV/JSON/XML/HTML/HTM (UTF-8 or BOM-marked UTF-16); source limit 512 KiB, prepared limit 40,000 characters per report / 80,000 total. HTML/XML are displayed and sent as inert plain text, never rendered/executed or resolved over the network. PDFs, executables, archives and active documents are unsupported; export text or a screenshot instead. No report is silently truncated.

PNG/JPEG source images are bounded to 20 MiB, 8192 pixels per side and 20 megapixels before browser decoding. The browser re-encodes a static PNG through canvas, removing source metadata, resizing to ≤1600 pixels per side and reducing further if necessary to meet **768 KiB per prepared image**. The user must inspect the actual prepared preview for readability and private data. Backend validation independently bounds file counts/text/base64/PNG headers/chunks, forbids ancillary text/EXIF/active image extensions and rejects malformed containers. It does not claim vendor authenticity or forensic image validation.

Local `support-preview` performs best-effort identity/sensitive-labelled-line omission for reports. **Pixels are not anonymized.** Users can edit the report, refresh its local prepared preview, remove files and must approve preview transmission separately. Image inclusion additionally requires confirming the exact selected model supports images and accepting possible repeated-image cost on every investigation turn. There is no automatic model/provider fallback, separate OCR provider, Files upload API, screenshot capture or external app execution. A provider rejecting images is an explicit failed investigation, never a text-only success that discarded them.

`support:*` citations identify **unverified user-supplied evidence**, not measurements from NeuroTune. Prepared-content SHA-256 is recomputed locally and persisted with file metadata; it proves identity, not freshness/PC association/authenticity. Report chunks remain exact, Unicode-safe cited text. Screenshot findings cite the metadata and describe visual readings as unverified interpretations, never newly measured facts. Conflicting supplemental content must remain visible to the model rather than replacing observed scan/baseline evidence. Report/image content is untrusted data, including any embedded prompt instructions; executable authority does not expand.

Prepared report text and file names/hashes are retained in the normal run evidence/history. **Screenshot pixels are transient and are not stored in local run journals**; original user files are untouched. A retried/recovered request must supply matching prepared attachments or dismiss the old prewrite run and start again. Full text/images go only to the selected principal provider; the optional classifier receives its existing sampled local context, not uploaded contents. All supplemental evidence shares the existing 256,000-byte textual-evidence budget; oversized combined bundles fail explicitly without deleting mandatory evidence. Tauri also bounds serialized agent payloads to 6.5 MB.

## Execution vetoes

| Control | Actual boundary |
|---|---|
| Registered capabilities | Only existing `ActionId` implementations and the typed parametric primitives of `ParametricActions` enter `OptimizationEngine.ApplyAsync`. A parametric action is rebuilt from its journaled kind + parameters and refuses forbidden targets before capture. Guidance and external resources remain review-only. Model-written scripts run only as `script.declared` in Expert mode, after a blocking AST check and with typed, snapshotted targets (see `SECURITY.md`). Unsupported BIOS writes/GPU IRQ candidates have no adapter. |
| Live availability | Action-specific state/access/prerequisites are inspected again before execution. Descriptive supported-build/hardware metadata is **not** a universal compatibility matrix or proof of benefit. |
| Approval and identity | Apply belongs to a persisted run and matches its approved IDs. High-risk actions require separate confirmation. Risk presets select, never authorize a write. |
| Recovery/backup | Verified restore point, original-state snapshots, Registry exports and an operation journal remain mandatory. Failure leads to verified rollback or visible recovery. |
| Baselines and comparison | Since v2 F4 measurements are context, not a gate: approval works from `BaselinePending`, and Keep works from `CandidatePending` without a comparison. Comparison still rejects invalid/mismatched captures and enforces drift/thermal constraints; any accepted comparison matching the run is recorded for the decision, not as causal proof. Single records of a run's operation can be restored while the run waits for measurement or a decision. |
| Recorder/tool separation | Optional inference and SCEWIN export use recording interlocks. Elevated local-worker launches use a LUA-filtered, medium-integrity copy of NeuroTune's own token (Administrators deny-only, privileges removed); if the desktop refuses it, the worker is declined, with no station/desktop ACL changes or administrator fallback. SCEWIN additionally requires explicit consent, elevation, approved staged hashes and fixed export arguments; no security bypass exists. |

`LlmClient.ParseDiagnosis` validates structure, evidence provenance and capability kinds, **not universal semantic goal-optimality**. There is no separate universal `DiagnosisValidator`. Preserving consent, credentials, privilege separation, backups, quality checks and verification does not require imposing tuning conclusions on the investigator.

## Validation scope

Mocked lifecycle/provider tests cover sequencing, new evidence/audit persistence, manual downgrade, cancellation during recorder startup, quality rejection, system-wide scope, final-result/fallback separation and absence of automatic writes. They do not replace elevated WPR acceptance, representative in-game workloads, live main/auxiliary inference, compatible approved firmware exports or comparative evaluation against a generic CLI prompt.
