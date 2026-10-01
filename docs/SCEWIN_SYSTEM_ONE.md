# Optional SCEWIN export and managed System One

## SCEWIN: explicit local tool approval, not a driver bundle

NeuroTune now exposes three fixed agent commands: `scewin-inspect`, `scewin-export`, and `scewin-import`. They are separate from the normal Windows firmware reader and from AI diagnosis. No scan, diagnosis, model output or startup task invokes SCEWIN automatically.

In **Settings → BIOS / UEFI inspection**:

1. Enable **Allow BIOS reading**.
2. Supply a legally obtained local folder containing `SCEWIN_64.exe`, `amifldrv64.sys` and `amigendrv64.sys`. Files must be bounded local images, not network/reparse-point paths. **Inspect package hashes** displays their SHA-256 identity; it does **not** establish vendor authenticity, signature validity, redistribution rights or firmware compatibility.
3. Read the warning and explicitly approve the privileged package, including unsigned/unverified driver risk. Export also requires a separate confirmation and an administrator process. The exact approved hashes must still match after staging; editing the folder invalidates the UI approval.
4. The only process arguments are `/o /s <temporary nvram.txt>`, using a fixed executable name, `ArgumentList`, no shell, closed standard input and a 120-second deadline. Only those three approved package files are staged; package batch files and optional DLLs are not executed/copied. The existing recorder mutex serializes export against capture operations, and an active recording blocks export.
5. SCEWIN may internally load kernel drivers, access local data/credentials and affect the system even when asked only to export. This is **not** a sandbox or proof that external code is read-only. Driver-signature enforcement, Secure Boot, Memory Integrity, firmware locks and HII compatibility are never weakened. No `/i`, force option, BIOS setter or driver-install/retry workaround is exposed. A loaded driver may persist until reboot; restart before latency testing if necessary.
6. Output is parsed locally and shown in a searchable, paginated settings list. Only an explicitly starred current option or a raw numeric `Value` is reported. `BIOS Default` is never substituted for an unknown current setting; missing/conflicting markers remain unknown. Common password/secret/serial/UUID/MAC/asset-tag/email/username/SSID labels are omitted. This is not guaranteed anonymization; never share an unreviewed raw dump. Raw exports are bounded to 16 MiB and 10,000 questions; temporary files are removed, with a warning if cleanup fails.
7. Reports carry source, local read time, export hash, tool hashes and limitations. An import hashes decoded text re-encoded as UTF-8; a fresh export hashes the exact file bytes being parsed. Setup values are observations, not validated tuning recommendations. They are not automatically included in an AI payload or persisted as a raw firmware dump. Existing manually imported text is labelled unverified in origin, freshness and board association.

An existing UTF-8/UTF-16 `nvram.txt` can instead be imported through the file control without elevation or any tool/driver execution. This is the safer route when Windows blocks a driver. Non-ASCII legacy code-page exports must first be converted to UTF-8/UTF-16; no encoding is guessed silently.

### Provenance and current validation boundary

[SCEHUB](https://github.com/ab3lkaizen/SCEHUB) was used to understand its export invocation and required filenames; its binaries/drivers were not copied into NeuroTune. The repository license endpoint did not establish redistribution rights. NeuroTune therefore does not distribute or download the AMI package. Obtain the correct package and compatibility guidance from a licensed, trustworthy source.

No suitable local SCEWIN package was found during this session. No driver was run, installed, unblocked or tested on the MSI MPG X570 GAMING EDGE WIFI. Export integration and parser checks are implemented; live elevated compatibility remains unverified. A Windows-observed MSI WMI interface is not a validated mapping of actual setup questions.

Validation after implementation: 76 .NET and 15 UI tests passed, along with typecheck, lint, .NET/Rust formatting checks, native Release build and diff checks. Packaged-agent checks verified offline import/redaction, consent/invalid-input rejection and resource hashes. Delayed mocked browser flows covered ChatGPT registration/reuse/account selection/sign-out/model labels/acknowledgement, SCEWIN inspection/approval/confirmation/import/pagination, and existing measurement operations at a 907×573 viewport with keyboard navigation and reduced motion. These checks do not replace live OAuth/plan inference or elevated SCEWIN/WPR testing.

## System One: useful for narrow auxiliary classification, not system authority

Sources reviewed: [Mapika/decider](https://github.com/Mapika/decider) and [Rizzo-AI-Academy/rizzo-flow](https://github.com/Rizzo-AI-Academy/rizzo-flow). Both describe typed decisions/probabilities from model evaluation rather than free-form token generation. Zero generated tokens does not mean zero CPU/GPU/RAM cost, guaranteed speed or correct decisions. Published figures below are their authors' measurements, not NeuroTune benchmarks.

| Candidate | Practical benefit | Limit for this app |
|---|---|---|
| decider 2B/4B | Purpose-trained typed decisions, documented calibration and GGUF weights around 1.3/2.7 GB (Q4_K_M); Apache-2.0 project | Torch dependency remains; documented GGUF path uses llama-cpp-python and is not served by its HTTP server. Larger knowledge/reasoning models need much more memory. Calibration on their datasets is not calibration on Windows tuning. |
| Rizzo Flow 4B | Simple local HTTP `POST /v1/systemone` interface and packaged llama.cpp backends including Vulkan/CPU; Apache-2.0 project | Default Q8 model is about 4.4 GB, with reported GPU memory around 5.6 GiB. Its small 1.7B alternative is less accurate. Probabilities require domain calibration; its own fine-tuned accuracy is still imperfect. |

For a first **Windows/AMD-friendly API prototype**, Rizzo Flow is the more convenient integration target. For a lighter **CPU/GGUF classification experiment**, decider-2B Q4 is interesting, but needs its own library/sidecar adapter rather than pretending it is a standard chat API. Project licenses do not replace a review of the exact downloaded model weights, runtime binaries and notices.

Suitable auxiliary tasks:

- Classify free-form user goals into a fixed set of UI intent choices and suggest them for confirmation.
- Tag locally reviewed log snippets into known troubleshooting categories; include an explicit `unknown`/abstain choice.
- Rank which existing read-only diagnostic panel to show next, without hiding exact evidence.

Unsuitable authority: choosing a BIOS value, disabling protections, executing a command, installing a driver, treating a probability as causal proof, or deciding to commit/keep system changes. Existing deterministic validation, catalog allowlists, evidence checks and explicit user approval remain authoritative. Simple observable checks (recorder state, prerequisites, numeric thresholds) should remain ordinary code rather than model calls.

## OpenRouter alternative (explicit cloud consent)

Settings → System One assistant also offers a separate OpenRouter API key/model instead of local Rizzo. No model/runtime download is needed. Cloud enablement explicitly consents to API charges and sampled-context transmission; main ChatGPT/OpenRouter credentials and model remain untouched. The key is DPAPI-protected independently and can be deleted separately. This auxiliary path uses bounded chat generation for the same fixed topic categories, **not** Rizzo's probability scoring; scores remain null. Unknown/malformed/refused/incomplete outputs do not become actions. A 30-second/256-output-token bound and no automatic provider/local fallback apply. Native capture exclusion applies to both sources. Common identity/drive-path redaction is best-effort, not guaranteed anonymization. Live billing/accuracy/speed have not been tested with a real key.

## Implemented optional component

**Settings → System One assistant → Local** is disabled by default. Consent starts an app-managed Windows x64 installation; no external Python, server or command line is required from the user. Choose Rizzo Flow 4B Q8 (approximately 4.4 GB weights) or 1.7B Q8 (approximately 1.8 GB), and automatic Vulkan/CPU or CPU-only compute. Reserve at least 7/4 GiB disk respectively, plus sufficient RAM/VRAM; the larger model is not automatically better or faster on this PC.

The installer pins Rizzo source revision `b9ba007ee4d2928bbab5b1d8bfe9009c3696b6de`, uv `0.12.21`, private Python `3.12.12`, llama.cpp `b11081` Windows Vulkan, and immutable model revisions/SHA-256 digests. Source, uv, runtime and weights downloads have size/hash checks, resumable partial files and bounded safe ZIP extraction. Python and transitive dependencies are installed by uv from the pinned lockfile; this is a supply-chain dependency, not proof of absence of vulnerabilities. Upstream Rizzo, uv, llama.cpp, Python and model licenses still apply; these assets are not bundled into NeuroTune's executable.

Progress reports real download byte counts, environment creation, verification and a **real local inference smoke test**. The component is enabled only after that smoke test succeeds. Cancel leaves bounded reusable partial downloads; **Remove local component / partial downloads** removes the private source/environment/cache/runtime/model directory. Closing the app cancels installation and read-only AI work, not apply/rollback transactions or the recording watchdog. Installation and removal are rejected during active recording.

### Scope and non-interference

- A fresh one-shot Python worker classifies sampled context into `memory`, `drivers`, `network`, `power`, `software`, or `unknown`, with abstention. It does not generate system commands or run an HTTP server.
- Before diagnosis it samples at most 30 facts and may add up to 24 **existing original IDs** to the initial evidence selection; it never deletes evidence, rewrites values or changes user goals. The main provider receives the advice explicitly labelled untrusted/uncalibrated and may disagree. This can influence attention; it is not a promise of zero model influence.
- It annotates comparison aggregates and successful/failed application outcomes. Application annotations cannot alter transaction state or replace verified action decisions. They are classification notes, not a full troubleshooting dialogue.
- New guided UI runs prepare **three selected, matching, quality-valid workload baselines before AI diagnosis**. A preparation panel counts compatible captures and automatically selects successfully analyzed baselines; the user explicitly continues before provider transmission. System-wide captures do not unlock this path. Existing recovered runs retain their original recoverable lifecycle.
- After comparison, **Ask selected AI to explain** explicitly sends aggregates and tuning goals through the existing selected provider. Its narrative may disagree with the numerical recommendation; model-supplied action/decision fields are ignored, and the explanation cannot change metrics, execution approval or the keep/rollback state. No automatic provider or API billing fallback is added.

Inference and recorder startup share the existing capture mutex. Active recording skips optional inference. The worker and all descendants belong to a kill-on-close Windows Job Object; deadline, cancellation, owner death and normal return unload them. There is no cached resident model. If NeuroTune is elevated, Windows must provide a linked standard-user UAC token whose identity, non-admin role and medium integrity verify; otherwise the worker does not run. Hand-filtering the elevated token is not used: that can fail desktop access even in an interactive CI session. UAC-disabled/unavailable-token environments refuse before process creation, without modifying desktop ACLs or running elevated. Only its own standard-I/O handles and a small environment allowlist are inherited, not provider credentials from the parent environment.

**This is not a filesystem/network sandbox.** Downloaded runtime/dependency code still runs under the local user and can read files accessible to that user. Administrator export tools remain a separate and substantially higher-risk feature. Hashes establish pinned identity, not universal publisher trust. If optional loading/inference fails, original evidence and the selected main provider continue unchanged; caller cancellation is not converted into a successful advisory.

### Validation ceiling and pending work

Unit checks cover typed-choice/probability rejection, preservation of original evidence IDs, unsafe archive rejection, narrative-only provider explanations, native Unicode/environment isolation and worker deadlines. UI checks cover default-off state, advisory labelling and baseline grouping. **No full managed model download, elevated-token launch, actual GPU/CPU inference or NeuroTune accuracy/speed benchmark has been completed on this host.** The install-time smoke test is implemented, not evidence that a tested installation already exists.

Automated validation on 2026-10-01: **81 .NET, 17 UI and 2 Rust tests passed**, with typecheck/lint, .NET/Rust formatting, native Release build and diff checks. Delayed mocked browser checks covered optional consent/cancel/partial removal and measure-first/no-provider preparation at 907×573 with reduced motion and keyboard navigation; existing ChatGPT/SCEWIN/measurement browser checks passed again. Packaged-agent status confirmed default-off, and existing offline-import/consent/resource-hash checks passed. Executable: `ui/src-tauri/target/release/neurotune.exe`, built 04:21:40 local time, still v0.7.0-alpha.1. No model was installed, provider invoked, driver run, system tweak applied, commit made or PR merged by these checks.

Before expansion, measure held-out classification accuracy, abstention/calibration, p50/p95 latency including cold startup, RAM/VRAM, idle load and actual saved provider turns versus ordinary code. No acceleration guarantee is made. Optional installation is a user decision, not a prerequisite to normal NeuroTune operation.

**Deferred at the user's request:** simplify SCEWIN read-file import/upload into a clearer guided operation (file selection, encoding/provenance feedback, actionable import errors). Current offline import/export and consent boundaries remain unchanged. No automatic AMI download, BIOS writer or security workaround is pending implicitly.

See [the deterministic-control audit](DETERMINISTIC_CONTROLS.md) for the distinction between technical vetoes, evidence validation and opinionated planning rules.
