# M1 Batch 3 — Presentation/Integration Verification

**Date (UTC):** 2026-08-31T08:32:43Z
**Scope:** `MeshDocument` → all 7 detectors, `MainWindow` diagnostics panel (both
load paths), `MeshRenderer`/`VertexDataBuilder` GL highlighting, GPU pixel-diff
tests, architecture boundaries (AGENTS.md, SPECIFICATION.md §4/§5.1/§6.3).

**Verdict: PASS**, with one mandatory follow-up (see "Still required" below)
before M1 can be declared done.

## 1. Build

Ran `dotnet build Meshwright.sln` directly (not trusting prior reports).

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Full log: [build.log](build.log)

## 2. Tests — exact counts

Ran `dotnet test Meshwright.sln` directly.

```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5  - Meshwright.Tests.Gpu.dll (net10.0)
Passed!  - Failed: 0, Passed: 68, Skipped: 0, Total: 68 - Meshwright.Tests.dll (net10.0)
```

**73 total (68 + 5)**, matching the expected count. All 5 GPU tests **ran and
passed** (Skipped: 0) — a real GL context was available on this host, so the
GPU pixel-diff evidence below is not a vacuously-skipped no-op.

Full log: [test.log](test.log)

## 3. MeshDocument — all 7 real detectors, no stubbing

[MeshDocument.cs](../../../src/Meshwright.Core/MeshDocument.cs) has a static
`Detectors` list wired to `MeshDiagnosticsRunner.Run`:

```
NonManifoldDetector, BoundaryHoleDetector, SelfIntersectionDetector,
InvertedNormalDetector, DegenerateTriangleDetector, DuplicateVertexDetector,
DisconnectedShellDetector
```

This is exactly the 7 categories listed in §5.1's "Inspect" bullet list. Each
detector is a real, independently-tested class in `Meshwright.Geometry/Diagnostics/`
(confirmed by reading `NonManifoldDetector.cs`, `SelfIntersectionDetector.cs`,
`DegenerateTriangleDetector.cs`, `InvertedNormalDetector.cs`,
`DisconnectedShellDetector.cs`, `BoundaryHoleDetector.cs` — none are stubs;
each emits real `MeshIssue` records with populated `TriangleIds`/`EdgeIds`).
`MeshDocumentTests.cs` has a test
(`Load_MeshWithNonManifoldEdge_ReportsIssueViaAllSevenDetectors`) asserting
the report reflects the full detector set, not a hardcoded subset. **Confirmed
genuine.**

## 4. MainWindow — statistics, plain-language Summary, per-issue list, both load paths

Both `LoadSampleMesh()` (startup) and `OnOpenFileClick()` ("Open STL...") funnel
through the single `ApplyLoadedMesh()` → `UpdateDiagnosticsPanel()` path in
[MainWindow.axaml.cs](../../../src/Meshwright.App/MainWindow.axaml.cs). No
divergent/duplicated logic between the two entry points — same statistics,
same `Summary`, same issues list populate regardless of which path loaded the
mesh.

- **Statistics**: `StatisticsText` — triangle count, vertex count, shell
  count, volume, surface area, bounding box dimensions. Matches §5.1's
  "Mesh statistics: triangle count, volume, surface area, bounding box, shell
  count" bullet exactly.
- **Summary sentence**: `SummaryText.Text = report.Summary`. Checked
  [MeshDiagnosticsReport.cs](../../../src/Meshwright.Geometry/Diagnostics/MeshDiagnosticsReport.cs)'s
  `Summary` property — it groups issues by category and phrases them via a
  `CategoryPhrases` lookup covering all 7 detector categories, with correct
  singular/plural handling. `MeshDiagnosticsReportTests.cs` asserts the output
  is **`"3 holes, 1 stray shell, 14 flipped faces found."`** — this is a
  near-verbatim match of §4/§5.1's own example wording
  (`"3 holes, 1 stray shell (0.02 % of volume), 14 flipped faces"`), not a
  generic/robotic "Errors: 18" style message. Genuinely plain-language.
- **Per-issue list**: `IssuesList.ItemsSource` bound to
  `report.Issues.Select(issue => $"[{issue.Severity}] {issue.Category}: {issue.Message}")`
  — a real per-issue breakdown, not just the summary sentence alone.
- **XAML** (`MainWindow.axaml`) confirms `StatisticsText`, `SummaryText`, and
  `IssuesList` are all present and bound in the right-hand diagnostics panel.

**Confirmed genuine on both load paths.**

## 5. GL highlighting — MeshRenderer / VertexDataBuilder

Read the actual shader source and upload logic in
[MeshRenderer.cs](../../../src/Meshwright.Rendering/GL/MeshRenderer.cs) and
[VertexDataBuilder.cs](../../../src/Meshwright.Rendering/GL/VertexDataBuilder.cs),
not just confirming they compile:

- Vertex shader passes a per-vertex `aHighlight` float attribute through to
  the fragment shader; fragment shader does
  `mix(uBaseColor, uHighlightColor, vHighlight)` — a real blend, not a no-op.
- `VertexDataBuilder.BuildTriangleHighlightFlags` sets the flag to `1f` only
  for triangle ids present in `flaggedTriangleIds`, defaulting to `0f`
  otherwise — when no ids are flagged (the empty-collection default), every
  vertex gets `0f` and `mix(...)` reduces to `uBaseColor`, i.e. **identical
  output to the pre-diagnostics/clean-mesh path**. Confirmed: a clean mesh
  renders unchanged.
- Flagged edges get a second VAO/VBO and a separate line-shader pass
  (`EdgeHighlightColor`, `DrawArrays(PrimitiveType.Lines, ...)`), only
  executed when `_edgeVertexCount > 0`.
- [MeshViewportControl.cs](../../../src/Meshwright.App/Views/MeshViewportControl.cs)'s
  `UploadCurrentMesh` genuinely collects `flaggedTriangleIds`/`flaggedEdges`
  from `_report.Issues[*].TriangleIds`/`.EdgeIds` (real detector output, not
  hardcoded test data) and passes them into `UploadMesh`. Re-uploads on both
  mesh change and `Report` setter (`_highlightsDirty` flag), so switching
  meshes via either load path refreshes highlighting.

**Confirmed genuine — not stubbed, and clean meshes are unaffected.**

## 6. GPU pixel-diff tests — real proof, not trivially-passing

Read [MeshRendererGpuTests.cs](../../../tests/Meshwright.Tests.Gpu/MeshRendererGpuTests.cs)
and [TriangleMeshFixtures.cs](../../../tests/Meshwright.Tests.Gpu/TriangleMeshFixtures.cs):

- `UploadMesh_WithFlaggedTriangle_ChangesRenderedPixelsVersusUnflagged` and
  `UploadMesh_WithFlaggedEdge_ChangesRenderedPixelsVersusUnflagged` render a
  cube once unflagged and once with triangle id `2` (or one of its edges)
  flagged, then `Assert.False(unflaggedFrame.AsSpan().SequenceEqual(flaggedFrame))`
  — an actual full-framebuffer byte comparison via `glReadPixels`, not a
  method-was-called assertion.
- **Sanity-checked the geometry claim**: in `TriangleMeshFixtures.BuildCube()`,
  the `faces` array's index 2 entry (`(5, 4, 7)`) is commented "front", and
  vertices 4–7 are the corners with `z = +1` — genuinely the front (+z) face.
  The camera is framed on the cube's center via `camera.Frame(...)` with the
  `OrbitCamera`'s default yaw/pitch (not reset to look at -z), so triangle 2
  is plausibly facing the camera and would actually show flagged pixels
  rather than being occluded. Triangle 0 (`(0,1,2)`, the -z back face) is
  correctly avoided for this reason per the test's own comment. This is a
  real, geometry-aware test, not a coincidentally-passing diff.
- All 5 GPU tests ran (not skipped) and passed in this environment, confirming
  a real GL context/driver executed them — see §2.

**Confirmed genuine.**

## 7. Architecture boundaries (§6.3, AGENTS.md)

- `Meshwright.Geometry.csproj` has **zero `ProjectReference`s** — confirmed no
  UI/rendering/IO coupling.
- `Meshwright.Core.csproj` references only `Meshwright.Geometry` and
  `Meshwright.IO`. `MeshDocument.cs` is the only source file in
  `Meshwright.Core` (plus the csproj) — a thin mesh+report holder, no
  `IMeshOperation`, no undo stack, no settings scope creep from M2/M1.x.
  Grep for `IMeshOperation|undo|Undo` in `src/Meshwright.Core/` returned no
  matches.

**Boundaries respected.**

## 8. Gaps / still required

All automated checks above pass, and this batch genuinely delivers the
"plain-language report plus visual highlighting in the existing viewport"
mechanics end-to-end in code. **However, per the milestone's own explicit
requirement, this is not sufficient to declare M1 done.** Automated unit/GPU
tests here use synthetic fixtures (a hand-built tetrahedron, a hand-built
cube) — none of them load an actual STL file containing multiple real-world
defects (holes, non-manifold edges, self-intersections, flipped normals,
duplicate vertices, stray shells) through the full `Meshwright.App` UI and
visually confirm the diagnostics panel text and viewport highlighting look
correct together, interactively. **A manual smoke test against a real broken
mesh file, run through the actual Avalonia app (`dotnet run --project
src/Meshwright.App`, "Open STL..." with a known-bad file), is still required
before M1 can be declared complete.** This report does not perform that step
— it was out of scope for automated verification and needs a human (or a
follow-up agent with a real display/interactive session) to do it.

## Summary

| Check | Result |
| --- | --- |
| Build | PASS |
| Tests (73 total: 68 + 5) | PASS |
| MeshDocument — 7 real detectors | PASS |
| MainWindow — stats/Summary/issues, both load paths | PASS |
| GL highlighting (shader + upload logic) | PASS |
| Clean mesh renders unchanged | PASS |
| GPU pixel-diff tests — real, geometry-sanity-checked | PASS |
| Architecture boundaries | PASS |
| Manual smoke test vs. real broken mesh | **NOT DONE — required before M1 sign-off** |
