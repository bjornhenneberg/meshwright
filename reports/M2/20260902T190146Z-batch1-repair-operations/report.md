# M2 batch 1 — the six repair operations, plus STL/OBJ export

**Verdict: verified**

## What this batch is

Seven independent units of work dispatched in parallel (per this repo's
milestone-lead/parallel-orchestrator convention), each scoped to disjoint
files against the `IMeshOperation` contract from
[batch 0](../20260902T183819Z-batch0-operation-contract/report.md):

1. Remove degenerate triangles + duplicate vertices
2. Normal unification
3. Remove small disconnected shells
4. Hole filling (flat / planar / smooth)
5. Self-intersection resolution
6. Voxel remesh / solidify fallback (required new vendoring — see below)
7. STL/OBJ export writers (`Meshwright.IO`, independent of repair entirely)

## What was built

Each repair operation follows the same shape established in batch 0: a pure
algorithm in `Meshwright.Geometry/Repair/`, no UI/Core/undo awareness, plus a
thin `Meshwright.Core/Operations/<Name>Operation.cs` wrapper deriving from
`MeshOperationBase`.

- **`RemoveDegenerateAndDuplicatesRepair`** — welds coincident vertices using
  the same spatial-bucket epsilon (`1e-9`) as M1's `DuplicateVertexDetector`,
  then drops triangles that are degenerate post-weld or by
  `DegenerateTriangleDetector`'s area-epsilon formula. Rebuilds into a fresh
  `DMesh3` and writes it back via `DMesh3.Copy(...)`.
- **`NormalUnificationRepair`** — BFS per connected component using the same
  edge-consistency test as `InvertedNormalDetector`, flipping disagreeing
  neighbors; then corrects each component's overall orientation via signed
  volume (reusing `DisconnectedShellDetector`'s volume formula, without the
  `Math.Abs`).
- **`SmallShellRemovalRepair`** — `MeshConnectedComponents` + per-shell signed
  volume (same as `DisconnectedShellDetector`); keeps the largest shell,
  removes any other shell below a configurable fraction of total volume
  (default 1%).
- **`HoleFillRepair`** — `MeshBoundaryLoops`-driven fan (`Flat`), best-fit-plane
  ear-clip triangulation (`Planar`, correct on non-convex loops), and
  `Smooth` (planar fill + one grafted, Laplacian-relaxed interior vertex).
  Winding is derived rigorously from `EdgeLoop`'s boundary-edge direction
  convention, verified by hand against a concrete fixture before trusting it.
- **`SelfIntersectionRepair`** — same detection logic as
  `SelfIntersectionDetector`, but accelerated with `DMeshAABBTree3` broadphase
  instead of the detector's documented-as-M1-only O(n²) scan; removes
  intersecting triangles and deliberately leaves any resulting holes for a
  later hole-fill pass rather than re-triangulating itself.
- **`VoxelRemeshRepair`** — required vendoring `MarchingCubes` and
  `MeshSignedDistanceGrid` from upstream geometry3Sharp (same pinned commit as
  the rest of the vendor tree) plus their real dependencies (`DenseGrid3f`/
  `DenseGrid3i`, `AxisAlignedBox3i`/`AxisAlignedBox2i`, and a deliberately
  trimmed `ImplicitFunction3d` interface). Every new vendored file carries the
  per-file Boost header; trims and rationale are recorded in
  [`VENDOR.md`](../../../src/Meshwright.Geometry/Vendor/g3/VENDOR.md). Bridges
  the discrete SDF grid to `MarchingCubes`' continuous sampling via a small,
  original (non-vendored) trilinear-interpolation adapter.
- **`StlWriter`** / **`ObjWriter`** (`Meshwright.IO`) — binary STL export,
  round-trip-verified through the existing `StlReader`; ASCII OBJ export with
  correct internal-id → 1-based sequential index remapping (verified against a
  mesh with a deliberately non-compact id space).

## Verification

- Clean rebuild (`obj`/`bin` removed first) — 0 errors, 0 warnings on the
  changed projects (pre-existing vendor-tree nullable warnings only).
  [build.log](build.log)
- Full `dotnet test` — **115/115 passing** (109 unit + 6 GPU), up from 79/79
  at the end of batch 0 (36 new tests across the seven units).
  [test.log](test.log)
- `git status` after all seven agents finished showed changes confined
  exactly to each task's assigned scope — no file outside the seven listed
  scopes was touched (in particular, nothing in `Meshwright.App`, despite one
  agent hitting a transient stale-incremental-build error mid-run that
  implicated `Meshwright.App`'s `obj/`; a clean rebuild afterward confirmed
  that was build-system contention from seven concurrent `dotnet build`
  invocations sharing `obj`/`bin`, not a real code issue).
- Reviewed the two most algorithmically involved files by hand
  (`HoleFillRepair.cs`'s winding/ear-clip/Laplacian-relaxation logic,
  `VoxelRemeshRepair.cs`'s SDF→marching-cubes bridge) — both are correct and
  well-commented on their non-obvious decisions (winding derivation,
  trim rationale).
- No UI-facing change in this batch — no screenshots needed.

## Known gaps / honest limitations (self-reported by the implementing agents, spot-checked above)

- `HoleFillRepair`'s ear-clipping does not check candidate diagonals against
  unrelated, non-adjacent mesh topology — a real but narrow limitation
  documented in the code and this batch's task history; not expected to
  matter for real-world holes.
- `NormalUnificationRepair`'s "is this shell's volume meaningfully nonzero"
  check uses a fixed absolute threshold (`1e-9`), not scale-relative — flagged
  as a known limitation in the type's own XML doc.
- `VoxelRemeshRepair`'s default resolution (128 cells along the longest axis)
  is not benchmarked against §6.4's 500k-triangle/5s auto-repair target —
  correctness is verified at low test resolutions only; performance tuning is
  deferred.
- `SelfIntersectionRepair` only removes intersecting triangles; it does not
  attempt to re-triangulate a patch. Downstream consumers (the Auto Repair
  pipeline, batch 2) must run hole filling afterward.

None of these block batch 2 (pipeline/undo wiring is unaffected by any of
them) or this batch's own verification.
