# Verification: real-GPU regression test project (`Meshwright.Tests.Gpu`)

**Date (UTC):** 2026-08-29T20:42:43Z
**Scope:** `tests/Meshwright.Tests.Gpu/` (`GpuTestFixture.cs`, `TriangleMeshFixtures.cs`,
`MeshRendererGpuTests.cs`), `Meshwright.sln`, `tests/Meshwright.Tests/Meshwright.Tests.csproj`.
**Verifier:** independent re-check, nothing here is taken on the worker's word.

## Verdict: verified

All four checks below were directly observed on this machine (real X session / GPU present).

## 1. `dotnet test tests/Meshwright.Tests.Gpu/Meshwright.Tests.Gpu.csproj`

Ran directly, full raw output saved to [gpu-test.log](gpu-test.log).

```
Passed Meshwright.Tests.Gpu.MeshRendererGpuTests.Initialize_CompilesAndLinksShadersOnRealDriver [170 ms]
Passed Meshwright.Tests.Gpu.MeshRendererGpuTests.UploadMesh_SwappingMeshChangesRenderedPixels [124 ms]
Passed Meshwright.Tests.Gpu.MeshRendererGpuTests.Render_AtMaxDistance_MeshIsStillAtLeastPartiallyVisible [39 ms]

Test Run Successful.
Total tests: 3
     Passed: 3
```

**Result: PASS.** All 3 tests genuinely ran and passed — none reported as `Skipped` in the
VSTest output. `GpuTestFixture.IsAvailable` was true on this host (real GLFW window + OpenGL
3.3 core context via Silk.NET.Windowing), so the `Skip.IfNot` guards did not trigger.

## 2. `dotnet test Meshwright.sln` (whole solution)

Ran directly, full raw output saved to [sln-test.log](sln-test.log).

- `Meshwright.Tests.Gpu` dll: `Total tests: 3, Passed: 3` (own isolated VSTest run).
- `Meshwright.Tests` dll: `Total tests: 32, Passed: 32` (own isolated VSTest run) — every
  pre-existing test (`TriangleMeshTests`, `SolutionScaffoldTests`, `VertexDataBuilderTests`,
  `OrbitCameraTests` incl. theory cases, `StlReaderTests`, `MainWindowTests`) still passes
  unchanged.
- Total wall time for the combined solution run: ~15.3s (dominated by the pre-existing
  `MainWindowTests.Constructing_MainWindow_DoesNotThrow` at ~2s and Avalonia/Skia startup, not
  by the new GPU project). The GPU project's own contribution was ~7.5s but ran as a separate,
  parallel VSTest host process, not serialized into the `Meshwright.Tests` run.

**Result: PASS.** `dotnet test Meshwright.sln` does invoke `Meshwright.Tests.Gpu` — it is a
project in the solution, so `dotnet test` on the `.sln` naturally builds/runs it as its own
test project — but the pre-existing `Meshwright.Tests` run remains exactly 32/32 passing, and
`Meshwright.Tests.csproj` was not modified to add a project- or package-level dependency that
would pull GPU code into that assembly (see check 4).

## 3. Code review of `MeshRendererGpuTests.cs` / `GpuTestFixture.cs`

- **Zero Avalonia/Meshwright.App references:** `grep -R "Avalonia" tests/Meshwright.Tests.Gpu`
  returns only two doc-comment mentions ("bypassing Avalonia entirely", "not Avalonia
  [headless]") — no `using Avalonia`, no `ProjectReference` to `Meshwright.App`, no
  `PackageReference` to any `Avalonia.*` package in
  [Meshwright.Tests.Gpu.csproj](../../../../tests/Meshwright.Tests.Gpu/Meshwright.Tests.Gpu.csproj).
  Project references are limited to `Meshwright.Geometry`, `Meshwright.IO`,
  `Meshwright.Rendering`, plus `Silk.NET.Windowing`/`Silk.NET.OpenGL` and xUnit/`SkippableFact`
  packages.
- **Graceful skip on GL failure:** `GpuTestFixture`'s constructor wraps window/context creation
  in `try/catch (Exception ex)`, sets `IsAvailable = false` and `UnavailableReason = ex.Message`
  instead of rethrowing, and disposes any partially-created window. Every test method starts
  with `Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? ...)` (Xunit.SkippableFact),
  which throws a `SkipException` recognized by the `SkippableFact` runner and reported as
  `Skipped`, not `Failed` — confirmed by reading the fixture and all three test bodies; no code
  path reaches GL calls before the skip check.
- **Test B (`UploadMesh_SwappingMeshChangesRenderedPixels`) is non-trivial:** it renders a
  single triangle, reads back the 64x64 RGBA framebuffer into `firstFrame`, then re-uploads a
  genuinely different mesh (a 12-triangle cube) with a re-`Frame()`d camera, reads back a
  *second, independently-rendered* `secondFrame` buffer, and asserts
  `!firstFrame.AsSpan().SequenceEqual(secondFrame)`. This is a diff between two distinct
  render passes with different geometry, not a buffer compared to itself.
- **Test C (`Render_AtMaxDistance_MeshIsStillAtLeastPartiallyVisible`) exercises a real
  far-plane edge case, not a near-default camera:** the test explicitly asserts
  `camera.Distance == camera.MaxDistance` after `Zoom(camera.MaxDistance)`, i.e. the camera is
  pushed to its absolute maximum orbit distance (~200x the mesh's framed radius per the code
  comment), which is far outside any "default" camera position. To keep the mesh from
  shrinking below one pixel at that distance (which would make the test trivially fail/pass
  for the wrong reason), the FOV is narrowed to `π/36` rad as a pure zoom-lens adjustment
  independent of the distance/far-plane relationship under test. The assertion then scans the
  full 64x64xRGBA readback for any pixel deviating from the known clear color by more than a
  tolerance of 2/255 per channel — a real pixel-content check, not a tautology.

**Result: PASS** on all sub-checks.

## 4. Solution/project wiring

- [Meshwright.sln](../../../../Meshwright.sln) declares
  `Project("...") = "Meshwright.Tests.Gpu", "tests\Meshwright.Tests.Gpu\Meshwright.Tests.Gpu.csproj", "{CF7D3533-98AE-445A-A2F5-FE1E9195D449}"`,
  has full `Debug|Release` x `Any CPU|x64|x86` `ActiveCfg`/`Build.0` entries for that GUID, and
  is nested under the `tests` solution folder via `GlobalSection(NestedProjects)` — wired
  identically to the pre-existing `Meshwright.Tests` project.
- [tests/Meshwright.Tests/Meshwright.Tests.csproj](../../../../tests/Meshwright.Tests/Meshwright.Tests.csproj)
  was read in full: its `ProjectReference` list is unchanged (`Meshwright.Geometry`,
  `Meshwright.IO`, `Meshwright.Core`, `Meshwright.Rendering`, `Meshwright.App`) and contains no
  reference to `Meshwright.Tests.Gpu` or any Silk.NET.Windowing/SkippableFact package — the two
  test projects are fully independent, confirming the pre-existing 32-test project was not
  modified to depend on the new one.

**Result: PASS.**

## Unverifiable / out of scope

- No UI-facing surface in this batch (no Avalonia views changed) — no screenshot capture was
  necessary or attempted for this report.
- Behavior on a machine *without* a GPU/X session (the actual skip path firing at
  `IsAvailable == false`) was not exercised here, since this host has a working GPU/X session.
  The skip mechanism itself was verified by code reading only (see check 3); its firing on a
  headless CI box remains unverified by this report.

## Raw logs

- [gpu-test.log](gpu-test.log) — `dotnet test tests/Meshwright.Tests.Gpu/Meshwright.Tests.Gpu.csproj`
- [sln-test.log](sln-test.log) — `dotnet test Meshwright.sln`
