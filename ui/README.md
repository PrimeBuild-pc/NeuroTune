# NeuroTune Desktop UI

Tauri 2 desktop shell with a React and TypeScript frontend. Windows-only operations are delegated through the restricted Tauri command bridge to `NeuroTune.Agent`.

```powershell
npm ci
npm test
npm run typecheck
npm run lint
npm run tauri dev
```

Build the unsigned NSIS installer:

```powershell
npm run tauri -- build --bundles nsis
```

See [`../docs/DESIGN_SYSTEM.md`](../docs/DESIGN_SYSTEM.md) for theme and contrast requirements.
