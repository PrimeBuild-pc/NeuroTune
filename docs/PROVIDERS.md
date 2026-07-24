# Model Provider Guide

## Connection Matrix

| Connection | Authentication | Protocol | Default base URL |
|---|---|---|---|
| OpenRouter | API key or browser authorization | OpenAI-compatible | `https://openrouter.ai/api/v1` |
| OpenAI | API key | OpenAI-compatible | `https://api.openai.com/v1` |
| Anthropic | API key | Anthropic Messages | `https://api.anthropic.com/v1` |
| DeepSeek | API key | OpenAI-compatible | `https://api.deepseek.com/v1` |
| Custom | Optional API key | OpenAI-compatible or Anthropic Messages | User supplied |
| Local | None by default | OpenAI-compatible | Loopback only |

## Browser Authorization

NeuroTune exposes browser sign-in only for OpenRouter because OpenRouter provides an official third-party OAuth flow. The implementation uses:

- a cryptographically random PKCE verifier and S256 challenge;
- a random CSRF state checked with constant-time comparison;
- an ephemeral `127.0.0.1` callback;
- a two-minute timeout;
- immediate DPAPI encryption of the issued key.

The flow follows the public implementation published by [OpenRouterLabs/spawn](https://github.com/OpenRouterLabs/spawn).

ChatGPT Plus, Claude Pro, and similar consumer subscriptions do not grant API access to third-party applications. NeuroTune will not capture browser cookies, impersonate an official client, or use undocumented subscription endpoints. OpenAI and Anthropic therefore remain API-key connections unless those providers publish a suitable third-party authorization flow.

## Custom Providers

A custom provider must expose one of these shapes relative to its base URL:

### OpenAI-compatible

- `GET /models`
- `POST /chat/completions`
- Bearer authentication when a key is supplied

### Anthropic-compatible

- `GET /models`
- `POST /messages`
- `x-api-key` authentication when a key is supplied

Built-in endpoints are locked. Custom remote endpoints must use HTTPS, cannot contain embedded credentials, and cannot redirect authenticated requests. Plain HTTP is accepted only for loopback hosts.

## Local Models

Presets are included for:

- Ollama: `http://127.0.0.1:11434/v1`
- LM Studio: `http://127.0.0.1:1234/v1`
- vLLM: `http://127.0.0.1:8000/v1`

Start the local server before selecting **Test & discover models**. If a local server requires a key, use the Custom connection type with its loopback base URL.
