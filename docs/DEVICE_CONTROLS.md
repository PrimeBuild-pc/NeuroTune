# Device Manager controls (latency presets)

For the **System latency** and **Network optimization** presets the AI may recommend disabling specific Device
Manager devices that your workload does not use. The recommendation explains what each device does, what you lose
and why removing it could reduce latency. You read it, choose which ones to apply, and NeuroTune applies them with a
restore point, a per-action journal and one-click rollback. A benefit is never promised: measure before and after.

## How it stays safe

- **Allowlist only.** The model can only name a registered `device.disable.<slug>` capability. It never supplies an
  instance ID, a command or a path; NeuroTune resolves the instance from Windows Plug and Play at the moment of use.
  The parametric kind `device.disable` takes only `{ "Slug": ... }` and maps it onto the same registered capability.
- **Exact matching.** Each capability matches a device class plus an anchored name pattern (and, for radios, a physical
  bus prefix). Anything else is ignored.
- **Guards.** A capability is unavailable when the device is absent, when it hosts a keyboard, mouse, HID, USB, disk,
  display or processor device below it, when a battery is present (battery placeholder), or when it is the only
  connected physical network adapter (Wi-Fi). Not running as administrator also makes it unavailable.
- **Policy.** Device toggles are never pre-selected, whatever the risk profile, because only you know whether you use
  the function. High-risk items also need separate confirmation. HPET and platform-timer targets remain outside the
  policy.
- **Reversible.** The snapshot stores each instance's prior enabled state. Rollback re-enables only devices that were
  enabled before and still match the capability; devices you had already disabled stay disabled.
- **Restart.** `pnputil` may report that a restart is required (exit 3010); that is treated as a pending success and
  shown as restart-required, not as a failure.

## Candidates

| Capability | Device | Risk | Notes |
|---|---|---|---|
| `umbus-root-bus` | UMBus Root Bus Enumerator | High | UMDF software-device bus: sensors, some audio/Bluetooth helpers |
| `composite-bus` | Composite Bus Enumerator | High | Composite virtual devices |
| `programmable-interrupt-controller` | Programmable interrupt controller | High | Legacy 8259 stub; effect on APIC systems unproven |
| `direct-memory-access-controller` | Direct memory access controller | Medium | Legacy ISA DMA stub |
| `numeric-data-processor` | Numeric data processor | Low | Legacy x87 placeholder |
| `remote-desktop-redirector-bus` | Remote Desktop Device Redirector Bus | Low | RDP drive/printer/audio redirection |
| `virtual-drive-enumerator` | Microsoft Virtual Drive Enumerator | Low | Mounting ISO/VHD |
| `ndis-virtual-network-enumerator` | NDIS Virtual Network Adapter Enumerator | Medium | VPN, Hyper-V, WSL virtual adapters |
| `kernel-debug-network-adapter` | Microsoft Kernel Debug Network Adapter | Low | Network kernel debugging only |
| `acpi-control-method-battery` | ACPI Control Method Battery | Low | Only when no battery exists |
| `hyperv-virtual-ethernet` | Hyper-V virtual ethernet adapters | Medium | Hyper-V, WSL2, Sandbox, containers |
| `wan-miniports` | WAN Miniport adapters | Medium | VPN, PPPoE, packet capture |
| `wifi-direct-virtual-adapters` | Wi-Fi Direct virtual adapters | Low | Mobile Hotspot, Miracast, Nearby Sharing |
| `bluetooth-radio` | Bluetooth radio | Medium | All Bluetooth peripherals |
| `wifi-adapter` | Wi-Fi adapter | Medium | Only while another physical adapter is connected |
| `gpu-hdmi-dp-audio` | GPU HDMI/DisplayPort audio | Medium | Check the default playback device first |
| `virtual-audio-drivers` | NVIDIA/Steam/Oculus/Virtual Desktop audio | Low | Streaming and VR audio routing |
| `gs-wavetable-synth` | Microsoft GS Wavetable Synth | Low | Software MIDI synth |

Adding a candidate is a table row in `src/NeuroTune.Core/DeviceControl.cs`: class, anchored name patterns, what it does,
what is lost, the latency rationale and a risk tier. Do not add input, storage, display, USB host/hub, processor,
firmware or security devices.

## Manual recovery

If you lose a function and cannot reach NeuroTune, open an elevated terminal and re-enable the device by instance ID:

```
pnputil /enum-devices /disabled
pnputil /enable-device "ROOT\UMBUS\0000"
```

Device Manager → View → Show hidden devices also lists disabled devices, and System Restore can undo the restore
point NeuroTune created before applying.
