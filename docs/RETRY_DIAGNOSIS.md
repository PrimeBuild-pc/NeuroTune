# Retry diagnosis — v0.9.0-alpha.2

This unsigned prerelease improves startup diagnostics, not a proven repair of
the tester's underlying startup block. Use Windows 11 x64. Do not disable
Defender/SmartScreen, change folder permissions or install global Pi/Node to
work around an error. Never delete recovery journals or reset credentials for
this test. No Apply is needed.

## 1. Install and retry once

1. Download `NeuroTune_0.9.0-alpha.2_x64-setup.exe` from this release and verify
   it against `SHA256SUMS` using `Get-FileHash -Algorithm SHA256` in PowerShell.
2. Close NeuroTune completely, install the new version, and reopen it. Confirm
   **v0.9.0-alpha.2** at the bottom of the app. Existing settings/recovery remain.
3. Keep the same provider and exact model used in the failed test (for example
   DeepSeek / `deepseek-flash`). Do not send or paste the API key in feedback.
4. If an interrupted prewrite diagnosis is retained, choose **Finish without
   changes**. Do this only when offered for a diagnosis that never applied
   anything. A recovery warning must instead be reviewed, not bypassed.
5. Open **Complete diagnosis**, select **Advisory audit** and choose
   **Start AI audit · no changes**. This first click collects local evidence.
   Review its preview and approve transmission only if you consent to sending
   that evidence to the configured provider and its possible API charges.
   There are no workload captures and Apply is unavailable in this mode.
6. Wait for the result within the configured investigation budget. Take a
   screenshot of the full error (including any Win32/exit code) or successful
   report. Collect diagnostics before retrying or dismissing this attempt.
7. Only if the audit succeeds, optionally repeat the original **Measured
   optimization / Complete diagnosis** flow to test its measurement stages.
   Do not approve/apply proposals for this diagnosis-startup check.

Labels above are the English UI labels; localized interfaces show their
translations. **Test & discover models** only checks the model catalog, not
inference or Pi startup. There is currently no app button that exports this
startup diagnostic report: use the script below.

## 2. Export the right diagnostics

### Easiest: double-click the BAT (no commands)

Download **NeuroTune-Diagnostics.zip** from the release. Right-click it and choose
**Extract All**. Keep the extracted files together. After the analysis fails or
finishes, double-click **Raccogli-log-NeuroTune.bat**, approve the Windows UAC
prompt using the same Windows account as NeuroTune, and wait. The window stays
open and shows where the Desktop JSON was saved. Send that JSON after reviewing
it, plus the error screenshot. Do not open the BAT from inside the ZIP and do
not open the PS1 manually. The BAT only launches the existing collector; it does
not start AI, download anything or change permanent execution policy. Decline or
report an organization/security block instead of weakening Windows protection.

**Italiano:** scarica lo ZIP, premi **Estrai tutto**, fai doppio clic su
**Raccogli-log-NeuroTune.bat**, conferma **Si** e invia il JSON creato sul Desktop.
Il file PS1 deve restare nella cartella, ma non devi aprirlo.

### Optional: manual PowerShell command

Download `Collect-NeuroTune-Diagnostics.ps1` from the **same release** (or use
the copy at the root of the portable ZIP). Review the script and its SHA256SUMS
entry. Right-click Start → **Terminal (Admin)** / **Windows PowerShell (Admin)**,
using the **same Windows account that ran NeuroTune**, and execute:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\Downloads\Collect-NeuroTune-Diagnostics.ps1"
```

`-ExecutionPolicy Bypass` applies only to this process; the script does not
change machine/user execution policy. If organizational policy blocks it,
report that rather than weakening the policy. Adjust the script's path if your
Downloads folder is elsewhere.

For the portable build or a non-default installation, also specify the folder
containing `NeuroTune.exe`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\Downloads\Collect-NeuroTune-Diagnostics.ps1" -InstallDirectory "C:\Path\To\NeuroTune-0.9.0-alpha.2-win-x64"
```

The script prints the exact output path and creates one
**`NeuroTune-diagnostics-<date>-<suffix>.json` on the Desktop**, including a
redirected/OneDrive Desktop. Review it in Notepad before sharing: redaction is
best-effort, not guaranteed anonymization. No download, model request, installed
program execution or system configuration change is performed by the script.
Only its output JSON is written.

## 3. Send these items privately

- The generated diagnostics JSON, after reviewing it.
- Screenshot of the full error or successful report, with personal data hidden.
- Confirmed app version, chosen provider and exact model ID (never its key).
- Whether this was installer or portable, Advisory audit or Measured optimization.
- Approximate attempt time, how long it waited and whether it stopped itself or
  was cancelled/closed manually.
- If the script reports missing files or no runs, include that report too and
  confirm whether Windows UAC used a different administrator account.

The authoritative run files are under:

```text
%ProgramData%\NeuroTune-journals\<Windows-user-SID>\runs\<run-id>\run.json
```

They require administrator access and contain more private evidence than the
export. **Do not send/copy the whole folder by default.** The script reads only
the current account's ten most recently modified runs and exports selected
status/error/audit fields, not their evidence or recovery state.
`%LocalAppData%\NeuroTune` holds settings, encrypted credentials, measurements
and any `NeuroTune.log`; that log may be absent and is not the primary planner
audit. **Do not zip all of LocalAppData or share credential files.** Never copy
`C:\Windows`, `System32` or the current shell directory as a fallback.
