# CLM as the System One assistant

[Contrastive Language Models](https://github.com/Contrastive-LM/CLM) (CLM) are the preferred optional "System One"
classifier. A CLM scores typed questions (yes/no, choice, rating) against a state with an encoder plus a small
projection head, so one classification is a few embeddings and a dot product: about 0.2 s once the server is warm.

NeuroTune uses it for exactly what System One always did: an advisory topic label (memory, drivers, network, power,
software, unknown) over sampled context, with evidence IDs that match that topic. It cannot approve, execute or change
anything, and it never replaces the main AI.

## What runs where

| Piece | Where | Notes |
|---|---|---|
| Encoder: Qwen3-8B Q8_0 GGUF via llama.cpp (Vulkan) | `127.0.0.1:8090` | About 9 GB on disk and 9-10 GB of VRAM (or RAM with `-Cpu`) |
| CLM server with the reference head | `127.0.0.1:8700` | CPU torch, 75 MB head, `--no-ui` |
| NeuroTune | your PC | Calls `POST /v1/systemone` on loopback only |

Nothing is sent to a cloud service, there is no API key, and NeuroTune does not download or start anything: you do.

## Install and run

```powershell
./scripts/clm-stack.ps1 install   # about 9 GB; every download is SHA-256 pinned
./scripts/clm-stack.ps1 start     # encoder + CLM, both on 127.0.0.1
./scripts/clm-stack.ps1 status
./scripts/clm-stack.ps1 stop      # frees the encoder's VRAM/RAM
```

Then open Advanced tools → System One assistant, choose **CLM · local contrastive model** and enable it. If the server
is stopped the assistant is skipped and the original evidence and main AI are used. The encoder is large: stop it
before latency-sensitive gaming or measurement sessions.

`NEUROTUNE_CLM_URL` overrides the endpoint, but only loopback addresses are accepted.

## Safety and trust

- Loopback only for NeuroTune and for the Pi router; the stack binds `127.0.0.1`.
- The reference head is a `torch` pickle. It is only loaded after its SHA-256 matches the pin in the script.
- The upstream package declares vLLM, which does not exist on Windows. The stack installs it with `--no-deps` and the
  dependencies pinned in `scripts/clm-requirements.txt`; any OpenAI-compatible `/v1/embeddings` server can be the encoder.

## Quality notes (measured on this PC)

- CLM ranks candidates that read like answers. Bare labels reached 4/12 on a labeled topic panel; answer-style
  candidates reached 10/12, and that phrasing is what NeuroTune sends. A second yes/no question makes it abstain on
  vague observations.
- llama.cpp with a Q8 quantization is not bit-identical to the upstream vLLM bf16 encoder. Direction matched the
  upstream README example but the probabilities differed, so scores are uncalibrated and only ever shown as such.
- Scores gate nothing: they only choose which already-collected evidence IDs are highlighted.

## Pi skill routing

`scripts/pi-clm-route.mjs` is the loopback-only router used by Pi scouts for `[SYSTEM ONE SKILL ROUTING]`. Copy it to
`~/.pi/agent/bin/clm-route.mjs`. It sends a generic task summary and the installed skill names/descriptions, treats
"no installed skill fits" as an explicit candidate, and prints `{covered, search, bestSkill, bestSkillProbability}`.
It exits with code 2 when CLM is not running so the scout falls back to its own judgement.

## Status of the previous sources

The Rizzo local model and the OpenRouter classifiers are still selectable so existing configurations keep working;
new installations default to CLM. Removing them is a separate, mechanical change once CLM has proven itself on your
hardware.
