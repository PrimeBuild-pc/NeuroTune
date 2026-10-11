# Vulnerability and compromise review

The **Vulnerability & compromise review** preset asks the selected AI to look for exposure and indicators of
compromise on this PC. It is an investigation focus, not a scanner: it starts no tweak, removal, process kill,
download or antivirus scan, and its findings are hypotheses with stated limits, never a "clean" or "infected" verdict.

## What it reads

Everything is a fixed, read-only reader. Pi receives no shell or file tool; it can only request these tools through
NeuroTune, which validates every request and result. The preset reuses the existing audit-only consent for Windows
privacy/security reads and adds no new consent surface.

| Tool | Returns | Deliberately omitted |
|---|---|---|
| `network-exposure` | Listening TCP/UDP sockets: port, scope (loopback / all interfaces / one interface), owning process name | Paths, remote peers, traffic, firewall evaluation |
| `service-integrity` | Services whose image path is unquoted-with-spaces or in a user-writable location | The paths themselves, signatures |
| `patch-level` | OS build, reported hotfix count and newest date, last Windows Update install time | A vulnerability scan; the QFE inventory is incomplete |
| `account-summary` | Local account counts, built-in Administrator/Guest enabled flags, Administrators-group member count | Account names, domain accounts |
| `wmi-persistence` | Counts of permanent WMI event filters, command-line/script consumers and bindings | Commands and scripts |
| `hosts-and-proxy` | Hosts-file entry counts (total / redirecting) and user proxy/PAC flags | Domains and proxy addresses |

The initial audit evidence also gains 12 hardening snapshots (`security.hardening.*`): RDP and NLA, SMBv1, LSA
protection, anonymous restriction, LAN Manager level, WDigest, UAC, PowerShell script-block logging, AutoRun and
LLMNR. Missing values are reported as "not configured", not as disabled.

The preset also turns on the mandatory audit coverage report, so protections, detections and persistence are covered
or explicitly marked partial/unavailable.

## What it does not claim

Legitimate software routinely listens on ports, runs services from `ProgramData`, redirects hosts entries or uses WMI
subscriptions. A flag asks "is this expected?", it does not prove malware. No hashes, signatures, memory or network
traffic are inspected. Absence of findings is not a clean PC; for suspected compromise use Windows Security's offline
scan or reinstall guidance from a trusted device.

## External tools considered

| Source | Decision | Reason |
|---|---|---|
| `personal-security-checklist`, `privacy.sexy` documentation, MITRE ATT&CK persistence techniques | Used as design references only | They informed which settings and persistence points the readers cover. No script from them is executed |
| `ffuf`, `hackingtool`, `xalgorix`, `strix`, `OpenHands`, `browser-use`, `maxun`, `crawl4AI`, `reverse-skill` | Not integrated | Offensive, crawling or arbitrary-execution tools. They need targets and a shell, which contradicts the no-shell, read-only, reversible boundary |
| `coolify`, `supabase`, `dify`, `langflow`, `open-webui`, `stirling-pdf`, `public-apis` | Not applicable | Self-hosting or application platforms, unrelated to auditing this host |
| `user-scanner`, `awesome-privacy`, `justdeleteme`, data-broker opt-out list, `ungoogled-chromium`, `pi-hole` | Out of scope | Online identity and footprint or browser/network choices, not host compromise. Could become a separate, opt-in feature |
| Triage (sandbox) | Not integrated | Uploading files to a third party contradicts the privacy boundary. A hash-only lookup could be a future opt-in |
| Sergei Strelec WinPE, Hiren's BootCD, OpenBoardView | Not applicable | Offline repair media for the user, not something an in-app agent can drive |

New readers must keep the same rules: fixed code, bounded output, no paths/names/addresses, no execution, and a
`limitation` string that says what the observation cannot prove.
