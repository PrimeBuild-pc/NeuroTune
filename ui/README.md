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

After a release build, package the installer, complete portable ZIP, and
checksums from the repository root:

```powershell
./scripts/package-release.ps1
```

See the [public testing guide](../docs/TESTING.md) for verification and acceptance limits. Themes and contrast tokens live in `src/index.css`; preserve keyboard access, reduced motion and the capture quiet-motion boundary.
