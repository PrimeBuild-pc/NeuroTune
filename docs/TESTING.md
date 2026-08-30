# Alpha Test Guide

Use a disposable Windows virtual machine. Do not use a primary PC for the first validation cycle.

## Prerequisites

- A currently supported Windows 11 build, x64
- A VM checkpoint created outside the guest operating system
- System Protection enabled on the Windows drive
- Administrator access
- A low-value test API key, an OpenRouter account, or a disposable local model endpoint

## 1. Setup

1. Start NeuroTune and approve the administrator prompt.
2. Verify **Use Windows setting**, **Light**, and **Dark** in Settings; change the Windows app theme and confirm System follows it.
3. Select a provider, enter its API key, and choose **Test & discover models**.
4. For OpenRouter, also test browser sign-in and confirm the loopback success page returns control to NeuroTune.
5. Test one custom HTTPS endpoint and one local Ollama, LM Studio, or vLLM endpoint.
6. Confirm that the model list loads and the selected model persists after restarting NeuroTune.
7. Inspect `%LocalAppData%\NeuroTune`: credential files must not contain readable plaintext.

## 2. Local Scan and Privacy

1. Select **Scan this PC** without requesting an AI diagnosis.
2. Confirm that live phase progress appears for hardware/firmware, Windows/Registry, network/devices, software, and services.
3. During a second scan, select **Cancel scan** and confirm the UI treats it as informational, keeps no partial profile, and leaves no agent, `powercfg`, `netsh`, WMI, or other probe process running.
4. Confirm that CPU, GPU, motherboard, BIOS, DIMMs, BCD, drivers, applications, device issues, and all 83 Registry facts are populated.
5. Review the sanitized JSON, fact count, UTF-8 payload size/limit, telemetry support matrix, exact component baselines, and local conflicts.
6. Confirm that unknown components report `baseline unavailable` rather than a nearest-model guess.
7. Confirm that the Windows username, device name, serial numbers, and MAC addresses do not appear.
8. Confirm that no provider request occurs until **Run AI diagnosis** is selected.

## 3. Diagnosis and Review

1. Enter at least one game or workload, test each optimization priority, and run the AI diagnosis.
2. Confirm that each finding cites an observed profile value and every recommendation maps to a visible action.
3. Inspect each conflict: both setting IDs and values, relationship, confidence, objective, and counterproductive effect must be explicit.
4. Switch among AI recommended, Conflict fixes, and All supported; unavailable or already-configured actions must remain visible but disabled.
5. Compare Select all supported, Select safe only, Clear, and manual selection behavior.
6. Confirm that every action shows its current state, risk, reason, and restart requirement; high risk must warn without disappearing.
7. Print the report and verify that navigation and execution controls are omitted.

## 4. Apply and Roll Back

1. Select one low-risk action.
2. Apply it and confirm that NeuroTune reports a completed operation.
3. Verify that the operation directory contains `manifest.json` and Registry exports where applicable.
4. Confirm that a restore point with the NeuroTune operation ID exists in Windows.
5. Review the before/after observations; treat them as telemetry, not a benchmark.
6. Select the operation in Activity & Restore and run rollback.
7. Confirm that the action returns to its original state and the manifest reports **Rollback completed**.

## 5. Recovery

In a disposable VM only, terminate NeuroTune while an operation is marked **Applying**. Restart it and confirm that the recovery banner identifies the interrupted journal and allows rollback.

## 6. ETW Measurements

1. Open **Measurements**, start a workload yourself, refresh the process list,
   and select that already-running process.
2. Record a 30-second Baseline. Close the UI during a second capture and verify
   that the internal watchdog still saves it at the deadline without leaving a
   named WPR session active.
3. Test Stop, Cancel, and analysis cancellation. Cancel must delete incomplete
   capture data; analysis cancellation must leave the ETL retryable.
4. Confirm the report separates ISR and DPC, shows Ready Time, running time,
   migrations, per-core residency/interrupt share, and explicit trace quality.
5. With **Keep raw ETL** off, confirm `capture.etl` disappears after successful
   analysis. With it on, confirm the file stays local.
6. Create one Baseline and one Candidate to confirm an Exploratory comparison;
   then create 3+3 valid sessions to confirm median aggregation and the
   Improvement/Regression/Inconclusive rule.
7. Opt one completed report into the next AI diagnosis. Inspect the provider
   payload and verify it contains only `measurement:*` IDs with numeric/boolean
   values—never ETL bytes, PID, command line, username, or full path.
8. Repeat the smoke test on supported Windows 11 builds used for release.
   Record WPR orphan checks and lost-event counts. Physical DirectX validation
   on AMD and NVIDIA hosts is mandatory before enabling any GPU action.
9. Select at least three valid Baselines and generate the GPU IRQ preview.
   Confirm it returns at most three distinct physical cores, uses the Windows
   group/SMT/efficiency/cache-cluster labels verbatim, and exposes no Apply
   control. Confirm WPR reports no active session before and after this step.

## Reporting

Include the Windows build, hardware/VM configuration, provider, selected action IDs, operation status, and redacted log lines. Never include an API key or an unredacted system profile.

## Hyper-V automation

From an elevated host PowerShell run scripts/vm-provision.ps1, then run scripts/vm-validation.ps1 with InstallerPath set to the generated NSIS installer.

For the read-only ETW pass, publish the agent and run the following from an elevated host PowerShell while the disposable Windows 11 VM is already running:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\vm-measurement-smoke.ps1 -AgentDirectory .\ui\src-tauri\agent
```

This records and analyzes three 30-second Baselines, exercises the independent watchdog, validates trace quality and read-only GPU previews, then deletes its sessions and temporary guest files. It does not restore checkpoints or change VM power state. The redacted result is written to `artifacts/vm-measurement-smoke.json`.

For a physical AMD/NVIDIA DirectX pass, close the NeuroTune UI, start the game,
and keep a repeatable scene running. From an elevated PowerShell use its
current PID:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\physical-gpu-measurement.ps1 -AgentDirectory .\ui\src-tauri\agent -ProcessId (Get-Process RDR2).Id -GraphicsApi DirectX12
```

The harness records three Baselines, rejects lost or incomplete traces,
inspects the current GPU IRQ policy, verifies candidates remain read-only,
deletes all created sessions, and writes a redacted report to
`artifacts/physical-gpu-measurement.json`. Use `-GpuName` when the host exposes
more than one physical AMD/NVIDIA adapter. A successful run validates only
that exact GPU, driver, game, tester-declared graphics API, and Windows build.

For the slower per-action Registry integrity pass, run scripts/vm-action-integrity.ps1 with InstallerPath set to the same final installer. It creates real restore points, validates mixed Registry value kinds and absent values, and restores the selected clean Hyper-V checkpoint in a finally block.

Provisioning refuses to overwrite an existing VM or VM directory. Validation restores Clean-NeuroTune-Alpha2, so use it only with the disposable NeuroTune-W11 guest created for this project.

The automated validation report covers installation, the validated baseline action apply/verify/rollback cycle, interrupted Apply and Rollback recovery, orphan-process checks, Defender, PawnIO absence, and clean uninstall. The page-file, core-parking, and dynamic per-app GPU writers remain explicitly pending until the repaired disposable VM is available. Scaling, keyboard navigation, forced-colors, physical sensors, SPD/XMP/EXPO, and real-GPU HAGS remain manual or physical-hardware checks.
