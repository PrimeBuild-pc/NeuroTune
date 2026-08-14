# NeuroTune v0.7.0-alpha.1

This unsigned alpha adds local, measurement-first ETW analysis to NeuroTune's
contextual planner. It supports Microsoft-supported Windows 11 x64 builds and
should be tested first in a disposable VM.

## Highlights

- Structured plans distinguish typed NeuroTune actions, manual guidance,
  unverified scripts, verified resources, and official update notices.
- Safe, Balanced, and Aggressive policies change deterministic preselection;
  high-risk actions require a separate confirmation enforced by the agent.
- Model-generated scripts can be inspected, copied, and saved as inert text,
  but NeuroTune has no command capable of running them.
- The reversible capability registry contains 25 VM-tested actions and keeps
  older operation manifests readable.
- Verified text-artifact and official GPU/chipset/motherboard advisor
  foundations are present; the public artifact and external-app catalogs are
  intentionally empty pending individual PrimeBuild approval.
- Named WPR sessions capture an already-running workload for deterministic
  ISR/DPC, scheduling, Ready Time, migration, per-core, and comparison reports.
- Raw ETL stays local and is deleted after successful analysis unless the user
  explicitly keeps it. Optional AI receives only normalized measurement facts.
- Read-only GPU IRQ candidates can be previewed from three valid baselines,
  and the current policy can be inspected for exact rollback compatibility;
  no device policy is writable in this release.
- A physical DirectX validation harness records three quality-gated Baselines
  and emits a redacted report without retaining ETL or enabling apply.

## Distribution and verification

The release contains an unsigned per-machine NSIS installer and a portable ZIP
for x64 Windows. "Portable" means no installation is required; settings and
rollback data are still stored under `%LocalAppData%\NeuroTune`.

Verify either asset against `SHA256SUMS` before running it. Windows SmartScreen
may warn because PrimeBuild does not purchase an Authenticode certificate for
this free open-source alpha.

## Known limitations

- Scaling at 150%/200% and Narrator still require manual acceptance testing.
- The cross-version page-file writer, typed per-app GPU target, and
  platform-qualified core-parking actions are not yet in the public catalog.
- Version-feed adapters and the first reviewed external artifact are pending.
- DirectX repeatability on physical AMD and NVIDIA systems remains required
  before the supervised GPU IRQ-affinity writer can be enabled.
- NeuroTune does not automate game launch or claim measured gains from a
  universal score.
