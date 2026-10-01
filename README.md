<div align="center">
  <img src="readme-banner.svg" alt="NeuroTune — AI-guided Windows optimization with verified rollback" width="100%">
  <p><strong>AI-assisted Windows optimization with safety boundaries and reliable rollback.</strong></p>
  <p>
    <a href="https://github.com/PrimeBuild-pc/NeuroTune/commits/main"><img alt="Last commit" src="https://img.shields.io/github/last-commit/PrimeBuild-pc/NeuroTune?style=plastic&amp;logo=git&amp;logoColor=white"></a>
    <a href="https://github.com/PrimeBuild-pc/NeuroTune/stargazers"><img alt="GitHub stars" src="https://img.shields.io/github/stars/PrimeBuild-pc/NeuroTune?style=plastic&amp;logo=github"></a>
    <a href="https://github.com/PrimeBuild-pc/NeuroTune/issues"><img alt="Open issues" src="https://img.shields.io/github/issues/PrimeBuild-pc/NeuroTune?style=plastic&amp;logo=github"></a>
  </p>
  <p>
    <a href="https://github.com/PrimeBuild-pc/NeuroTune/actions/workflows/build.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/PrimeBuild-pc/NeuroTune/build.yml?branch=main&amp;style=plastic&amp;logo=githubactions&amp;label=build"></a>
    <a href="LICENSE"><img alt="License" src="https://img.shields.io/github/license/PrimeBuild-pc/NeuroTune?style=plastic"></a>
    <img alt="Status: Alpha" src="https://img.shields.io/badge/status-alpha-f59e0b?style=plastic">
    <img alt="Windows 11" src="https://img.shields.io/badge/Windows-11-0078D4?style=plastic&amp;logo=windows&amp;logoColor=white">
    <img alt="Tauri 2" src="https://img.shields.io/badge/Tauri-2-24C8DB?style=plastic&amp;logo=tauri&amp;logoColor=white">
    <img alt="React 19" src="https://img.shields.io/badge/React-19-087EA4?style=plastic&amp;logo=react&amp;logoColor=white">
    <img alt="Bring your own key" src="https://img.shields.io/badge/AI-BYOK-8b5cf6?style=plastic">
    <img alt="Local LLM support" src="https://img.shields.io/badge/local%20LLM-supported-2ea44f?style=plastic">
  </p>
</div>

<p align="center"><img src="docs/screenshots/neurotune-dark.png" alt="NeuroTune desktop overview in dark mode" width="100%"></p>

<hr>

<h2>⚡ What NeuroTune Does</h2>

<ol>
  <li>Collects a local Windows hardware and software profile.</li>
  <li>Collects measurements and sends sanitized evidence and any explicitly reviewed supporting files to your selected model.</li>
  <li>Lets the AI investigate and propose typed actions or review-only manual guidance; only registered actions can be executed.</li>
  <li>Creates a System Restore point and Registry backups before changing anything.</li>
  <li>Applies verified optimizations and records the previous state for one-click rollback.</li>
</ol>

<h2>🔒 Safety Model</h2>

<table>
  <thead>
    <tr>
      <th>Protection</th>
      <th>Behavior</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>Typed capability registry</td>
      <td>The LLM can select known <code>ActionId</code> values. Generated scripts remain reviewable artifacts and have no execution path inside NeuroTune.</td>
    </tr>
    <tr>
      <td>Exact evidence provenance</td>
      <td>Each model finding must cite an exact provided evidence ID and value. User reports/screenshots remain unverified; provenance checks do not prove an interpretation or performance benefit.</td>
    </tr>
    <tr>
      <td>Fail-closed backups</td>
      <td>No optimization runs if the required restore point or Registry backup fails.</td>
    </tr>
    <tr>
      <td>Transactional execution</td>
      <td>Actions are applied and verified individually; failures trigger reverse-order rollback.</td>
    </tr>
    <tr>
      <td>Protected credentials</td>
      <td>API keys are encrypted for the current Windows user with DPAPI and excluded from logs.</td>
    </tr>
    <tr>
      <td>Profile transparency</td>
      <td>The application shows the exact evidence facts sent to the provider and redacts the Windows username and device name.</td>
    </tr>
  </tbody>
</table>

<blockquote>
  <strong>Important:</strong> System optimization always carries risk. Test NeuroTune in a Windows virtual machine before using it on a primary PC, and keep an independent backup.
</blockquote>

<h2>Features</h2>

<ul>
  <li><strong>Deep evidence inventory:</strong> 83 typed Registry probes plus BCD, drivers, devices, filters, software, firmware, DIMMs, power, gaming, networking, runtime, and startup state.</li>
  <li><strong>Flexible model connections:</strong> OpenRouter, OpenAI, Anthropic, DeepSeek, any OpenAI-compatible or Anthropic-compatible API, Ollama, LM Studio, and vLLM.</li>
  <li><strong>Official browser authorization:</strong> OpenRouter OAuth and official Sign in with ChatGPT for eligible plan/credits, with PKCE and encrypted local credentials; API-key billing stays separate.</li>
  <li><strong>Native web UI:</strong> Tauri 2 and React with high-contrast light/dark themes, Windows appearance synchronization, a branded readiness-driven startup and restrained CSS motion. Decorative motion is disabled for reduced-motion users and during observed captures.</li>
  <li><strong>One complete diagnosis:</strong> choose objectives and a running workload, then automatically collect evidence and three matching traces before the AI investigation. Without a workload, a single system-wide diagnostic remains non-benchmark evidence. Actual phases/logs, elapsed time and cancellation replace mandatory intermediate buttons.</li>
  <li><strong>AI-led investigation:</strong> the model may challenge local heuristics and request new bounded read-only Windows observations. Default 12-turn/10-minute investigation budget is configurable; manual proposals outside the executable catalog remain visible. See <a href="docs/DETERMINISTIC_CONTROLS.md">investigation/execution boundaries</a>.</li>
  <li><strong>Optional supporting reports/screenshots:</strong> up to 8 user files (4 images), editable inert-text previews and explicit image/privacy consent. The selected main provider receives unverified supplemental evidence; no external app is launched and screenshot pixels are not saved in local run history. See <a href="docs/DETERMINISTIC_CONTROLS.md">formats, limits and provenance</a>.</li>
  <li><strong>Optional managed System One:</strong> opt-in Rizzo Flow topic classification with pinned downloads, private Python/runtime, real install smoke test, cancel/resume/removal, non-admin one-shot inference and recorder exclusion. Advisory only; no speed or accuracy guarantee.</li>
  <li><strong>Read-only comparison explanation:</strong> explicitly ask the selected main AI to interpret numerical results without replacing metrics, authorizing actions or changing the keep/rollback decision.</li>
  <li><strong>Goal-aware diagnosis:</strong> four investigation presets specialize the AI prompt toward overall performance, system latency, network optimization or system stability—not predetermined tweaks. Workload context, preservation notes, risk and approval stay independent; saved FPS/efficiency goals remain compatible.</li>
  <li><strong>Explicit conflict graph:</strong> names the exact settings and values involved, including timer, filter/VPN, overlay, memory, device, power, and recovery relationships.</li>
  <li><strong>Cancellable scans:</strong> cancellation terminates only the matching agent process tree and never keeps a partial profile.</li>
  <li><strong>Reviewable payload limits:</strong> advanced local-evidence tools expose fact count, UTF-8 size, privacy classes and the enforced single-pass evidence limit without adding a mandatory intermediate step.</li>
  <li><strong>Exact local baselines:</strong> versioned CPU and memory references match exact component identifiers; unknown hardware reports <code>baseline unavailable</code>.</li>
  <li><strong>User-controlled plans:</strong> risk-ordered AI proposals include evidence, expected impact, trade-offs, uncertainty and reversibility. Select some, all executable recommendations, or none; review-only guidance/scripts cannot gain write authority.</li>
  <li><strong>Current allowlisted actions:</strong> power, gaming, graphics, visual, memory, GPU-timeout, and legacy TCP repairs with local capture, verification, and rollback.</li>
  <li><strong>Local latency diagnostics:</strong> bounded system-wide or workload WPR captures, live per-core times, complete ISR/DPC aggregates, hard pagefaults and local spike timelines. Repeated workload comparisons and raw-trace deletion remain built in; interrupt-to-process latency is not yet measured.</li>
  <li><strong>Optional BIOS inspection:</strong> opt-in firmware/memory facts and an optional user-approved local SCEWIN export or offline import. Driver risks and unavailable values are explicit; BIOS writing is not enabled.</li>
  <li><strong>Shareable hardware matrix collector:</strong> a transparent, no-admin, offline CMD/PowerShell bundle records redacted AMD/NVIDIA, driver, CPU-set, and current interrupt-policy facts without changing the PC.</li>
  <li><strong>Local operation history:</strong> Per-action state snapshots and rollback from the desktop interface.</li>
  <li><strong>Honest telemetry boundary:</strong> low-level capabilities remain read-only and unavailable or driver-not-approved until a separate adapter and driver trust review is complete.</li>
</ul>

<h2>Contribute an AMD/NVIDIA hardware report</h2>

<p>Download or copy <code>tools/hardware-collector</code>, then double-click <code>Collect-NeuroTune-HardwareReport.cmd</code>. The dated JSON created beside it can be shared for the physical GPU/driver matrix. The collector does not require administrator rights, connect to the internet, install anything, or write system settings. Its PowerShell source and privacy exclusions are included in the same folder.</p>

<h2>Requirements</h2>

<ul>
  <li>A Microsoft-supported Windows 11 build, x64</li>
  <li>Administrator privileges</li>
  <li>System Protection enabled on the Windows drive</li>
  <li>A supported API credential, OpenRouter browser account, eligible ChatGPT plan authorization, or local OpenAI-compatible model server</li>
  <li>For source builds: .NET 8 SDK, Node.js 24, Rust stable, and the Visual Studio C++ desktop workload</li>
</ul>

<h2>Build from Source</h2>

<p>Run the following commands from an elevated PowerShell terminal:</p>

<pre><code>git clone https://github.com/PrimeBuild-pc/NeuroTune.git
cd NeuroTune
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
cd ui
npm ci
npm test
npm run tauri dev</code></pre>

<h3>Publish a Self-Contained Build</h3>

<pre><code>cd ui
npm ci
npm run tauri -- build --bundles nsis</code></pre>

<p>The GitHub Actions workflow produces a <code>NeuroTune-win-x64</code> artifact containing the unsigned per-machine NSIS installer, a no-install portable ZIP with both self-contained agents, and <code>SHA256SUMS</code>.</p>

<h2>Local Data</h2>

<p>Settings, DPAPI-encrypted API keys, redacted logs, Registry exports, and rollback manifests are stored in:</p>

<pre><code>%LocalAppData%\NeuroTune</code></pre>

<p>No API key or runtime profile is committed to this repository.</p>

<h2>Project Status</h2>

<p>
  NeuroTune v0.8.0-alpha.1 is an <strong>unsigned alpha</strong> intended for controlled testing. The project is MIT-licensed and
  produces an unsigned NSIS installer, a no-install portable ZIP, and SHA-256 checksums.
  Generated scripts may be reviewed or saved, but NeuroTune executes only typed, locally registered, reversible
  capabilities. Destructive cleanup and arbitrary model-generated writes remain excluded.
</p>

<h2>Documentation</h2>

<ul>
  <li><a href="ROADMAP.md">Product roadmap and release criteria</a></li>
  <li><a href="docs/IMPLEMENTATION_PLAN.md">Implementation plan</a></li>
  <li><a href="docs/DESIGN_SYSTEM.md">Design system and theme contract</a></li>
  <li><a href="docs/PROVIDERS.md">Cloud, custom, OAuth, and local provider guide</a></li>
  <li><a href="docs/TESTING.md">Alpha test guide</a></li>
  <li><a href="docs/SCEWIN_SYSTEM_ONE.md">Optional SCEWIN and managed System One scope</a></li>
  <li><a href="docs/DETERMINISTIC_CONTROLS.md">Deterministic vetoes versus opinionated planning policies</a></li>
  <li><a href="docs/VALIDATION_MATRIX.md">VM, accessibility, scaling, and hardware validation matrix</a></li>
  <li><a href="docs/PAWNIO_TRUST_REVIEW.md">PawnIO / LibreHardwareMonitor trust decision</a></li>
  <li><a href="docs/SECURITY_AUDIT_2026-08-02.md">Pre-publication security and privacy audit</a></li>
  <li><a href="RELEASE_NOTES.md">v0.8.0-alpha.1 release notes</a></li>
  <li><a href="LICENSE">MIT license</a></li>
  <li><a href="SECURITY.md">Security policy</a></li>
</ul>
