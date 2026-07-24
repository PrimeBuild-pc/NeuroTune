# Implementation Plan

## Alpha Architecture

- Windows 10/11 x64 desktop shell built with Tauri 2, React, TypeScript, and semantic CSS tokens.
- A local .NET 8 agent owns Windows profiling, credentials, backup, execution, and rollback; there is no remote NeuroTune backend or database.
- Built-in OpenRouter, OpenAI, Anthropic, and DeepSeek profiles plus custom and local compatible endpoints.
- OpenRouter browser authorization with PKCE; other providers use their supported API credential flow.
- Windows appearance synchronization with tested high-contrast light and dark themes.
- Closed local catalog: the LLM diagnoses and recommends but never generates executable changes.
- Verified restore point, Registry exports, per-action journal, and reverse-order rollback.

## Implemented Flow

1. Initialize the solution, repository, CI, and documentation.
2. Define system profiles, tuning goals, diagnoses, actions, telemetry, and operation manifests.
3. Protect API keys, support OpenRouter browser authorization, and dynamically discover provider models.
4. Stream a phased inventory of hardware/firmware, Registry, boot, device, driver, software, network, runtime, and service evidence.
5. Sanitize and preview the exact evidence facts sent to the selected provider.
6. Accept only findings whose evidence ID/value pair matches the local scan and recommendations that reference the compiled action allowlist.
7. Check compatibility and current state before an action can be selected.
8. Build local objective-aware conflict patterns and expose AI-recommended, conflict-related, and all supported actions with risk-aware approval.
9. Require and verify a new System Restore point plus affected Registry exports.
10. Journal every attempt before execution, verify results, and roll back automatically on failure.
11. Detect interrupted operations and expose manual recovery at startup.
12. Show immediate before/after telemetry without presenting it as a benchmark.
13. Build and test the .NET agent, React UI, Rust shell, and contrast tokens; publish an NSIS installer and checksum through GitHub Actions.

## Alpha Completion Criteria

- Build and tests pass on Windows.
- No API key appears in versioned files, profiles, or logs.
- Unknown LLM `ActionId` values are rejected.
- Unsupported and already-configured actions cannot be selected.
- No system change is attempted when the required backup fails.
- Interrupted operations retain enough state for recovery.
- Rollback verifies that the restored value matches the saved snapshot.
- The pipeline produces a self-contained Windows NSIS installer and SHA-256 checksum.

## External Requirements

Code signing and automatic updates require an organization-owned certificate and trusted update channel. End-to-end restore testing requires disposable Windows 10 and Windows 11 virtual machines and is tracked in the [roadmap](../ROADMAP.md).
