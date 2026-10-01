# AI-led investigation, bounded execution

The main provider conducts the investigation and proposes improvements. NeuroTune supplies local observations, measurements, safe follow-up readers and recoverable execution. Optional System One is contestable topic advice, never an authority. Neither the model nor the application establishes universal optimality.

## Complete diagnosis

`ui/src/diagnosisFlow.ts` owns one cancellable lifecycle: local scan → workload preparation → automatic named WPR captures → local ETL quality checks → model investigation → final proposal. A selected running workload produces three matching baselines; without one, a single system-wide diagnostic is collected instead. Scan, traces and numerical analysis do not invoke either AI. The selected provider and optional assistant run only after these stages finish. During each measured interval, the workflow uses cheap UI timers and the existing recorder watchdog rather than repeatedly spawning native agents; state retrieval occurs after the capture deadline.

The user chooses objectives/context and consents to sanitized provider transmission when starting. The UI reports actual stages, operation logs and elapsed/countdown times, not fabricated percentages. A ten-second preparation period lets the user return to the workload. The application cannot guarantee identical game scenes, record absent games, or derive FPS/end-to-end input latency from interrupt traces. Frame metrics need their own supported acquisition/import path.

Cancellation waits for noncancellable recorder startup/finalization so the owned session is known, then releases only that session if still recording. Completed reports remain local. The existing independent watchdog finalizes captures on app closure; captured traces can be analyzed in advanced measurements. An unfinished recoverable run prevents another diagnosis. AI failure is not presented as a completed deterministic substitute plan.

## Analysis presets are prompt specializations

The primary and advanced diagnosis views share four selectable objectives. `LlmClient.AnalysisFocus` maps the validated objective to application-owned instructions on **every planner turn** and in the read-only comparison explanation:

| Preset | Investigation emphasis | Evidence caveat |
|---|---|---|
| Performance complessive (`balanced`) | Actual workload bottlenecks, sustained useful throughput, frame-time consistency where available, responsiveness and resource/power trade-offs. | Scheduling traces do not establish FPS improvements; use representative supported before/after measures. |
| Latenza del sistema (`systemLatency`) | Responsiveness, stalls/tail delays, CPU ready time, interrupts, paging/storage waits and display/input context. | DPC/ISR attribution is not causality or end-to-end input latency. |
| Ottimizzazione rete (`networkLatency`) | The actual symptom: latency, jitter, loss, disconnects or throughput; distinguish host, local link/router, WAN/ISP and remote-service issues. | Adapter counters/configuration do not measure ping/jitter or establish path causes; missing probes become explicit manual proposals. |
| Stabilità del sistema (`stability`) | Reproducible crashes/freezes/resets, device/driver/event/storage/memory evidence and uncertain thermal/firmware/tuning context, with data-integrity risks first. | Error IDs, old drivers, installed tuning software or a clean snapshot alone cannot prove causes, active overclocks or stability; stress tests/repairs/firmware operations are not automatic tools. |

These are **not predetermined tweak packages**, prescribed policy states or extra permissions. They change reasoning priorities, while workload choice, mandatory measurements, available readers, investigation budget, provider/attachment consent, evidence/quality gates, risk profiles and explicit approval remain independent. Every focus allows cross-domain investigation, user preservation constraints, uncertainty and no change. The model must explain goal relevance and trade-offs; no numeric priority weights or deterministic action list is added. Existing goal-conditioned local hypotheses remain contestable.

Persisted `fps`/`efficiency` goals remain valid with their previous enum values and dedicated legacy prompts; restoring one exposes its labelled legacy selection without silently converting the run. Stability is appended to the enum, not inserted into old values. Unknown objectives/risk enums are rejected before investigation. Changing the visible goal cannot rewrite a persisted run or its matching-goal validation.

## Investigation freedom and limits

- The model may request existing registered evidence or new local read-only observations: per-core counters, paging pressure, disk/free-space health metadata, display configuration, device errors, adapter error/traffic counters, service state, recent System warning/error IDs, and driver associations/version metadata.
- Readers have fixed projections and bounded output; model-supplied shell commands, WMI queries, filesystem paths, driver loading, network probes and firmware export are not tools. Unsupported requests produce explicit unavailability and can become manual follow-up proposals. These readers do **not** provide arbitrary Codex CLI powers or web research.
- New observations receive application-owned `investigation:` IDs, collection time/limitations and per-attempt/turn provenance; they are persisted with the run/audit and cannot replace original facts. Post-capture snapshots are not capture-time causal proof. Unknown is not zero.
- The default budget is **12 turns / 10 minutes after measurements**, configurable in provider settings within **2–32 turns / 1–30 minutes**. Existing-evidence requests remain bounded to 40 facts per turn. Evidence/output size limits and exact citations remain in force. The final turn is reserved by prompt for a diagnosis; exhaustion/failure is reported rather than inventing completion.
- `ConflictAnalyzer` still produces goal-conditioned, opinionated hypotheses. The main prompt explicitly permits challenging/dismissing them; there is no prescribed preferred MemoryCompression/PageCombining state. Local hypotheses are collapsed and labelled separately from AI conclusions.
- Useful outside-catalog proposals remain manual guidance or review-only script artifacts. Unknown executable/resource/update identifiers cannot gain write authority; they are downgraded to unverified manual proposals rather than rejecting an otherwise cited analysis.
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
| Registered capabilities | Only existing `ActionId` implementations enter `OptimizationEngine.ApplyAsync`. Guidance, generated scripts and external resources remain review-only. Unsupported BIOS writes/GPU IRQ candidates have no adapter. |
| Live availability | Action-specific state/access/prerequisites are inspected again before execution. Descriptive supported-build/hardware metadata is **not** a universal compatibility matrix or proof of benefit. |
| Approval and identity | Apply belongs to a persisted run and matches its approved IDs. High-risk actions require separate confirmation. Risk presets select, never authorize a write. |
| Recovery/backup | Verified restore point, original-state snapshots, Registry exports and an operation journal remain mandatory. Failure leads to verified rollback or visible recovery. |
| Baselines and comparison | Only valid workload baselines unlock approval. System-wide sessions are separately linked diagnostic evidence and never unlock apply. Comparison rejects invalid/mismatched captures and enforces drift/thermal constraints. Accepted repeated 3+3 comparisons support the decision gate, not causal proof. |
| Recorder/tool separation | Optional inference and SCEWIN export use recording interlocks. Elevated local-worker launches require a same-user, non-admin, medium-integrity linked UAC token or refuse before process creation; no hand-filtered token, station/desktop ACL changes or administrator fallback. SCEWIN additionally requires explicit consent, elevation, approved staged hashes and fixed export arguments; no security bypass exists. |

`LlmClient.ParseDiagnosis` validates structure, evidence provenance and capability kinds, **not universal semantic goal-optimality**. There is no separate universal `DiagnosisValidator`. Preserving consent, credentials, privilege separation, backups, quality checks and verification does not require imposing tuning conclusions on the investigator.

## Validation scope

Mocked lifecycle/provider tests cover sequencing, new evidence/audit persistence, manual downgrade, cancellation during recorder startup, quality rejection, system-wide scope, final-result/fallback separation and absence of automatic writes. They do not replace elevated WPR acceptance, representative in-game workloads, live main/auxiliary inference, compatible approved firmware exports or comparative evaluation against a generic CLI prompt.
