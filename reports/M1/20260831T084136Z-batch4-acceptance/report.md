# M1 Batch 4 — Real Broken-Mesh Acceptance Gate (Verification)

**Date (UTC):** 2026-08-31T08:41:36Z
**Scope:** The milestone's explicit "run it and confirm ... on a real broken
mesh, not only on unit-test fixtures" requirement. This batch is the
acceptance gate for the whole M1 milestone.

**Verdict: VERIFIED — PASS.** M1 is demonstrably working end-to-end on a real
broken mesh (diagnostics report text + viewport highlighting), independent of
prior batches' self-reports.

## 1. `BrokenSample.stl` — confirmed real, valid binary STL

Parsed the file manually (Python, `struct`, independent of `StlReader`) rather
than trusting the fixture's name or the reader's own success:

- Header text: `BrokenSample: cube missing a face, one f...` (80-byte binary
  STL header, human-readable description embedded by the author).
- Triangle count field: **14**. File size: 784 bytes = `80 + 4 + 14*50`
  exactly — no truncation, no trailing garbage.
- Deduplicating vertices (12 unique) and building connected components by
  shared-vertex adjacency gives **2 components**: one with 8 vertices / 10
  faces (a cube missing 2 of its 12 triangles — i.e. one quad face) and one
  with 4 vertices / 4 faces (a separate small floating tetrahedron shell).
- Boundary-edge count (edges used by exactly one triangle): **4**, matching
  the outline of exactly one missing quad face on the cube — confirms the
  "hole" claim geometrically, not just by test assertion.
- Directed-edge winding check found **3 duplicate-direction edges, all
  belonging to one triangle** in the 10-face cube component — confirms a
  single flipped-winding face among the cube's triangles, distinct from the
  floating shell.

This independently confirms the fixture is exactly what the batch claims: a
cube-like solid missing one face, a separate floating shell, and a flipped
face among the rest — not an empty, corrupt, or mislabeled file.

## 2. Avalonia-headless integration test — real pipeline, not a shortcut

Read [BrokenSampleIntegrationTests.cs](../../../tests/Meshwright.Tests/BrokenSampleIntegrationTests.cs)
and [MainWindow.axaml.cs](../../../src/Meshwright.App/MainWindow.axaml.cs):

- The test constructs a real `MainWindow` and calls `LoadFileForTesting(path)`,
  which does `File.OpenRead` → `StlReader.Read` → `ApplyLoadedMesh`, the same
  private method used by both the real file-picker path (`OnOpenFileClick`)
  and the startup sample-mesh path. `LoadFileForTesting` exists solely to
  substitute for the OS file picker, which can't be driven headlessly — it
  does not bypass `MeshDocument`, detectors, or the UI update path.
- `ApplyLoadedMesh` calls `_document.Load(mesh)`, then pushes `_document.Report`
  into `Viewport.Report` and the diagnostics panel (`StatisticsText`,
  `SummaryText`, `IssuesList`) via `UpdateDiagnosticsPanel` — the real UI
  update path, not a test-only stub.
- `CurrentReport`, `StatusMessage`, `SummaryMessage` are read-only properties
  exposing already-live `TextBlock.Text` bindings for assertion, not
  test-injected values.
- Ran it in isolation:

  ```
  Passed Meshwright.Tests.BrokenSampleIntegrationTests.LoadingBrokenSample_ThroughRealPipeline_FlagsHoleShellAndInvertedNormal [892 ms]
  ```

  Confirms `BoundaryHole`, `DisconnectedShell`, and `InvertedNormal` issue
  categories are all present in the real report, and both `StatusMessage` and
  `SummaryMessage` are non-blank and mention "hole" and "shell".

## 3. GPU render test — genuine visual highlighting, re-run and visually inspected

Read [BrokenSampleRenderGpuTests.cs](../../../tests/Meshwright.Tests.Gpu/BrokenSampleRenderGpuTests.cs),
[GpuTestFixture.cs](../../../tests/Meshwright.Tests.Gpu/GpuTestFixture.cs), and
[PngWriter.cs](../../../tests/Meshwright.Tests.Gpu/PngWriter.cs):

- `GpuTestFixture` creates a real (hidden) GLFW window and OpenGL 3.3 core
  context via Silk.NET — not a mock/software rasterizer stub. `IsAvailable`
  gates the test via `SkippableFact`; on this host it was **available**, so
  the test did not vacuously skip.
- The test loads `BrokenSample.stl` via the real `StlReader`, runs it through
  a real `MeshDocument`, extracts flagged triangle IDs/edges from the real
  `MeshDiagnosticsReport.Issues`, and feeds them into the real
  `MeshRenderer.UploadMesh`/`Render` — the same class the app's
  `MeshViewportControl` uses, not a test-only renderer.
- Re-ran it standalone (not just as part of the full suite) to confirm it
  executes for real rather than being skipped in aggregate counts:

  ```
  Passed Meshwright.Tests.Gpu.BrokenSampleRenderGpuTests.RenderingBrokenSample_HighlightsFlaggedTrianglesAndCapturesPng [289 ms]
  ```

- Regenerated the PNG evidence from this run
  (`/tmp/meshwright-gpu-evidence/BrokenSample-highlighted.png`, produced by
  `PngWriter.WriteRgba` — a dependency-free, self-inspected encoder, correctly
  flipping glReadPixels' bottom-up rows to top-down) and copied it into this
  report folder:
  - [BrokenSample-highlighted.png](BrokenSample-highlighted.png) — raw 64x64
    capture as written by the test.
  - [BrokenSample-highlighted-8x.png](BrokenSample-highlighted-8x.png) —
    nearest-neighbor 8x upscale for legibility, no interpolation/color change.
- **Viewed the image directly** (not just trusting the test's pixel-count
  assertions): the rendered cube shows a **red highlight** patch
  (`HighlightColor` = (0.95, 0.15, 0.1), used for flagged triangles — the
  flipped face and shell triangles) and a **yellow/gold wireframe outline**
  (`EdgeHighlightColor` = (1, 0.85, 0.1), used for flagged boundary edges —
  the hole outline), both clearly distinct in hue from the neutral dark-gray
  base-shaded surface (`BaseColor` = (0.7, 0.7, 0.75) dimmed by diffuse
  lighting) and the dark background clear color. This is genuine, visually
  confirmable highlighting, not an assertion-only pass.

## 4. Full build and test — exact counts

Ran directly, not trusting prior reports:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
Full log: [build.log](build.log)

```
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 520 ms - Meshwright.Tests.Gpu.dll (net10.0)
Passed!  - Failed:     0, Passed:    69, Skipped:     0, Total:    69, Duration: 2 s - Meshwright.Tests.dll (net10.0)
```

**75 total (69 + 6)** — matches the expected count exactly. Zero skips: the
GPU suite's real-hardware-dependent tests, including the BrokenSample render
test, genuinely executed on this host rather than being silently skipped.

Full log: [test.log](test.log)

## Overall verdict for the milestone lead

**M1 is demonstrably working end-to-end on a real broken mesh, not only on
unit-test fixtures.** A genuine, independently-parsed binary STL (cube missing
one face, a separate floating shell, one flipped-winding face) flows through
the real load pipeline (`StlReader` → `MeshDocument` → all 7 detectors) and
produces:

1. A diagnostics report and summary/status text correctly naming the hole,
   shell, and inverted-normal issues (Avalonia-headless integration test,
   re-run and passing).
2. Visually distinct highlight rendering in the real GL viewport pipeline —
   red flagged-triangle fill and yellow flagged-edge wireframe against neutral
   base shading — captured to PNG and visually inspected in this report, not
   merely asserted by pixel-count.

No blockers found. Nothing was modified in `src/` or `tests/`; only this
report and its evidence PNGs were added under `reports/M1/`.
