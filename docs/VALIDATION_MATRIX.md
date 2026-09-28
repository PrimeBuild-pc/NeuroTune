# Alpha validation matrix

## Automated host and VM coverage

| Area | Supported Windows 11 VM | Historical Windows 10 result (unsupported) | Physical hardware |
|---|---|---|---|
| Gen 2, UEFI, Secure Boot, vTPM | required | required | n/a |
| Memory Integrity | enabled and recorded | recorded when available | required before driver experiments |
| v0.7.0-alpha.1 NSIS per-machine install, agent version, Defender, uninstall | passed on build 26200 | not repeated; v0.6 historical pass only | installer SHA-256 `8D33E53E0CDEBA56C09303C8E082F1BB68DDEDD25AAD06D96F80D1F829B79465` |
| v0.7.0-alpha.1 run-aware apply/verify/rollback and interrupted Apply/Rollback recovery | passed | not repeated; v0.6 historical pass only | explicit `OptimizationRun`/`runId`; deterministic VM-only BaselineReady fixture; not a 3+3 quality-gate test |
| v0.6.0-alpha.1 portable ZIP layout, agent response, and UI launch | passed on physical build 26200 | not repeated | complete ZIP contains UI, Agent, Telemetry, license, README, and release notes |
| Scan cancellation / no orphan process tree | Rust fake-agent test plus VM checklist | same | optional |
| Writer integrity: Inspect, Capture, Apply, Verify, exact Restore | 16 targeted actions passed, including page file, core parking, and per-app GPU high/default | historical legacy pass | every-current-action sweep and per-app GPU power-saving remain open before stable release |
| Crash while Applying and Rolling back | passed with deterministic VM-only delay hook, kill, history recovery, rollback | passed with the same harness | not required |
| 100%, 150%, 200% scaling | 100% passed; 150%/200% blocked by Enhanced Session | manual visual pass | manual |
| Keyboard-only, focus visibility, reduced motion, forced colors | manual plus CSS/semantic checks | manual plus CSS/semantic checks | manual |
| SPD/XMP/EXPO, motherboard sensors, temperatures, real HAGS | unavailable | unavailable | required |
| PawnIO install/HVCI/uninstall | deliberately excluded | deliberately excluded | disposable dedicated PC only after approval |
| v0.7.0-alpha.1 ETW watchdog, analysis, quality, cleanup | 3/3 valid on build 26200; 0 lost events; no ETL or WPR orphan | not required | redacted physical DirectX harness available; AMD/NVIDIA runs pending |
| Shareable hardware collector | Windows PowerShell 5.1 self-test passed; no-admin/offline/read-only contract | not required | Fleet intake now includes RX 7900 XT (8 CPU sets/11 devices), RTX 5060 Ti (16/9), RTX 5080 (16/14), and RTX 3070 on Intel (12/7); inventory only, physical performance/rollback validation pending |
| M11 system-wide latency and M12 firmware inspection | separate VM coverage pending | not required | 2026-09-07: 3/3 final desktop captures on Ryzen 5800X3D / RX 6950 XT, 16 processors, zero lost events and raw ETL deleted; MSI BIOS interface detected without usable setup values; [report](LOCAL_LATENCY_VALIDATION.md) |

scripts/vm-provision.ps1 creates clean Hyper-V guests and DPAPI-protected credential files. scripts/vm-validation.ps1 normally restores the clean checkpoint, copies the installer with PowerShell Direct, runs the automated matrix, and writes a redacted JSON report. `-SkipCheckpointRestore` is available for a supervised, already-running existing guest; the harness then restores every state it seeds instead of changing the checkpoint chain. Neither script prints or commits guest passwords.

Windows 11 25H2 is used because it is the current ISO available in the lab. As
of v0.7.0-alpha.1, NeuroTune supports only Microsoft-supported Windows 11 x64
builds. Windows 10 entries below are preserved as historical evidence and do
not represent a current support or release gate.

The former disposable `NeuroTune-W10` disk/checkpoint chain became unbootable
and was replaced during v0.6 validation with a clean Windows 10 Pro 22H2
installation. Those results remain reproducibility history only; the VM and
its checkpoint are no longer required or maintained.

On 2026-08-31 the recovered in-place `NeuroTune-W11` guest on `D:` passed the
v0.7 run-aware validation without copying the VM or modifying its checkpoint
chain. `docs/validation/v0.7.0-alpha.1-windows11-writer-integrity.json`
records 16 passed targeted
actions and no failures, including Windows-managed page-file sizes, AC core
parking, and dynamic per-app GPU high/default preferences. Each case passed
Inspect, Apply, Verify, raw read-back, restore-point lookup, exact rollback,
and terminal manifest checks.
`docs/validation/v0.7.0-alpha.1-windows11-recovery.json` records successful
install/version, run-aware apply/rollback, interrupted Apply and Rollback
recovery, Defender, PawnIO absence, HVCI configuration, and uninstall.
The writer harness asserts that its deterministic VM-only journal fixture
reaches `BaselineReady`; it does not claim to validate a real ETW Baseline or
the repeated 3+3 Keep gate, which remain separate tests.

On 2026-08-02 the M2 capability probe repeated the exact-state round trip for
all 25 registered actions, including the new Balanced plan, BCD timer/resource
repair, and complementary
default/on/off states for Game Mode, HAGS, Game DVR, app capture, and visual
effects. Windows 10 Pro 22H2 build 19045 and Windows 11 build 26200 both passed
all 25 cases at that time. That historical result predates the current
Windows-managed page-file, core-parking, and dynamic per-app GPU writers and does
not replace the current v0.7 evidence above. The Windows 11 VM was temporarily started with 4 GB because the
host disk could not allocate its 8 GB runtime-state file; the runner then shut
it down and verified that its original 8 GB startup setting was restored.

## Manual accessibility acceptance

Recorded Windows 11 result on 2026-08-02: the 100% scale pass completed without overlap or clipping. Windows blocked 150% and 200% changes because Hyper-V Connect was using an Enhanced/remote session, so those two results are not claimed. High Contrast and Reduced Motion remain optional manual follow-ups.
The remaining supervised checks are intentionally deferred until the interface
is closer to feature-complete; automated semantic and keyboard regressions stay
in the regular test gate.

At 100%, 150%, and 200% display scale:

1. Resize to the minimum 720×600 window and confirm no control is unreachable.
2. Traverse every page with Tab/Shift+Tab and activate controls with Space/Enter.
3. Confirm the current navigation page is announced and focus is always visible.
4. Enable Windows Always show scrollbars, Reduced Motion, and a High Contrast theme.
5. Confirm status is never communicated by color alone and notices are announced.
6. Print the review report and confirm interactive controls are omitted.

VM automation cannot validate real sensor correctness, GPU scheduling on a passed-through GPU, or motherboard firmware state.

## AMD-NVIDIA fleet intake

`tools/hardware-collector/Collect-NeuroTune-HardwareReport.cmd` creates a dated
JSON beside itself. It reads CIM, the 64-bit Registry view, and the Windows
CPU-set API. It does not request elevation, access the network, start ETW, or
change a setting, service, driver, device, or Registry value.

Each report contains Windows/build/security status, non-serial platform model,
CPU topology, GPU hardware/driver identity, and relevant display/network/audio/
storage/USB-controller interrupt-policy snapshots. Raw PnP identifiers are
replaced by random report-local device keys; user/computer names, serials,
MAC/IP addresses, full paths, and Registry paths are excluded and checked
before write.
Existing interrupt masks are reduced to presence, Registry type, and byte
length; the mask value itself is not exported.

Fleet reports determine which physical fixtures deserve testing. They do not
validate a performance gain or authorize automatic affinity changes. A device
writer still requires exact capture/verify/restore, restart behavior, and
repeatable 3+3 Baseline/Candidate evidence for the specific driver family.

### Inventory received through 2026-09-15

Folders are classified by GPU vendor, not CPU vendor. The reports received on
2026-09-07 contain NVIDIA GPUs with AMD CPUs; the 2026-09-15 NVIDIA report uses
an Intel CPU. The existing AMD report is retained under `amd/`. All four use
schema 1 / collector 0.1.0 and report Windows 11 Pro build 26200, a single
processor group (0), and `hypervisorPresent=false`.
These are reported snapshots, not independent certification of a physical host.

| Report | GPU / Windows driver version | CPU / observed topology | Board / BIOS | GPU interrupt policy | Intake status |
|---|---|---|---|---|---|
| [AMD, 2026-09-03](validation/hardware-fleet/amd/NeuroTune-HardwareReport-20260903-182946.json) | RX 7900 XT / `32.0.31041.1004` | Ryzen 7 7800X3D; 8 physical / 8 logical | ASUS TUF GAMING B850-PLUS WIFI, Rev 1.xx / `1681` | `windowsDefault`; mask and DevicePolicy absent | 11 devices: 2 configured, 9 default; no collector warnings |
| [NVIDIA, 2026-09-06 21:17:59](validation/hardware-fleet/nvidia/NeuroTune-HardwareReport-20260906-211759.json) | RTX 5060 Ti / `32.0.16.1664` | Ryzen 7 9700X; 8 physical / 16 logical | SAPPHIRE NITRO+ B850M WIFI, AM50L301R27 / `AM50L301R27` | `windowsDefault`; mask and DevicePolicy absent | 9 devices: 4 configured, 5 default; Secure Boot unavailable |
| [NVIDIA, 2026-09-06 22:03:39](validation/hardware-fleet/nvidia/NeuroTune-HardwareReport-20260906-220339.json) | RTX 5080 / `32.0.16.1664` | Ryzen 7 9800X3D; 8 physical / 16 logical | ASUS TUF GAMING X870E-PLUS WIFI7, Rev 1.xx / `2402` | `configured`; Binary mask, 1 byte, value redacted; DWord DevicePolicy `4` | 14 devices: 10 configured, 4 default; Secure Boot unavailable |
| [NVIDIA, 2026-09-15](validation/hardware-fleet/nvidia/NeuroTune-HardwareReport-20260915-134216.json) | RTX 3070 / `32.0.16.1692` | Intel Core i5-10600K; 6 physical / 12 logical | MSI MPG Z490 GAMING PLUS (MS-7C75), v2.0 / `A.F1` | `windowsDefault`; mask and DevicePolicy absent | 7 devices: 2 configured, 5 default; Secure Boot unavailable |

Exact GPU hardware IDs, respectively:

- RX 7900 XT: `PCI\VEN_1002&DEV_744C&SUBSYS_05ED1043&REV_CC`.
- RTX 5060 Ti: `PCI\VEN_10DE&DEV_2D04&SUBSYS_F3301569&REV_A1`.
- RTX 5080: `PCI\VEN_10DE&DEV_2C02&SUBSYS_53151462&REV_A1`.
- RTX 3070: `PCI\VEN_10DE&DEV_2484&SUBSYS_146B10DE&REV_A1`.

### What these reports change

- NVIDIA inventory is no longer missing. The RTX 5060 Ti and RTX 5080 exercise
  different initial states despite the same reported driver version: absent
  GPU policy and an existing one-byte Binary mask with DevicePolicy 4. Test
  exact restore of absence and preservation of existing type/length/value
  locally; the redacted mask cannot identify the selected core or restore that
  machine. The RTX 3070 adds a second NVIDIA driver version with default policy.
- The RTX 5080 snapshot contains configured policies on 10 of 14 devices,
  including GPU, audio, network, storage, and USB. Treat its measured starting
  state as already configured, not factory defaults or proof those settings
  help. Isolate one change per experiment and restore the captured state.
- The AMD case adds 8 observed logical processors; the two 2026-09-06 NVIDIA
  cases add 16 across 8 physical cores. Exercise topology-derived candidate
  selection with and without observed SMT siblings. The report alone does not
  establish why the AMD system exposes 8 logical processors or prove a BIOS
  SMT value.
- The RTX 3070 case adds the first Intel CPU and Z490 fixture: 12 logical
  processors map to 6 physical cores with two CPU sets per core. It broadens
  homogeneous Intel/SMT coverage but does not cover hybrid P/E cores. Its GPU
  remains on the Windows default policy while SATA AHCI and NVMe each report
  DevicePolicy 5; preserve those starting states locally and do not generalize
  them into recommendations for other storage controllers.
- All three NVIDIA reports have `secureBoot=null` and the explicit unavailable warning.
  Preserve Unknown; do not infer disabled, a defect, or a recommended change.
  BIOS manufacturer/version identifies firmware, not its current setup values.
- All four GPU snapshots contain an `msiSupported` DWord value of 1. That is
  Registry evidence only; it does not prove effective interrupt routing under
  load, latency, or justify forcing MSI or copying affinities between machines.
- Fleet observations stay in this validation matrix. Do not promote observed
  clocks, BIOS versions, or driver dates into official component baselines or
  a latest-version catalog without exact vendor specifications and provenance.

### Remaining physical acceptance

- [ ] Collect three valid matching DirectX Baselines per listed GPU/driver with
  `scripts/physical-gpu-measurement.ps1`; record lost events, workload, duration,
  thermal/configuration fingerprints, and the read-only candidate preview.
- [ ] Implement and validate the supervised M9 writer, then collect three
  Candidate runs, actual routing verification, restart/resume and exact restore
  evidence per case. An existing configured mask does not satisfy this gate.
- [ ] Add physical coverage for other driver versions/families, hybrid CPUs,
  multiple processor groups, multi-GPU, VBS/HVCI-enabled configurations,
  and unavailable/unsupported policies before making those support claims.
- [ ] Validate M11's complete per-core/system metrics, driver/service mapping,
  hard pagefaults and interrupt-to-process probe; these inventory files contain
  no ETW sessions, busy time, DPC spike durations, or benchmark results.
- [ ] Validate M12 BIOS guidance against exact board/firmware manuals and test
  optional OEM access separately; none of these reports proves settings access.

### Intake integrity

Reports are preserved byte-for-byte. JSON parsing, inventory/topology/device
links, privacy exclusions and empty exported affinity-mask values were checked
for the original intake on 2026-09-07 and the RTX 3070 intake on 2026-09-26.
SHA-256 identifies the supplied bytes, not their authenticity:

| Report filename | SHA-256 |
|---|---|
| `NeuroTune-HardwareReport-20260903-182946.json` | `2F76E39618B2A448C7C1757D18F17A420C8CAC9D10B0C8981B4AF6D0F92F2C16` |
| `NeuroTune-HardwareReport-20260906-211759.json` | `280EE102E5CAB28C70DCF127D8BF5FC384CDED16F05FD5788B124212698C1768` |
| `NeuroTune-HardwareReport-20260906-220339.json` | `14AAA74A6E6198878E998C370414DF4AFCB8C78D8BD7676CBE7A5B13ABBB3554` |
| `NeuroTune-HardwareReport-20260915-134216.json` | `0BB7D0190281CDFEB183919D5E97CD38E83D05627EA57A8DD88C0AD15B765803` |
