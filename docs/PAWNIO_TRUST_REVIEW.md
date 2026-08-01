# PawnIO / LibreHardwareMonitor trust review

Decision: **do not integrate or install PawnIO in NeuroTune 0.5.0-alpha.2**.

The review used pinned upstream sources and release artifacts without installing or executing the driver:

| Component | Pinned revision / release | License | Reviewed artifact SHA-256 |
|---|---|---|---|
| PawnIO | 2.2.0, 5cdf470831fdfff3f7f1d06363ca6b230f3bf35a | GPL-2.0-or-later with the repository exception | 1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032 |
| PawnIO.Setup | 2.2.0, c14a8567b881a92e54d26fe00d78491f67928a58 | Repository metadata only; installer source was not present | same installer above |
| PawnIO.Modules | 0.2.10, c683032770575d7705d1149f9d7fa7fd381766fc | LGPL-2.1 | 971C7C974C538B62AC020E0442FA99D0423417BFB496DFE9A4A43CCC0ABC0E63 |
| LibreHardwareMonitor | 0.9.6, 3d331e3370efb858411f19511373eff65a218701 | MPL-2.0 plus third-party notices | .NET 10: 29739C4959B01B348FDDAD87664066634BCFD4F46E9BF41E4E916C318BCFDB99; standard archive: 086D9F1B5A99E643EDC2CFAAAC16051685B551E4C5AC0B32A57C58C0E529C001 |

The official PawnIO installer had a valid Authenticode signature from namazso.eu / namazso, certificate thumbprint F380DCC9F706E2756A5047B832FFE719E1BC35F5, with a Microsoft timestamp. Digest values matched the upstream release metadata.

## Boundary findings

- The INF grants device access to SYSTEM and Built-in Administrators. The IOCTLs use FILE_ANY_ACCESS, so authorization relies on that device ACL rather than an in-driver token check.
- The strict build verifies Pawn module signatures. PAWNIO_UNRESTRICTED intentionally disables this check, is off by default, and produces a distinct PawnIO_unsigned output.
- A signed module receives physical and virtual memory access, port and firmware I/O, MSR/control-register operations, kernel symbol lookup, indirect invocation, and callback primitives. This is not a read-only capability boundary.
- Plausible signed-module parser and callback-lifetime defects need an isolated parser harness or disposable kernel VM to close. They are not reachable by an ordinary unprivileged process under the reviewed ACL/signature assumptions.
- LibreHardwareMonitor 0.9.6 embeds PawnIO setup and module resources and can offer to install them. Its embedded provenance README is stale relative to its release notes. NeuroTune must not reuse this automatic-install path.
- PawnIO.Setup did not expose enough installer source to audit exact service, file, and uninstall side effects.
- HVCI/Memory Integrity compatibility and clean uninstall remain runtime questions.

## Implemented NeuroTune boundary

NeuroTune.Telemetry.exe is a separate, no-network process with JSON stdin/stdout, a single fixed capabilities command, a two-second caller timeout, and no arbitrary paths or commands. It runs only after separate UI consent. It currently returns driverNotApproved and contains no driver installation, loading, or IOCTL code.

The licenses above do not enter the distributed product because NeuroTune does not copy, link, package, or execute PawnIO or LibreHardwareMonitor.

The complete static scan report is in the Codex Security scan workspace for pinned commit 5cdf470831fdfff3f7f1d06363ca6b230f3bf35a.
