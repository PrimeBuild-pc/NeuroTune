# Security

## Reporting a Vulnerability

Do not open a public issue containing API keys, personal data, or immediately exploitable details. Use the repository's private **Security advisories** feature instead.

Include the affected version, reproduction steps, expected impact, and a contact method. Do not attach a complete Windows profile dump.

## Security Model

- Every LLM response is treated as untrusted input.
- Only action identifiers compiled into the local allowlist, or typed parametric primitives, are accepted. A parametric action's ID is its kind plus a SHA-256 prefix of its normalized parameters; the journal (schema v4, v3 still readable) stores kind and parameters, and recovery rebuilds the action from them and rejects any ID mismatch.
- Device keys under `Enum` accept only a present PCI device's `Interrupt Management\MessageSignaledInterruptProperties` (`MSISupported`, `MessageNumberLimit`) and `Interrupt Management\Affinity Policy` (`DevicePolicy`, `AssignmentSetOverride`, `DevicePriority`) values; everything else under `Enum` is refused.
- Parametric primitives refuse protected targets before capture: `SAM`, `SECURITY`, `BCD00000000`, `Lsa`, `CI`, `SafeBoot`, Defender/firewall/UAC policy keys, Winlogon, IFEO (except `PerfOptions` through `ifeo.perf`), `Classes`, autostart `Run*` keys, environment keys, service `ImagePath`/`ServiceDll`, services Windows cannot boot or sign in without plus WMI, Defender and firewall services, Defender scheduled tasks, BCD elements outside a short list, and configuration files outside the user profile/Steam folder or behind links. HKLM Registry writes, disabled services, BCD changes and Defender exclusions are High risk and need separate confirmation. VBS/Memory Integrity (`DeviceGuard` keys, `hypervisorlaunchtype`) can be turned off this way by user choice; it weakens Windows protection and may break anti-cheats, WSL2 and Hyper-V. Startup apps are disabled through `StartupApproved`, as Task Manager does; raw `Run` key writes stay forbidden. The forbidden list covers known security, boot and autostart paths, not every possible hijack path; the user's confirmation remains the last gate.
- Diagnosis findings are rejected unless their evidence identifier and value exactly match the local scan.
- Microsoft Defender: `defender.exclusion` is the only Defender write. An exclusion stays until the user rolls it back, like any journaled change. NeuroTune never turns protection off; while real-time protection is off a persistent banner links to Windows Security.
- Model-generated scripts and write commands are never executed. The only model-written code that runs is a read command through `powershell-read`: PowerShell's own parser describes the command, NeuroTune refuses any AST node, command or argument outside its read-only allowlist (no method calls, assignments, file redirection, `$( )`, `&`/`.` invocation, remote parameters or UNC paths), then runs it in Constrained Language mode as a non-admin process (admin only for `bcdedit`, `Get-MpPreference`, `Get-StorageReliabilityCounter`), with a 30 s limit and bounded, redacted output. Model-proposed paths, Registry locations and values only reach Windows as validated parameters of a typed primitive.
- Compatibility and current state are checked locally before execution.
- The engine stops before making changes if it cannot verify a new restore point or export required Registry keys.
- Every action attempt is journaled before execution and verified after application.
- Automatic and manual rollback restore actions in reverse order and verify the saved state.
- Privileged operation/run journals live under `%ProgramData%\NeuroTune-journals\<user SID>`, with Administrators/SYSTEM ownership and access only. Existing unsafe owners/ACLs and reparse paths are rejected, not silently repaired. A same-user non-elevated process is not trusted to provide recovery state.
- Rollback validates the complete manifest, action IDs, exact snapshot fields/names/types/values and target identity before any restore. GPU target metadata and custom-plan IDs survive loss of the disposable cache or staged `.pow`; core-parking recovery targets the saved scheme, not `SCHEME_CURRENT`.
- Journals use a flushed temporary file and atomic replacement. This is not a power-loss certification; interruption and storage-failure acceptance remain VM gates.
- Opposing states for one setting are rejected during approval and engine preflight. Trusted Windows utilities use absolute system paths, concurrent bounded pipe drains and deadlines.
- API keys and OpenRouter-issued OAuth keys are protected with DPAPI `CurrentUser` and redacted from local logs.
- OpenRouter browser authorization uses PKCE, a random CSRF state, a loopback callback, and a two-minute timeout.
- Built-in provider endpoints cannot be edited. Custom remote providers require HTTPS, HTTP is loopback-only, and authenticated HTTP redirects are disabled.
- The exact sanitized evidence bundle, privacy classes, UTF-8 size, and enforced single-pass limit are visible before diagnosis.
- Scan cancellation can target only the matching NeuroTune.Agent child and terminates its probe process tree.
- Factory baselines are versioned local data, require exact non-unique component identifiers, and never use serial numbers or nearest-model guesses.
- Firmware heuristics and the telemetry support matrix are read-only and labelled as uncertain; no kernel telemetry driver is downloaded, loaded, or installed silently.

## Known Limitations

NeuroTune currently runs with administrator privileges because it changes system settings. A compromised Windows account or tampered executable can bypass application-level controls. Alpha builds are unsigned and must be checked against the SHA-256 value produced by the build pipeline.

Legacy `%LocalAppData%\NeuroTune\operations` and `runs` journals are not automatically imported or trusted. Before upgrading, finish/review recovery with the previous build and archive these directories only after preserving independent backups and resolving old writes. The new build blocks journal operations while legacy entries remain, including manifests that claim a completed rollback: those unprotected fields are not proof of current Windows state. Do not copy legacy JSON into the protected store or delete pending recovery data to bypass this gate.

Complete diagnosis checks journal access before collection, WPR or AI. A journal access failure does not discard the configured provider; the interface displays the native error with all blocking legacy paths for the Windows account running the agent. After external manual review and archiving, use **Refresh** to recheck. Moving only `operations` does not unblock a nonempty `runs`; copying a backup without moving the reviewed originals or reinstalling does not clear the gate. Empty ordinary legacy directories are allowed. This behavior is independent of virtualization.

PresentMon imports require process attribution but remain user-supplied data with unverified session provenance. Missing metric coverage or hard-fault collection cannot justify a favorable Keep recommendation.

End-to-end restore behavior still requires validation across the supported Windows virtual-machine matrix. Use alpha builds only on disposable systems or PCs with an independent backup.
