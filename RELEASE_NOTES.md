# NeuroTune v0.6.0-alpha.1

This unsigned alpha introduces the foundation of NeuroTune's contextual AI
planner. It is intended for controlled testing on Windows 10 and Windows 11,
preferably first in a disposable VM.

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
- User-entered performance metrics are contextual, unverified observations;
  NeuroTune does not yet automate game benchmarks or claim measured gains.
