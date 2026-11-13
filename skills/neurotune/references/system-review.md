# Full system analysis

Run this whole sequence after the user's consent to the read-only analysis (SKILL.md step 1). Do not ask again for each area; ask only when an area needs something new (administrator rights, a capture, a config file's contents). Everything here reads; nothing changes the system.

## 1. Collect

Commands follow `node "<skill-directory>/scripts/cli.mjs"`. Write outputs to a private folder the user agrees to, with new `.json` names.

| Step | Command | Gives |
|---|---|---|
| System scan | `scan-system --out scan.json` then `system-findings --input scan.json` | Devices (present, disabled, with errors, not present), driver store, MSI/affinity per PCI device, USB input chains, services, scheduled tasks, startup, desktop and Store apps, Windows Update, privacy, gaming settings, leftover risky tweaks, network adapters and their power settings, audio endpoints, installed games |
| Hardware facts | `collect --out inventory.json` then `evidence` | Citable CPU, GPU and driver, memory, Windows build, VBS ([investigation.md](investigation.md)) |
| Driver latency | `measure-latency --seconds 60 --out latency.json` (administrator) or `latencymon --input report.txt --out latency.json`, then `evidence` | Highest and total DPC/ISR time per driver and per CPU (see [measurements.md](measurements.md#driver-latency-dpc-and-isr)) |
| Frame times | `frames` / `compare` on a PresentMon capture, if the user has one | FPS, frame-time tails, stutters ([measurements.md](measurements.md)) |
| Game configs | `game-configs --game <id>` for each id in `games.supportedConfigCatalogue` | Where the config lives and which keys matter ([game-configs.md](game-configs.md)) |

`system-findings` is a compact summary with account names and SIDs removed. `scan.json` holds more (device instance IDs that can contain serial numbers, service paths and startup commands that can contain the user's name): do not read the whole file into the conversation; extract only the entry a specific proposal needs (one device's instance ID, one service's path). Sections in `unavailableSections` were not collected, usually because they need administrator rights: say so, and offer to rerun elevated if they matter. For a supplied app/service snapshot instead of a live scan, use [apps-services.md](apps-services.md).

Read live through the harness's command tool what the scan does not cover and the case needs, for example `Get-Counter '\Processor(*)\% DPC Time'` while the user plays, `powercfg /q` for a specific plan setting, the GPU control panel's settings, or a vendor's current driver version on its website. Cite those as "observed via `<command>` on <date>".

## 2. Interpret, area by area

- **Hardware and firmware.** Memory: one module often means single channel; a configured speed below the kit's XMP/EXPO rating means the profile is off (a BIOS setting the user changes, not you). BIOS older than about two years: suggest checking the board vendor's page, never flash it yourself. Storage: game drive type and free space.
- **Devices.** Explain each device with a problem code in plain words. Not-present devices (old USB sticks, previous GPUs, phantom monitors) are clutter, rarely a performance issue: report them, do not remove them (no backup is possible and Windows reinstalls them on reconnect). List devices nobody uses (unused controllers, virtual adapters, extra audio endpoints) as candidates to disable, never core platform devices (see Critical below).
- **Drivers.** Old unused packages from `drivers.oldUnusedPackages` can be removed after export. For the GPU, chipset, network and audio drivers, compare the installed version and date with the vendor's current release (look it up); report outdated ones with the official download page, and let the user install vendor packages. Duplicate drivers with the same version are not an error.
- **Driver latency.** Use the thresholds in [measurements.md](measurements.md#driver-latency-dpc-and-isr). Name the driver, the device behind it, and the CPU that serves its interrupts.
- **Interrupts, MSI and affinity.** An MSI-capable device that is still line-based (`interrupts.msiCapableButOff`) and shows up in the latency trace is a candidate for MSI mode. Existing affinity or priority overrides came from someone: ask, and check them against the trace. Moving a busy device's interrupts off CPU 0 or off the cores the game uses is a hypothesis to test with a second trace, not a rule.
- **USB and input.** From `inputChains`: a mouse behind hubs, or sharing its controller with a webcam, audio interface, VR headset or storage, is worth moving. Say which device to move and to what kind of port (a rear port on a different controller, not a front-panel hub), then rescan to confirm the chain changed. You cannot see the physical port labels; describe them by controller.
- **Services, scheduled tasks, startup.** Separate Windows components from third-party ones. Third-party updaters, telemetry, RGB/peripheral suites and launchers are the usual candidates; ask which programs the user uses. Never disable what Windows needs to boot, update, secure itself or run the user's hardware. `services.thirdPartyActive` is selected by install path, so check each name: vendor services installed under `C:\Windows` are not in it, and `services.microsoftActive` (Defender, Edge, Office, Gaming Services) is not a cleanup list. Anti-cheat services and drivers (Vanguard `vgc`/`vgk`, EasyAntiCheat, BattlEye, FACEIT and similar) stay as they are for any game the user plays.
- **Apps.** List installed programs and Store apps the user may not remember. Ask before proposing removals; an unused-looking app is a question, not a removal. Never propose removing shared runtimes and platform pieces that look unused but that games and launchers depend on: Visual C++ redistributables, .NET, DirectX, WebView2, the Microsoft Store and App Installer, Xbox Identity Provider and Gaming Services, GPU and chipset driver packages.
- **Windows Update.** Security updates must be installed and current: a disabled update service, paused updates or no security update in about 45 days is a finding to fix, never a tweak to keep. `security_update_date_unknown` means the date could not be read, not that updates are missing: ask the user to check Windows Update. Feature updates are optional, unless one ships a scheduler or driver improvement for this hardware.
- **Privacy and background activity.** Advertising ID, tailored experiences, suggestions, silent app installs, background apps and optional diagnostic data can be turned off without losing features the user relies on; ask about the ones that affect features (background apps for messaging, for example).
- **Gaming settings.** Power plan, Game Mode, Game DVR and background recording, hardware-accelerated GPU scheduling, mouse acceleration, visual effects. Recommend from the hardware and the user's priority, not by default.
- **Risky leftovers.** `riskyTweaks` lists values left by "optimizer" scripts (forced platform clock, changed input queues, disabled MPO, memory-management changes...). Restoring Windows defaults is usually the improvement.
- **Network.** For the adapter in use: power saving, Energy Efficient Ethernet and wake settings are usually safe to turn off on a desktop; interrupt moderation trades CPU load for latency and needs a measurement. Wi-Fi power saving matters on laptops and desktop Wi-Fi alike.
- **Audio.** Most users need one output and one input. Extra active endpoints (monitor speakers, virtual devices, unused headsets) can confuse games and voice apps; ask which to keep and offer to disable the rest.
- **Security features.** VBS/Memory Integrity costs some performance on many CPUs. Never put it in a preset. Mention it separately only when the user asks for maximum performance, with the trade-off in plain words. Defender, firewall, UAC, Secure Boot and security updates are never proposed for removal.

## 3. Classify every candidate

- **Safe:** well-documented, non-essential, and the user confirmed they do not use it (or it is a privacy or background default with no user-visible loss).
- **Moderate:** may matter to some users or setups. Ask in plain words before including it.
- **Critical:** tied to boot, storage, display, input, networking in use, security, Windows servicing, anti-cheat or a shared runtime, or its role here is unclear. Do not propose it. If the user insists, explain the concrete risk and require a separate confirmation for that exact item.

Use your knowledge plus a web lookup when unsure about a specific service, task, driver or device. Never invent a role for something you do not recognize: say you do not know and keep it out of the plan.

## 4. Coverage

Before the audit, every area must be **covered**, **skipped** with a reason (user declined, not applicable, needs administrator rights) or **unavailable**: hardware and firmware, devices, drivers and driver store, driver latency, interrupts and MSI/affinity, USB and input chains, services, scheduled tasks, startup, apps, Windows Update, privacy and background activity, gaming settings, risky leftovers, network, audio, installed games and their configs. Silently omitting one is not acceptable.
