# Implementation Plan

## Alpha Architecture

- Windows 10/11 x64, .NET 8, and WPF.
- One administrative desktop application with no backend or database.
- BYOK providers: OpenRouter, OpenAI, and Anthropic.
- Windows/.NET profiling, local JSON settings, and DPAPI-protected secrets.
- Closed local catalog: the LLM diagnoses and recommends but never generates executable changes.
- Verified restore point, Registry exports, per-action journal, and reverse-order rollback.

## Implemented Flow

1. Initialize the solution, repository, CI, and documentation.
2. Define system profiles, diagnoses, actions, presets, telemetry, and operation manifests.
3. Protect API keys and dynamically discover provider models.
4. Collect hardware, Windows, gaming, network, process, startup, and service data locally.
5. Sanitize and preview the exact profile sent to the selected provider.
6. Accept only structured recommendations that reference the compiled action allowlist.
7. Check compatibility and current state before an action can be selected.
8. Filter AI recommendations by preset and risk.
9. Require and verify a new System Restore point plus affected Registry exports.
10. Journal every attempt before execution, verify results, and roll back automatically on failure.
11. Detect interrupted operations and expose manual recovery at startup.
12. Show immediate before/after telemetry without presenting it as a benchmark.
13. Build, test, publish, checksum, and retain a portable Windows artifact through GitHub Actions.

## Alpha Completion Criteria

- Build and tests pass on Windows.
- No API key appears in versioned files, profiles, or logs.
- Unknown LLM `ActionId` values are rejected.
- Unsupported and already-configured actions cannot be selected.
- No system change is attempted when the required backup fails.
- Interrupted operations retain enough state for recovery.
- Rollback verifies that the restored value matches the saved snapshot.
- The pipeline produces a self-contained `win-x64` artifact and SHA-256 checksum.

## External Requirements

Code signing requires an organization-owned certificate. Installer and updater work must wait for a signed distribution channel. End-to-end restore testing requires disposable Windows 10 and Windows 11 virtual machines and is tracked in the [roadmap](../ROADMAP.md).
