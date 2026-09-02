# Verification: M1 batch 2 — detector suite

**Date (UTC):** 2026-08-31T08:17:47Z

**Scope:** the 7 mesh-quality detectors (`NonManifoldDetector`,
`BoundaryHoleDetector`, `SelfIntersectionDetector`, `InvertedNormalDetector`,
`DegenerateTriangleDetector`, `DuplicateVertexDetector`,
`DisconnectedShellDetector`) plus the shared contract
(`IMeshDetector`, `MeshIssue`, `MeshIssueSeverity`, `MeshStatistics`,
`MeshDiagnosticsReport`, `MeshDiagnosticsRunner`) in
`src/Meshwright.Geometry/Diagnostics/`, building on the already-PASSED
foundation batch at
[20260831T075303Z-batch1-foundation-recheck/report.md](../20260831T075303Z-batch1-foundation-recheck/report.md).
Baseline: same uncommitted worktree on top of commit `b25e014`
(`git status --short` shows the diagnostics tree entirely untracked, plus the
already-verified vendor-tree changes from batch 1).

**Verifier:** independent check. Only directly observed source and command
output is recorded below; nothing here restates the worker's own claims
without being re-checked.

## Verdict: PASS

All 7 detectors exist, implement `IMeshDetector` against real vendored
`g3` APIs (no handwritten stub types, no production `TriangleMesh`
references), and have xUnit tests with both a positive (issue flagged) and a
negative (clean mesh, zero false positives) fixture. `MeshDiagnosticsRunner`
aggregates statistics + issues from a caller-supplied detector list without
hardcoding which detectors exist. Build is clean; test run is 67/67 passing,
matching the expected count. Manual geometric re-derivation of the
`SelfIntersectionDetector` and `InvertedNormalDetector` fixtures (the two
flagged as most algorithmically subtle) confirms the test geometry genuinely
produces/avoids the claimed issue — see the "Manual algorithm checks" section
below.

## Checks

| Check | Result | Direct observation |
| --- | --- | --- |
| `dotnet build Meshwright.sln` | PASS | 0 warnings, 0 errors, all 7 projects built, exit code 0. Raw output: [build.log](build.log). |
| `dotnet test Meshwright.sln` | PASS | `Meshwright.Tests.Gpu`: 3/3 passed. `Meshwright.Tests`: 64/64 passed. **Total 67/67**, 0 skipped, 0 failed — matches the expected ~67. Exit code 0. Raw output: [test.log](test.log). |
| All 7 detector files exist | PASS | `src/Meshwright.Geometry/Diagnostics/` contains `NonManifoldDetector.cs`, `BoundaryHoleDetector.cs`, `SelfIntersectionDetector.cs`, `InvertedNormalDetector.cs`, `DegenerateTriangleDetector.cs`, `DuplicateVertexDetector.cs`, `DisconnectedShellDetector.cs`, each `sealed class ... : IMeshDetector`. |
| Detectors use real vendored g3 APIs, not fabricated stubs | PASS | `grep -rln "class MeshQueries\|class IntrTriangle3Triangle3\|orient_tri_edge_and_find_other_vtx" src/Meshwright.Geometry/Vendor/g3` resolves to real vendored files: `queries/MeshQueries.cs`, `intersection/IntrTriangle3Triangle3.cs`, `math/IndexUtil.cs` — the exact symbols `SelfIntersectionDetector` and `InvertedNormalDetector` call are genuine vendored implementations, not something invented in the batch. `BoundaryHoleDetector` uses `MeshBoundaryLoops`/`EdgeLoop` (vendored, confirmed present in batch 1's 92-file tree); `DisconnectedShellDetector`/`MeshStatistics` use `MeshConnectedComponents` (same). |
| No handwritten stub APIs / no `NotImplementedException`/TODO placeholders | PASS | `grep -RInE "NotImplementedException|TODO|stub|placeholder|throw new Exception" src/Meshwright.Geometry/Diagnostics` returned zero matches. |
| No production `TriangleMesh` references | PASS | `grep -rn "TriangleMesh" src/Meshwright.Geometry/Diagnostics tests/Meshwright.Tests/Diagnostics` returned zero matches; all detectors and tests operate on `g3.DMesh3` directly. |
| Each detector has a test file with positive + negative fixtures | PASS | Confirmed by direct reading of all 10 files under `tests/Meshwright.Tests/Diagnostics/`: every detector test class has at least one "clean mesh → `Assert.Empty(issues)`" case and at least one "known-bad mesh → issue(s) asserted with correct `Category`/`Severity`/implicated ids/message" case (e.g. `DegenerateTriangleDetectorTests` additionally has a mixed clean+dirty mesh asserting the good triangle is never flagged; `DisconnectedShellDetectorTests` has a dedicated "never flags the large shell" test). None of the assertions are tautological (`Assert.NotNull` on results, etc.) — every positive case checks category, severity, and the specific triangle/vertex/edge ids implicated. |
| `MeshDiagnosticsRunner` aggregates without hardcoding detector list | PASS | `MeshDiagnosticsRunner.Run(DMesh3, IEnumerable<IMeshDetector>)` takes the detector list as a parameter and only calls `MeshStatistics.Compute` + `detector.Detect` per supplied detector; it contains no reference to any of the 7 concrete detector class names. `MeshDiagnosticsRunnerTests` proves this using a `FakeDetector` test double (not one of the 7 real detectors), confirming the runner is generic over `IMeshDetector` rather than coupled to specific implementations. |
| §5.1 coverage | PASS | Spec requires highlighting non-manifold edges/vertices, boundary holes, self-intersections, inverted/inconsistent normals, degenerate/zero-area triangles, duplicate vertices, and disconnected shells/stray debris — all 7 are present as distinct detectors with matching `Category` names. |
| §6.3 architecture boundary | PASS | All diagnostics code lives in `Meshwright.Geometry/Diagnostics/`, references only `g3` (in-tree vendor) and BCL types — no UI or I/O references, consistent with `Meshwright.Geometry` being UI-agnostic. |

## Gap noted (not a blocker for this batch's stated scope)

`grep -rn` for the 7 detector class names under `src/Meshwright.App` and
`src/Meshwright.Core` returned **zero matches** — nothing in the app/core
layer currently constructs or wires these detectors into a
`MeshDiagnosticsRunner.Run(...)` call, so there is no UI-facing "Inspect"
feature yet (§5.1's "visual highlight" and "plain-language report" in the
viewport). This is expected/acceptable if this batch's scope was the
Geometry-layer detector suite only and UI wiring is a separate, later batch —
but it means M1 is not yet demonstrable end-to-end in the app. Flagging so it
isn't silently dropped from the milestone's tracking; not treated as a defect
in the batch under test since the user's scope for this verification pass was
explicitly the 7 detectors + shared contract.

## Manual algorithm checks (geometric re-derivation, not just "tests pass")

### `SelfIntersectionDetector`

- **Negative — tetrahedron (`Detect_Tetrahedron_ReportsNoIssues`):** every pair
  of the 4 faces of a tetrahedron shares an edge (2 vertices), so
  `SharesVertex` short-circuits every pair before `MeshQueries.TrianglesIntersection`
  is ever called. Directly confirmed by hand: tetrahedron faces
  `(v0,v2,v1)`, `(v0,v1,v3)`, `(v0,v3,v2)`, `(v1,v2,v3)` — any two of these
  four triples share at least 2 of `{v0,v1,v2,v3}`. Correct exclusion.
- **Positive — piercing triangles (`Detect_NonAdjacentTrianglesThatPierceEachOther_ReportsOneIssue`):**
  triangle A = `(-1,-1,0),(1,-1,0),(0,1,0)` lies in the `z=0` plane. Checked
  by hand that the origin `(0,0,0)` is strictly inside A using the
  sign-of-cross-product edge test: edge0→edge1 cross at origin gives
  consistent sign with the third vertex for all three edges (A is a
  reasonably "fat" triangle spanning `x∈[-1,1], y∈[-1,1]` with apex at
  `y=1`, and `(0,0)` sits well inside its interior, not near any edge).
  Triangle B = `(0,0,-1),(0,0,1),(0,2,0)` has an edge from `(0,0,-1)` to
  `(0,0,1)` that crosses the `z=0` plane exactly at `(0,0,0)` — the same
  point. No shared vertex ids between A and B, so `SharesVertex` does not
  exclude the pair, and the geometric intersection claim is correct: the
  fixture genuinely produces an intersection, not a false positive baked
  into the test.
- **Negative — far-apart triangles:** triangle A is near the origin, triangle
  B's vertices are all at coordinates ≥100 in every axis; bounding boxes
  don't even come close to overlapping. Trivially correct.

### `InvertedNormalDetector`

- **Negative — consistent tetrahedron:** re-derived all 4 face normals via
  `(v_b - v_a) × (v_c - v_a)` by hand for `BuildConsistentTetrahedron()`
  (`v0=(0,0,0), v1=(1,0,0), v2=(0,1,0), v3=(0,0,1)`):
  - `(v0,v2,v1)` → normal `(0,0,-1)` (outward, away from centroid
    `(0.25,0.25,0.25)` at the `z=0` face) ✓.
  - `(v0,v1,v3)` → normal `(0,-1,0)` (outward at the `y=0` face) ✓.
  - `(v0,v3,v2)` → normal `(-1,0,0)` (outward at the `x=0` face) ✓.
  - `(v1,v2,v3)` → normal `(1,1,1)` (outward at the slanted face) ✓.
  All four faces are consistently wound outward, so the "no issues" assertion
  is a genuinely valid closed, correctly-oriented mesh, not an accidental
  pass.
- **Positive — one flipped face:** `BuildTetrahedronWithOneFlippedFace()`
  reverses the 4th face to `(v1,v3,v2)`. In a tetrahedron every pair of the 4
  faces shares exactly one edge (6 total shared edges across
  C(4,2)=6 pairs), so the flipped face shares an edge with each of the other
  3 faces and disagrees with all of them — the test's expectation of exactly
  3 flagged issues, each with 2 triangle ids and 1 edge id, is the correct
  count, not an arbitrary number the implementation happened to produce.
  The detector's convention (a consistently-oriented shared edge is
  traversed in *opposite* directions by its two triangles, flagging
  `sameDirection`) is the standard manifold-orientation check and matches
  the hand-derived normals above.

### `DisconnectedShellDetector` (spot-checked for completeness)

- Volume-based "largest shell wins" logic checked against
  `Detect_LargeCubeWithTinyFarTetrahedron_FlagsOnlyTheTinyShell`: cube side
  10 → volume 1000; tetrahedron legs scaled by 0.1 from a unit right-angle
  tetrahedron of volume `1/6` → volume `0.1³/6 ≈ 1.667e-4`. Test's expected
  percentage formula (`tinyVolume / (cubeVolume + tinyVolume) * 100`) matches
  the detector's own `percent = shellVolumes[i] / totalVolume * 100.0`
  computation exactly, and the tiny shell (translated to `(1000,1000,1000)`,
  far from the cube at the origin) is topologically disconnected, so
  `MeshConnectedComponents` will report 2 components. The volume disparity
  (1000 vs ~0.000167) makes misidentifying the "largest" shell impossible;
  this is a real, non-tautological check.

## Commands executed

```text
git status --short
git log --oneline -10
dotnet build Meshwright.sln
dotnet test Meshwright.sln
grep -rn "TriangleMesh" --include=*.cs src/Meshwright.Geometry/Diagnostics tests/Meshwright.Tests/Diagnostics
grep -RInE "NotImplementedException|TODO|stub|placeholder|throw new Exception" src/Meshwright.Geometry/Diagnostics
grep -rn "NonManifoldDetector|BoundaryHoleDetector|SelfIntersectionDetector|InvertedNormalDetector|DegenerateTriangleDetector|DuplicateVertexDetector|DisconnectedShellDetector" --include=*.cs src/Meshwright.App src/Meshwright.Core
grep -rln "class MeshQueries|class IntrTriangle3Triangle3|orient_tri_edge_and_find_other_vtx" src/Meshwright.Geometry/Vendor/g3
```

## Remaining items for a human

1. No app/core wiring yet connects the 7 detectors + `MeshDiagnosticsRunner`
   into the Avalonia UI — §5.1's "visual highlight" and "plain-language
   report" in the viewport is not yet demonstrable end-to-end. Confirm
   whether this is planned for a subsequent M1 batch before declaring the
   milestone complete.
2. Carried forward, unrelated to this batch: build-hook JSON
   (`.github/hooks/build-on-edit.json`) is still invalid/disabled, and the
   Boost-licence-header discrepancy from the batch 1 recheck is still open.

## Raw logs

- [build.log](build.log)
- [test.log](test.log)
