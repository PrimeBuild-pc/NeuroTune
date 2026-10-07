# Model Provider Guide

## Connection Matrix

| Connection | Authentication | Protocol | Default base URL |
|---|---|---|---|
| OpenRouter | API key or browser authorization | OpenAI-compatible | `https://openrouter.ai/api/v1` |
| OpenAI API | API key, separate API billing | OpenAI-compatible | `https://api.openai.com/v1` |
| ChatGPT plan | Official Sign in with ChatGPT; eligible plan/credits | Streaming OpenAI Responses | `https://api.openai.com/v1` |
| Anthropic | API key | Anthropic Messages | `https://api.anthropic.com/v1` |
| DeepSeek | API key | OpenAI-compatible | `https://api.deepseek.com/v1` |
| Custom | Optional API key | OpenAI-compatible or Anthropic Messages | User supplied |
| Local | None by default | OpenAI-compatible | Loopback only |

## Discover and select models

For API and local connections, **Test & discover models** fetches the connected
endpoint's catalog. **Available models** shows every returned ID without filtering
by the currently typed model. Choose an entry or enter an **Exact model ID**
manually, then **Save securely**. Discovery does not silently replace your API
model; changing endpoint/protocol/credential clears stale choices. Startup does
not contact a provider automatically; use the explicit discovery control.

For example, `deepseek-v4-flash` is selectable if the DeepSeek endpoint returns
that ID for your account. NeuroTune does not invent model IDs, aliases or account
permissions. Catalog membership does not establish chat/diagnosis support, image
support, available inference quota or a successful validated response. OpenAI's
catalog may also contain non-chat models: select a suitable model explicitly.

## DeepSeek planner reliability (unreleased after alpha.3)

The source changes after alpha.3 enable DeepSeek's documented
[JSON Output](https://api-docs.deepseek.com/guides/json_mode), with an explicit
system contract. This setting is not assumed for arbitrary custom/local models.

DeepSeek receives stable, investigation-local short evidence references such as
`f0001`, with descriptive source labels. These are **evidence references, not
model IDs**. Requests, findings, recommendations and audit coverage are resolved
exactly to original IDs before validation and storage. Unknown/unprovided IDs,
repeated or oversized requests, unsafe tools and fabricated explicit values
still fail closed; there is no fuzzy matching or automatic inference retry.

Finding values omitted by the model are resolved from the exact provided local
fact. AI interpretation stays in `assessment`; it is not a measured observation
or authorization to Apply. Safe failures distinguish validation stages without
showing raw provider responses or arbitrary exception text.

## Browser Authorization

NeuroTune exposes separate official browser flows for OpenRouter and ChatGPT. OpenRouter authorization uses:

- a cryptographically random PKCE verifier and S256 challenge;
- a random CSRF state checked with constant-time comparison;
- an ephemeral `127.0.0.1` callback;
- a two-minute timeout;
- immediate DPAPI encryption of the issued key.

The flow follows the public implementation published by [OpenRouterLabs/spawn](https://github.com/OpenRouterLabs/spawn).

### ChatGPT plan — official open-source flow

The current [OpenAI contract for open-source applications](https://developers.openai.com/siwc/token-sharing-open-source) supports eligible ChatGPT Plus/Pro plans or available credits. It is distinct from API-key billing and from the restricted commercial-website integration. The DevDay recap URL returned HTTP 403 during this review; implementation follows the directly accessible official developer docs, not a claimed recap announcement or Codex client impersonation.

1. Select **ChatGPT plan**, then **Continue with ChatGPT**. The system browser opens the official OpenAI authorization page. No password, cookie or token is entered into NeuroTune.
After sign-in, use the native **Model** dropdown; selection saves automatically. No URL/protocol/key fields are shown for ChatGPT. Existing connections load their catalog only through explicit discovery; account switching/add/sign-out live under **Account options**. **Reload models** retries discovery without requiring another browser login. If discovery fails, the UI distinguishes a saved login from an unavailable catalog. Successful model catalogs have a separate 16 Mi-character/10,000-entry bound; inference/auth/error responses retain their smaller bounds.

2. First registration uses `dynamic_agent_client`, `agent_name_hint=NeuroTune` and a persisted `urn:uuid` host identity. The callback-issued `oaiapp_…` ID is used for token exchange and future sign-ins. Existing client registrations are retained across sign-out and failed login. The picker supports multiple registrations; OpenAI's authorization screen determines the actual account/workspace eligibility.
3. Authorization Code + PKCE uses random state/nonce, a two-minute loopback callback and RSA/RS256 ID-token checks for signature, issuer, audience, expiry, nonce and subject. Discovery endpoints are pinned. Access/refresh/ID tokens remain in a DPAPI-encrypted local file, never in frontend state, logs or repository files. A shared file lock serializes refresh-token rotation across agent processes.
4. Requested scopes are `openid profile email offline_access resource.invoke chatgpt.tokens.use.direct`, with resource `https://api.openai.com/v1`. Identity-only authorization does **not** enable diagnosis; reconnect to grant plan use. Terminal refresh errors clear tokens but retain client registration; transient failures do not erase credentials.
5. Model discovery uses the account's `models` / `slug` / `display_name` / `visibility` catalog, not assumed Chat Completions model names. Inference goes only to public `POST /v1/responses`, with `store:false`, `stream:true` and array input. Preview-incompatible parameters such as `temperature`, `max_output_tokens`, `previous_response_id` and tools are not sent.
6. SSE output is bounded. Incomplete/refused/failed responses and unexpected tool outputs are discarded; no model-generated command is executed. Usage-limit errors include the safe machine code, parameter/request ID where supplied, and a [Manage usage](https://chatgpt.com/settings/usage) link. There is **no automatic API-key billing fallback**.
7. First connection displays a native acknowledgement dialog explaining shared plan limits/credits. **Sign out selected account** requests revocation and always clears local tokens; the UI distinguishes confirmed remote revocation from a failed/unconfirmed attempt.

On 2026-10-01 the rebuilt packaged agent successfully loaded **six selectable models from the existing authorized ChatGPT account**. No inference request or API-key billing was performed by that catalog check. Full fresh authorization, issuer registration acceptance, refreshed credentials and actual billed-plan inference still require their own live acceptance checks. Passing local tests does not demonstrate those live outcomes. Claude Pro and other consumer subscriptions remain unsupported unless their providers expose an appropriate official flow. NeuroTune never captures browser cookies or uses undocumented ChatGPT backend endpoints.

## Optional assistant via OpenRouter

In Advanced tools → System One assistant choose **OpenRouter API**, enter a separate API key, load/select its classifier model, and explicitly enable cloud usage. The key is DPAPI-encrypted separately (`system-one-openrouter.key`) and never changes the main provider, model, subscription or key. Deleting the classifier key leaves main credentials intact.

This is a **token-generating cloud topic classifier**, not the local Rizzo probability engine or a guaranteed System One model hosted by OpenRouter. Calls consume API credits, send sampled context through OpenRouter to the selected provider, have a 30-second deadline/256-token output cap, reject unsupported/incomplete outputs and do not use an automatic local/provider fallback. Returned probability/action fields are ignored; no calibrated score is fabricated. Common local identity/drive-path redaction is not guaranteed anonymization: do not put secrets into goal/error context. Ordinary System scan and recording do not invoke it; it is advisory during subsequent analysis/application phases.

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
