# Security

## Reporting a Vulnerability

Do not open a public issue containing API keys, personal data, or immediately exploitable details. Use the repository's private **Security advisories** feature instead.

Include the affected version, reproduction steps, expected impact, and a contact method. Do not attach a complete Windows profile dump.

## Security Model

- Every LLM response is treated as untrusted input.
- Only action identifiers compiled into the local allowlist are accepted.
- Model-generated commands, scripts, paths, Registry locations, and Registry values are never executed.
- Compatibility and current state are checked locally before execution.
- The engine stops before making changes if it cannot verify a new restore point or export required Registry keys.
- Every action attempt is journaled before execution and verified after application.
- Automatic and manual rollback restore actions in reverse order and verify the saved state.
- API keys and OpenRouter-issued OAuth keys are protected with DPAPI `CurrentUser` and redacted from local logs.
- OpenRouter browser authorization uses PKCE, a random CSRF state, a loopback callback, and a two-minute timeout.
- Built-in provider endpoints cannot be edited. Custom remote providers require HTTPS, HTTP is loopback-only, and authenticated HTTP redirects are disabled.
- The exact sanitized profile sent to the provider is visible in the application.

## Known Limitations

NeuroTune currently runs with administrator privileges because it changes system settings. A compromised Windows account or tampered executable can bypass application-level controls. Alpha builds are unsigned and must be checked against the SHA-256 value produced by the build pipeline.

End-to-end restore behavior still requires validation across the supported Windows virtual-machine matrix. Use alpha builds only on disposable systems or PCs with an independent backup.
