# NeuroTune Design System

The desktop interface uses a web frontend inside Tauri 2. Its structure is informed by the local-first desktop patterns demonstrated by [Open Design](https://github.com/nexu-io/open-design) (Apache-2.0) and the native webview architecture provided by [Tauri](https://github.com/tauri-apps/tauri) (MIT/Apache-2.0). NeuroTune does not copy Open Design product assets or branded layouts.

## Principles

1. **Readability before atmosphere:** all text and status colors use semantic tokens rather than one-off colors.
2. **One primary action per surface:** secondary actions remain visually quiet.
3. **Visible trust boundary:** credentials, provider payloads, compatibility, risk, backup, and rollback state stay explicit.
4. **Stable hierarchy:** 8 px spacing rhythm, consistent card radius, and a restrained type scale.
5. **Purposeful motion only:** short hover and busy-state transitions; no decorative animation that obscures state.

## Appearance

Users can choose:

- **Use Windows setting:** follows `prefers-color-scheme` from WebView2 and updates when Windows changes.
- **Light:** persistent manual light override.
- **Dark:** persistent manual dark override.

The preference is stored locally. Both themes expose the same semantic token names, so components do not contain theme-specific styling.

## Contrast

`ui/src/contrast.test.ts` parses the shipped CSS tokens and requires WCAG AA contrast of at least 4.5:1 for:

- primary text on page and card surfaces;
- secondary text on card surfaces;
- text on primary actions;
- success, warning, and danger text on their tinted backgrounds.

Focus indicators use a separate high-contrast token and are never removed.

## Core Tokens

| Role | Light | Dark |
|---|---:|---:|
| Page background | `#f4f6f9` | `#0b0e14` |
| Card surface | `#ffffff` | `#141a24` |
| Primary text | `#111827` | `#f4f7fb` |
| Secondary text | `#4b5b70` | `#bdc7d5` |
| Primary action | `#4f46e5` | `#9388ff` |
| Border | `#d8dee8` | `#2d3747` |

## Provider UX

Provider selection separates connection type from credentials and model choice. Built-in providers lock their trusted endpoint. Custom remote providers require HTTPS; HTTP is accepted only for loopback local-model servers. Browser sign-in is displayed only when a provider exposes an official third-party authorization flow.
