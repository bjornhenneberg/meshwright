# geometry3Sharp provenance

Upstream geometry3Sharp licenses at the repo root (a single `LICENSE` file) rather than per-file; the Boost Software License 1.0 header prepended to each vendored `.cs` file here was added to satisfy this project's own per-file vendoring policy (AGENTS.md / the g3sharp-vendoring skill), not because upstream carries one.

Vendored 2026-08-29 from geometry3Sharp commit `ece336493111ffe372a4bfc7fee5026d4127dade` (https://github.com/gradientspace/geometry3Sharp), under the Boost Software License 1.0.

The following upstream implementation roots are copied verbatim with their original relative source layout. Their real compile-time dependencies are vendored alongside them under `core/`, `math/`, `mesh/`, `mesh_selection/`, `spatial/`, `queries/`, `distance/`, `intersection/`, and `io/`. No local scaffolds or substitute implementations are retained.

| Upstream source | Vendored type |
| --- | --- |
| `mesh/DMesh3.cs`, `mesh/DMesh3_debug.cs`, `mesh/DMesh3_edge_operators.cs` | `DMesh3` |
| `mesh/MeshNormals.cs` | `MeshNormals` |
| `mesh_selection/MeshConnectedComponents.cs` | `MeshConnectedComponents` |
| `mesh_selection/MeshBoundaryLoops.cs` | `MeshBoundaryLoops` |
| `spatial/DMeshAABBTree.cs` | `DMeshAABBTree3` |

Trim: `math/TransformSequence.cs` omits its `Store` and `Restore` binary serialization members. They require g3's unrelated planar-curve serialization graph and are not needed by the vendored mesh, topology, normal, or spatial-query functionality.

---

Vendored 2026-09-02 from geometry3Sharp commit `ece336493111ffe372a4bfc7fee5026d4127dade` (https://github.com/gradientspace/geometry3Sharp), under the Boost Software License 1.0, for the voxel remesh / solidify repair operation (SPECIFICATION.md §6.2, §5.1 Repair).

| Upstream source | Vendored type |
| --- | --- |
| `mesh_generators/MarchingCubes.cs` | `MarchingCubes` |
| `spatial/MeshSignedDistanceGrid.cs` | `MeshSignedDistanceGrid` |
| `spatial/DenseGrid3.cs` | `DenseGrid3f`, `DenseGrid3i` (required by `MeshSignedDistanceGrid`) |
| `math/AxisAlignedBox3i.cs` | `AxisAlignedBox3i` (required by `DenseGrid3f`/`DenseGrid3i` bounds and `MeshSignedDistanceGrid`'s flood-fill sweep) |
| `math/AxisAlignedBox2i.cs` | `AxisAlignedBox2i` (required by `MeshSignedDistanceGrid.compute_signs`'s parallel x-row partitioning) |
| `implicit/Implicit3d.cs`, vendored as `core/Implicit3d.cs` | `ImplicitFunction3d` only |

Trims:

- `mesh_generators/MarchingCubes.cs`: the parameterless constructor no longer default-constructs `Implicit` to an `ImplicitSphere3d` sample sphere. `ImplicitSphere3d` lives in g3's implicit-surface primitive/CSG library, which §6.2 explicitly excludes from the vendored set in favor of Manifold for booleans; only the one-method `ImplicitFunction3d` interface that `MarchingCubes.Implicit` is typed against was kept. `Implicit` is left unset by the constructor; `Meshwright.Geometry.Repair.VoxelRemeshRepair` always assigns it before calling `Generate()`. Documented inline in the file at the constructor.
- `implicit/Implicit3d.cs`: of upstream's ~30 implicit-surface types (spheres, boxes, half-spaces, CSG union/intersection/difference, skeletal/R-function blends, etc.), only the `ImplicitFunction3d` interface (`double Value(ref Vector3d pt)`) is kept, for the same reason as above. Placed under `core/` rather than a new `implicit/` subdirectory, since a single interface didn't warrant a new top-level bucket and `core/g3Interfaces.cs` already holds small cross-cutting interfaces in this vendor tree.
- `spatial/DenseGrid3.cs`: `DenseGrid3f.get_slice`/`set_slice` and `DenseGrid3i.get_slice`/`get_bitmap` are omitted. They pull in `DenseGrid2f`/`DenseGrid2i` (2D grid slicing, unused by marching-cubes/SDF generation) and `Bitmap3` (binary bitmap conversion, likewise unused here). Nothing in `MeshSignedDistanceGrid` or `MarchingCubes` calls them.

No trims were needed in `spatial/MeshSignedDistanceGrid.cs`, `math/AxisAlignedBox3i.cs`, or `math/AxisAlignedBox2i.cs` — all their dependencies (`DMesh3`, `DMeshAABBTree3`, `MathUtil`, `gParallel`, `gIndices` (`math/IndexUtil.cs`), `Triangle3d`, `DistPoint3Triangle3`, `Vector2i`/`Vector3d`/`Vector3f`/`Vector3i`, `AxisAlignedBox3d`) were already vendored from M1.

---

Vendored 2026-09-02 from geometry3Sharp commit `ece336493111ffe372a4bfc7fee5026d4127dade` (https://github.com/gradientspace/geometry3Sharp), under the Boost Software License 1.0, for the quadric edge-collapse decimation operation (SPECIFICATION.md §6.2, §5.1 Simplify).

| Upstream source | Vendored type |
| --- | --- |
| `mesh/Reducer.cs` | `Reducer`, `QuadricError` |
| `mesh/MeshRefinerBase.cs` | `MeshRefinerBase` (`Reducer`'s base class — shared flip/link-condition checks and constraint-aware collapse logic, also used by upstream's `Remesher`, not yet vendored) |
| `mesh/MeshConstraints.cs` | `MeshConstraints`, `EdgeConstraint`, `VertexConstraint`, `EdgeRefineFlags` (referenced by `MeshRefinerBase`/`Reducer`'s optional constraint path; Meshwright's `DecimateOperation` does not currently set constraints, so this exercises the "no constraints" branch only, but the type is a hard compile-time dependency of `MeshRefinerBase`) |
| `core/IndexPriorityQueue.cs` | `IndexPriorityQueue` (min-heap keyed by edge id, used to process edge collapses in increasing quadric-error order) |
| `core/ProgressCancel.cs` | `ProgressCancel`, `ICancelSource`, `CancelFunction` (cancellation hook `MeshRefinerBase.Progress` is typed against) |

No trims were needed — `Reducer.cs`'s own dependencies beyond the five files above (`DMesh3`, `MathUtil`, `IndexUtil`, `gParallel`, `DVector<T>`, `IProjectionTarget` (`spatial/SpatialInterfaces.cs`), `Vector3d`/`Index2i`/`Index3i`) were already vendored from M1/M2. `IProjectionTarget` in particular meant `Reducer`'s optional projection-target support (`SetProjectionTarget`) compiles as-is; `DecimateOperation` does not use it (no target surface concept exists yet in Meshwright), so the `TargetProjectionMode.NoProjection`/absent-target path is what's actually exercised.
