# Alpha Test Guide

Use a disposable Windows virtual machine. Do not use a primary PC for the first validation cycle.

## Prerequisites

- Windows 10 22H2 or a currently supported Windows 11 build, x64
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
2. Confirm that CPU, GPU, memory, and Windows cards are populated.
3. Review the sanitized JSON profile.
4. Confirm that the Windows username and device name do not appear.
5. Confirm that no provider request occurs until **Run AI diagnosis** is selected.

## 3. Diagnosis and Review

1. Run the AI diagnosis.
2. Confirm that every recommendation maps to a visible action in the Review tab.
3. Confirm that unavailable and already-configured actions cannot be selected.
4. Compare Safe, Gaming, and manual selection behavior.
5. Confirm that every action shows its current state, risk, reason, and restart requirement.

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

## Reporting

Include the Windows build, hardware/VM configuration, provider, selected action IDs, operation status, and redacted log lines. Never include an API key or an unredacted system profile.
