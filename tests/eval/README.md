# Prompt evaluation (F11)

Manual suite, not run in CI because it calls a real provider and costs credits. Use it before and after changing
presets or prompts: a change that helps one model can hurt another.

Each `*.json` case holds goals, a `SystemProfile`, canned reader output (`observations`, keyed by tool ID; anything
else returns "unavailable") and the proposals that must (`expected`) or must not (`forbidden`) appear. A matcher
uses `actionId` (prefix), `actionKind` and a subset of `parameters` (case-insensitive). Proposals that NeuroTune
refused on the test PC still count, because the eval measures what the model proposed.

Run from the repository root:

```powershell
$env:NEUROTUNE_EVAL_PROVIDER = 'Local'            # OpenRouter, OpenAI, Anthropic, DeepSeek, Custom, Local
$env:NEUROTUNE_EVAL_MODEL = 'qwen2.5-14b-instruct'
$env:NEUROTUNE_EVAL_BASEURL = 'http://127.0.0.1:1234/v1'   # optional
$env:NEUROTUNE_EVAL_KEY = '...'                             # optional
dotnet test -c Release --filter "FullyQualifiedName~EvalRunner.Run_eval" --logger "console;verbosity=detailed"
```

The report (recall, missed items, violations, parse failure, turns, every proposal) is written to `results/`, which is
not committed. Nothing is written to Windows: readers are stubbed and apply is never called.

To add a real case, export a scan from a PC, remove names, serials, paths with user names and anything personal, then
write the expectations by hand.
