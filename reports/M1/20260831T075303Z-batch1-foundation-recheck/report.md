# Verification: M1 batch 1 foundation — re-check

**Date (UTC):** 2026-08-31T07:53:03Z

**Scope:** re-verification of the same uncommitted worktree (baseline commit
`b25e014`) previously verified and **failed** in
[20260829T210452Z-batch1-foundation/report.md](../20260829T210452Z-batch1-foundation/report.md).
This report supersedes that one for the foundation batch.

**Verifier:** independent re-check. Only directly observed source and command
output is recorded below; no claim is restated from the worker or the prior
report without being re-checked here.

## Verdict: PASS (foundation compliance) — with two known, pre-existing, out-of-scope gaps carried forward

The vendor tree has been completely replaced since the prior failed check.
`src/Meshwright.Geometry/Vendor/g3/` is now a 92-file tree that mirrors
upstream geometry3Sharp's own directory layout (`core/`, `math/`, `mesh/`,
`mesh_selection/`, `spatial/`, `queries/`, `distance/`, `intersection/`,
`io/`, `color/`, `curve/`, `mesh_generators/`), not a single handwritten
merged file. The build is green, all 36 tests (3 GPU + 33 unit) pass, no
production `TriangleMesh` type remains, and the previously-flagged weak
normal-expansion test now asserts all six output indices against two
oppositely-wound triangles. The two ancillary issues flagged previously
(invalid build-hook JSON, no viewport visual evidence) are still present and
are recorded below as known gaps, unchanged from the prior report — they are
not blockers for this batch's mesh-foundation scope.

## Checks

| Check | Result | Direct observation |
| --- | --- | --- |
| `dotnet build Meshwright.sln` | PASS | Completed in ~2.9 s, 0 warnings, 0 errors, all 7 projects built. Raw output: [build.log](build.log). |
| `dotnet test Meshwright.sln` | PASS | `Meshwright.Tests.Gpu`: 3/3 passed. `Meshwright.Tests`: 33/33 passed. 0 skipped, 0 failed overall. Raw output: [test.log](test.log). |
| Vendor tree is real upstream source, not a stub | PASS | `find .../Vendor/g3 -type f` lists 92 files under upstream-style subfolders. Key files are full-length implementations, not scaffolds: `mesh/DMesh3.cs` is 2450 lines (`public partial class DMesh3 : IDeformableMesh`), `spatial/DMeshAABBTree.cs` is 2418 lines (`public class DMeshAABBTree3 : ISpatial`, all query methods listed in its doc comment present, not stubbed), `mesh_selection/MeshBoundaryLoops.cs` is 631 lines, `mesh_selection/MeshConnectedComponents.cs` is 255 lines, `mesh/MeshNormals.cs` is 123 lines. All five §6.2-required types (`DMesh3`, `MeshNormals`, `MeshConnectedComponents`, `MeshBoundaryLoops`, `DMeshAABBTree3`) are confirmed present by class name via `grep`. Comment style throughout (`[TODO]`, `[RMS TODO]`, author-initial annotations) is consistent with genuine upstream geometry3Sharp source, not fabricated text. |
| `VENDOR.md` accuracy | PASS | Records upstream repo, commit `ece336493111ffe372a4bfc7fee5026d4127dade`, vendor date, and a table mapping each upstream source file to its vendored type. States "No local scaffolds or substitute implementations are retained," which matches direct inspection. Declares one trim: `math/TransformSequence.cs` omits `Store`/`Restore` binary-serialization members, justified as pulling in an unrelated curve-serialization graph — this is a plausible, narrow trim of unrelated functionality, not of anything the vendored mesh/topology/spatial code needs. |
| Boost licence header presence | **UNVERIFIED / discrepancy noted** | The skill requires keeping an existing per-file Boost header intact. A repo-wide `grep` found the Boost licence text in **zero** of the 92 vendored `.cs` files (only `VENDOR.md`'s prose mentions the licence). This differs from the prior (failed) batch, whose single handwritten `DMesh3.cs` *did* carry a full Boost header banner. I have no network access in this session to diff against the real upstream commit to confirm whether genuine geometry3Sharp source files carry per-file license banners or rely solely on the repository-root `LICENSE` file — I could not verify this claim first-hand and am not asserting either explanation as fact. Recorded as a follow-up item for a human (or a verifier with repo access) to confirm against the actual upstream commit; not treated as disqualifying given the strength of the authenticity evidence above, but not waved through either. |
| No production `TriangleMesh` type | PASS | `src/Meshwright.Geometry/TriangleMesh.cs` now contains only `namespace Meshwright.Geometry;` with no type body. Repo-wide search for `TriangleMesh` outside that file only matches `tests/Meshwright.Tests.Gpu/TriangleMeshFixtures.cs` (a test fixture helper class, not production code) and local variables/types named `g3.DMesh3`/`TwoTriangleMesh()` (a test helper method name, not the old type). |
| No g3Sharp/geometry3Sharp package reference | PASS | `grep` across all `.csproj` files for `g3Sharp`/`geometry3Sharp` found zero matches. |
| `Meshwright.Geometry` has no project/package references | PASS | `Meshwright.Geometry.csproj` contains only an SDK project header and an `AllowUnsafeBlocks` property — no `ProjectReference` or `PackageReference` elements. |
| Architecture boundaries (§6.3) | PASS | `Meshwright.IO` and `Meshwright.Rendering` each reference only `Meshwright.Geometry`; `Meshwright.Core` references `Geometry` + `IO`; `Meshwright.App` references `Core`, `IO`, `Rendering` plus Avalonia/Silk.NET packages. No GPL dependency introduced. |
| Normal-expansion test coverage (previously flagged weak) | PASS | `VertexDataBuilderTests.BuildPerVertexNormals_DuplicatesEachTriangleNormalThreeTimesInOrder` now builds two **oppositely-wound** triangles (`(a,b,c)` and `(d,b,c)`) so their face normals differ (`+Z` vs `-Z`), and separately asserts `result[0]`, `result[1]`, `result[2]` against `(0,0,1)` and `result[3]`, `result[4]`, `result[5]` against `(0,0,-1)`. This exercises both triangles' output positions and proves per-triangle order, unlike the prior version which only asserted indices 3–5. |
| Build-hook JSON validity (previously flagged, unrelated to foundation) | **STILL FAILING — known gap, not this batch's blocker** | `.github/hooks/build-on-edit.json` is still entirely wrapped in `//` line comments, making it invalid JSON and disabling the post-edit build hook. Unchanged from the prior report. Recorded here so it isn't dropped from the record, per instructions; still out of scope for mesh-foundation compliance. |
| Viewport visual evidence (previously flagged, unrelated to this recheck's mandate) | **STILL MISSING — known gap** | No Avalonia Headless PNG/frame capture exists for this batch in this session either; this recheck did not attempt to add one (out of the "vendoring + build/test + no TriangleMesh" scope given for this recheck). Renderer changes (`MeshRenderer.cs`, `VertexDataBuilder.cs`) are exercised only by the real-GPU pixel-diff test suite (part of the 3/3 passing GPU tests), not by a reviewable screenshot. |

## Commands executed

```text
git log --oneline -15
git status --short
find src/Meshwright.Geometry/Vendor/g3 -type f | sort
grep -rl "Boost Software License" src/Meshwright.Geometry/Vendor/g3
for f in $(find src/Meshwright.Geometry/Vendor/g3 -name '*.cs'); do grep -q "Boost" "$f" || echo "$f"; done
wc -l src/Meshwright.Geometry/Vendor/g3/mesh/DMesh3.cs .../MeshNormals.cs .../MeshConnectedComponents.cs .../MeshBoundaryLoops.cs .../DMeshAABBTree.cs
grep -n "class DMeshAABBTree3|class DMesh3|class MeshNormals|class MeshConnectedComponents|class MeshBoundaryLoops" ...
grep -RInE "throw new NotImplementedException|TODO|stub|scaffold|placeholder" src/Meshwright.Geometry/Vendor/g3/**
grep -RIn "TriangleMesh" --include=*.cs src tests
grep -RIn "ProjectReference|PackageReference" src/*/*.csproj
grep -RIn "g3Sharp|geometry3Sharp" --include=*.csproj .
dotnet build Meshwright.sln
dotnet test Meshwright.sln
```

## Remaining items for a human

1. **Boost licence header** — confirm against the real upstream commit
   `ece336493111ffe372a4bfc7fee5026d4127dade` whether genuine geometry3Sharp
   source files carry per-file Boost banners or rely on a root `LICENSE`
   file only. If per-file banners exist upstream and were stripped here,
   that must be fixed before this vendor tree is trusted for redistribution;
   if upstream never had per-file banners, this is a non-issue and `VENDOR.md`
   already correctly attributes the licence at the project level.
2. Build-hook JSON (`.github/hooks/build-on-edit.json`) is still invalid/
   disabled — carried forward from the prior report, unrelated to this
   batch's mesh-foundation scope.
3. No Avalonia Headless viewport screenshot exists yet — carried forward from
   the prior report.

## Raw logs

- [build.log](build.log)
- [test.log](test.log)
