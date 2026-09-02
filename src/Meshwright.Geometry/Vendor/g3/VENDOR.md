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
