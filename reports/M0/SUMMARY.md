# M0 (Skeleton) — Milestone Summary

**Status: Complete.** All 4 batches verified.

## Goal

Per [SPECIFICATION.md](../../SPECIFICATION.md) §7 M0:

> Solution structure, Avalonia window, Silk.NET viewport rendering a loaded
> STL with orbit/pan/zoom. Proves the hardest integration risk first.

## What was built

1. **Batch 1 — solution scaffolding.** `global.json` pinning the net10.0 SDK
   feature band, `Meshwright.sln`, and five projects
   (`Meshwright.Geometry`, `Meshwright.IO`, `Meshwright.Core`,
   `Meshwright.Rendering`, `Meshwright.App`) matching the architecture in
   §6.3, plus the `Meshwright.Tests` xUnit project.
   Report: [batch1-scaffolding/report.md](20260829T193923Z-batch1-scaffolding/report.md)

2. **Batch 2 — STL reader and camera math.** Binary + ASCII STL
   autodetection in `Meshwright.IO`, a minimal `TriangleMesh` stopgap type in
   `Meshwright.Geometry` (ahead of full `DMesh3` vendoring), and `OrbitCamera`
   orbit/pan/zoom math in `Meshwright.Rendering`.
   Report: [batch2-stl-camera/report.md](20260829T194622Z-batch2-stl-camera/report.md)

3. **Batch 3 — GL renderer.** Silk.NET OpenGL `MeshRenderer` (flat-shaded
   Lambertian shader, VAO/VBO upload) and a pure `VertexDataBuilder` helper in
   `Meshwright.Rendering`.
   Report: [batch3-gl-renderer/report.md](20260829T195049Z-batch3-gl-renderer/report.md)

4. **Batch 4 — full integration.** `MeshViewportControl` (Avalonia
   `OpenGlControlBase` bound to a Silk.NET GL context), `MainWindow` wiring
   (loads an embedded sample STL on startup, "Open STL..." file picker), and
   pointer/scroll input mapped to orbit/pan/zoom.
   Report: [batch4-integration/report.md](20260829T200248Z-batch4-integration/report.md)

## Test results

`dotnet test`: **22/22 passing**, `dotnet build` clean, across all 4 batches
— nothing regressed batch-over-batch.

## Known gaps / deferred issues

- Actual OpenGL pixel rendering (triangle draw, orbit/pan/zoom visual
  correctness) could not be verified on this dev host — no GPU/display
  server available. Documented in the [batch 4 report](20260829T200248Z-batch4-integration/report.md).
  This should be manually smoke-tested on a machine with a GPU before
  relying on it further.
- g3Sharp vendoring, the full `DMesh3`-based mesh structure, OBJ import, and
  any M1+ features are explicitly out of scope and untouched in M0.

## Next milestone

**M1 — Inspect** (mesh statistics + error detection), per §7.
