# M1 (Inspect) — Milestone Summary

**Status: Complete.** All 4 batches verified.

## Goal

Per [SPECIFICATION.md](../../SPECIFICATION.md) §7 M1:

> Full mesh analysis and error highlighting. Shippable alone as a free "why
> won't this print?" tool — and a cheap way to find the first users.

## What was built

1. **Batch 1 — g3Sharp vendoring foundation.** Replaced the handwritten
   `TriangleMesh` stopgap with a genuine vendored g3Sharp tree
   (`src/Meshwright.Geometry/Vendor/g3/`, 92 files mirroring upstream layout:
   `DMesh3`, `MeshNormals`, `MeshConnectedComponents`, `MeshBoundaryLoops`,
   `DMeshAABBTree3`, and their dependencies), with `VENDOR.md` recording
   upstream commit and trims.
   - First attempt failed verification (handwritten merged subset with
     stubbed APIs, weak test coverage): superseded
     [20260829T210452Z-batch1-foundation/report.md](20260829T210452Z-batch1-foundation/report.md)
     (**FAILED**, superseded).
   - Re-check confirmed the corrected vendor tree passed foundation
     compliance:
     [20260831T075303Z-batch1-foundation-recheck/report.md](20260831T075303Z-batch1-foundation-recheck/report.md)
     (**PASS**, supersedes the failed report above).

2. **Batch 2 — detector suite.** The 7 mesh-quality detectors
   (`NonManifoldDetector`, `BoundaryHoleDetector`, `SelfIntersectionDetector`,
   `InvertedNormalDetector`, `DegenerateTriangleDetector`,
   `DuplicateVertexDetector`, `DisconnectedShellDetector`) plus the shared
   contract (`IMeshDetector`, `MeshIssue`, `MeshIssueSeverity`,
   `MeshStatistics`, `MeshDiagnosticsReport`, `MeshDiagnosticsRunner`) in
   `src/Meshwright.Geometry/Diagnostics/`, built on real vendored `g3` APIs
   with positive/negative test fixtures for each.
   Report: [20260831T081747Z-batch2-detectors/report.md](20260831T081747Z-batch2-detectors/report.md)

3. **Batch 3 — presentation/integration.** `MeshDocument` wired to all 7
   detectors via `MeshDiagnosticsRunner`; `MainWindow` diagnostics panel
   (statistics, plain-language summary sentence, per-issue list) on both the
   startup sample-mesh path and the "Open STL..." path; GL highlighting
   (`MeshRenderer`/`VertexDataBuilder`) rendering flagged triangles/edges in
   distinct colors while leaving clean meshes unchanged; GPU pixel-diff tests
   proving the highlighting is real.
   Report: [20260831T083243Z-batch3-presentation/report.md](20260831T083243Z-batch3-presentation/report.md)

4. **Batch 4 — real broken-mesh acceptance gate.** Independently verified
   `BrokenSample.stl` (a cube missing one face, a separate floating shell, one
   flipped-winding face) flows through the real load pipeline (`StlReader` →
   `MeshDocument` → all 7 detectors) and produces correct diagnostics text and
   visually distinct GL highlighting, captured to PNG and visually inspected.
   Report: [20260831T084136Z-batch4-acceptance/report.md](20260831T084136Z-batch4-acceptance/report.md)

## Test results

Final authoritative count, from the batch 4 acceptance report: **75 total
tests, 0 failed, 0 skipped** (`Meshwright.Tests.Gpu`: 6/6 passed;
`Meshwright.Tests`: 69/69 passed). `dotnet build Meshwright.sln`: 0 warnings,
0 errors.

## Known gaps / deferred issues

- **Boost licence header discrepancy — resolved.** The batch 1 recheck found
  zero per-file Boost Software License banners across the 92 vendored `g3`
  files. This was closed in a follow-up fix after that report was written:
  every vendored file now carries a per-file Boost Software License 1.0
  header (confirmed present in the current source), and `VENDOR.md`
  documents this as an addition beyond upstream's repo-root-only licensing.
  Not a gap in the final milestone state.
- **Build-hook JSON still invalid.** `.github/hooks/build-on-edit.json` is
  still entirely wrapped in `//` comments, making it invalid JSON and
  disabling the post-edit build hook. Carried forward unresolved across all
  four M1 batches; unrelated to M1 mesh-inspection scope.
- **No app/core wiring at the time of batch 2.** As of batch 2, nothing in
  `Meshwright.App`/`Meshwright.Core` yet constructed the 7 detectors — this
  was resolved by batch 3's `MeshDocument`/`MainWindow` wiring and is not a
  gap in the final milestone state.

## Next milestone

**M2 — Repair** (Auto Repair plus the individual repair operations, undo
stack, export), per §7.
