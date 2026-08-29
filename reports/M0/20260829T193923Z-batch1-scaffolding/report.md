# M0 Batch 1 Verification — Solution & Project Scaffolding

Verified: 2026-08-29T19:39:23Z
Verifier: independent verifier agent (no fixes applied beyond inspection)

## Summary verdict

**Verified — pass.** Build, restore, and test all succeeded with 0 errors;
project structure and reference graph match SPECIFICATION.md §6.3; no
premature Cli project, vendored g3Sharp code, or non-scaffolding logic found.

## Checks

### 1. `global.json` — PASS

- Exists at repo root: [global.json](../../../global.json)
- Content:
  ```json
  { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
  ```
- Installed SDK (`dotnet --version` / `dotnet --list-sdks`): `10.0.111` at
  `/usr/lib/dotnet/sdk`. Same major.minor.feature-band (10.0.1xx) as the
  pinned `10.0.100`, and `rollForward: latestFeature` permits resolving up to
  `10.0.111` within that band. Confirmed resolved correctly — `dotnet build`
  ran without an SDK-not-found error.

### 2. `Meshwright.sln` project list — PASS

All expected projects present, in expected solution folders (`src`, `tests`):

- Meshwright.Geometry
- Meshwright.IO
- Meshwright.Core
- Meshwright.Rendering
- Meshwright.App
- Meshwright.Tests

`Meshwright.Cli` is **not** present in the .sln or on disk — correctly
deferred per AGENTS.md (v1.x, not v1.0).

### 3. Project reference graph vs SPECIFICATION.md §6.3 — PASS

Read each `.csproj`'s `<ProjectReference>` items directly:

| Project | References found | Expected | Match |
|---|---|---|---|
| Meshwright.Geometry | (none) | none | ✅ |
| Meshwright.IO | Geometry | Geometry | ✅ |
| Meshwright.Core | Geometry, IO | Geometry+IO | ✅ |
| Meshwright.Rendering | Geometry | Geometry only | ✅ |
| Meshwright.App | Core, IO, Rendering | Core+IO+Rendering | ✅ |
| Meshwright.Tests | Geometry, IO, Core, Rendering | Geometry+IO+Core+Rendering | ✅ |

No cross-references from Geometry into IO/Core/Rendering/App — boundary
intact.

### 4. `dotnet restore` / `dotnet build` / `dotnet test` from repo root — PASS

- `dotnet restore`: exit 0, "All projects are up-to-date for restore."
  Full log: [mw_restore.log](./mw_restore.log)
- `dotnet build`: exit 0, all 6 projects built, **0 Warnings, 0 Errors**.
  Full log: [mw_build.log](./mw_build.log)
- `dotnet test`: exit 0, `Meshwright.Tests.dll` ran — **Failed: 0, Passed: 1,
  Skipped: 0, Total: 1**.
  Full log: [mw_test.log](./mw_test.log)

Only test present is the scaffold smoke test
[SolutionScaffoldTests.cs](../../../tests/Meshwright.Tests/SolutionScaffoldTests.cs)
(`Assert.True(true)`), consistent with a scaffolding-only batch.

### 5. No premature g3Sharp vendoring / rendering / STL parsing logic — PASS

- `src/Meshwright.Geometry/Vendor/g3/` contains only `VENDOR.md`, whose full
  content is: *"No g3Sharp types vendored yet — see SPECIFICATION.md §6.2."*
  No `.cs` files vendored.
- `src/Meshwright.Geometry/` has no other source files besides the `.csproj`
  and the empty `Vendor/` tree.
- `src/Meshwright.Rendering/Meshwright.Rendering.csproj` only declares the
  `Silk.NET.OpenGL` / `Silk.NET.Maths` package references — no renderer,
  camera, gizmo, or picking source files exist yet.
- `src/Meshwright.IO/Stl/` and `src/Meshwright.IO/Obj/` each contain only a
  `.gitkeep` placeholder — no STL/OBJ reader/writer code.
- `src/Meshwright.App/` contains only the standard Avalonia template files
  (`App.axaml(.cs)`, `MainWindow.axaml(.cs)`, `Program.cs`) — no custom
  viewport/view-model logic added.

### 6. `.gitignore` covers build artifacts — PASS

[.gitignore](../../../.gitignore) includes `bin/`, `obj/`, `*.user`, `*.suo`,
`artifacts/`, `publish/`, `[Dd]ebug/`, `[Rr]elease/`, plus editor/OS noise and
project-specific `/testdata/local/` and `/reports/build-hook/`.

## Unverifiable / out of scope for this batch

- No UI is rendered by this batch (App is the stock Avalonia template with no
  custom views), so no headless screenshot evidence was captured — nothing
  UI-facing exists yet to verify visually.

## Raw logs

- [mw_restore.log](./mw_restore.log)
- [mw_build.log](./mw_build.log)
- [mw_test.log](./mw_test.log)
