<div align="center">
  <h1>🧠 NeuroTune</h1>
  <p><strong>AI-assisted Windows optimization with safety boundaries and reliable rollback.</strong></p>
  <p>
    NeuroTune profiles your PC, requests contextual recommendations from your preferred LLM,
    and applies only predefined, reversible system changes.
  </p>
  <p>
    <a href="https://github.com/PrimeBuild-pc/NeuroTune/actions/workflows/build.yml"><img alt="Build" src="https://github.com/PrimeBuild-pc/NeuroTune/actions/workflows/build.yml/badge.svg"></a>
    <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white">
    <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows&logoColor=white">
    <img alt="Status: MVP" src="https://img.shields.io/badge/status-MVP-orange">
  </p>
</div>

<hr>

<h2>⚡ What NeuroTune Does</h2>

<ol>
  <li>Collects a local Windows hardware and software profile.</li>
  <li>Sends the sanitized profile to OpenRouter, OpenAI, or Anthropic using your API key.</li>
  <li>Accepts recommendations only when they reference an action in the built-in allowlist.</li>
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
      <td>Closed action catalog</td>
      <td>The LLM can select known <code>ActionId</code> values; it cannot execute generated commands or scripts.</td>
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
      <td>The application shows the exact profile sent to the provider and redacts the Windows username and device name.</td>
    </tr>
  </tbody>
</table>

<blockquote>
  <strong>Important:</strong> System optimization always carries risk. Test NeuroTune in a Windows virtual machine before using it on a primary PC, and keep an independent backup.
</blockquote>

<h2>Features</h2>

<ul>
  <li><strong>Zero-input system profiling:</strong> CPU, GPU, drivers, memory, storage, Windows build, power plan, gaming settings, network configuration, startup items, processes, and services.</li>
  <li><strong>BYOK providers:</strong> OpenRouter, OpenAI, and Anthropic.</li>
  <li><strong>Optimization presets:</strong> Safe / Balanced, Extreme Gaming, and Custom.</li>
  <li><strong>Current allowlisted actions:</strong> High Performance power plan, Game Mode, HAGS, Game DVR, and Windows visual effects.</li>
  <li><strong>Local operation history:</strong> Per-action state snapshots and rollback from the desktop interface.</li>
  <li><strong>No proprietary telemetry:</strong> Runtime data remains local except for the profile explicitly sent to the selected LLM provider.</li>
</ul>

<h2>Requirements</h2>

<ul>
  <li>Windows 10 or Windows 11, x64</li>
  <li>Administrator privileges</li>
  <li>System Protection enabled on the Windows drive</li>
  <li>An OpenRouter, OpenAI, or Anthropic API key</li>
  <li>.NET 8 SDK only when building from source</li>
</ul>

<h2>Build from Source</h2>

<p>Run the following commands from an elevated PowerShell terminal:</p>

<pre><code>git clone https://github.com/PrimeBuild-pc/NeuroTune.git
cd NeuroTune
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
dotnet run --project src/NeuroTune</code></pre>

<h3>Publish a Self-Contained Build</h3>

<pre><code>dotnet publish src/NeuroTune/NeuroTune.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true</code></pre>

<p>The GitHub Actions workflow also produces a <code>NeuroTune-win-x64</code> artifact and SHA-256 checksum after every successful push to <code>main</code>.</p>

<h2>Local Data</h2>

<p>Settings, DPAPI-encrypted API keys, redacted logs, Registry exports, and rollback manifests are stored in:</p>

<pre><code>%LocalAppData%\NeuroTune</code></pre>

<p>No API key or runtime profile is committed to this repository.</p>

<h2>Project Status</h2>

<p>
  NeuroTune is currently an <strong>MVP</strong>. Destructive cleanup, arbitrary Registry tweaks,
  generic network “optimizations,” LLM-generated scripts, automatic updates, an installer,
  and code signing are intentionally excluded until they can be implemented and validated safely.
</p>

<h2>Documentation</h2>

<ul>
  <li><a href="docs/IMPLEMENTATION_PLAN.md">Implementation plan</a></li>
  <li><a href=".claude/PROJECT_SPECIFICATION.md">Project specification</a></li>
  <li><a href="SECURITY.md">Security policy</a></li>
</ul>
