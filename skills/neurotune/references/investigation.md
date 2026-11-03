# Inventory investigation

Commands below follow `node "<skill-directory>/scripts/cli.mjs"`.

```text
doctor
collect --out "C:\Users\you\Documents\inventory-01.json"
evidence --input "C:\Users\you\Documents\inventory-01.json"
```

The output directory must already exist. Collection is Windows-only, read-only and normally non-admin. It runs the bundled fixed PowerShell collector with `-NoProfile -NonInteractive -ExecutionPolicy Bypass -File`; the policy override applies to that process only and the machine policy is unchanged. No shell code is accepted. CIM reads can be slow or unavailable; the process has a 90-second deadline. An error is not permission to escalate privileges.

Collection stages a raw report in the user's temporary directory, projects it to an allowlist, saves the compact report and deletes staging files in a finally block. An interrupted process may leave staging files. The stored facts include Windows build, architecture, Secure Boot/VBS availability, memory capacity, CPU model/core counts, GPU model/driver version and hypervisor presence. Null is unavailable. Core counts of zero from missing source properties are not affirmative hardware measurements. Warning count exposes partial collection, not a complete explanation of each unavailable subsystem.

Neither serial numbers, raw device instance IDs, network addresses nor process lists are selected for the compact report. Hardware strings still originate outside our control: this is data minimization, not guaranteed anonymization. Review the file locally before sharing. Do not open it with an AI tool to perform the pre-sharing privacy review: that already shares it.

The collector briefly reads more information than the compact report retains, including topology, motherboard/BIOS identity and interrupt-policy metadata. It does not execute vendor tools or modify firmware. Its inventory code is inherited from NeuroTune; provenance is recorded in the package NOTICE.

Evidence IDs have the form `<report hash>:<field>`. The hash includes timestamp and content. Cite the actual IDs returned by `evidence`; do not invent shorthand IDs. IDs establish which artifact a statement refers to, not whether that artifact is trustworthy or a hypothesis is true.

Interpretation rules:

- VBS or Secure Boot being enabled is not evidence that disabling it will fix stutter.
- GPU driver version alone does not establish a known defect or the latest available version.
- Core count, clock specification and RAM size do not measure load, throttling or scheduling delay.
- This inventory does not measure DPC/ISR, input latency, FPS, network jitter or malware coverage.
- Keep findings proportional: identify missing measurements, offer alternatives and ask what changed when symptoms started.
