using g3;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// Hollow: offset shell to a given wall thickness (SPECIFICATION.md §5.1). Keeps the mesh's
/// original outer surface untouched and adds a second, inner surface — an inward SDF offset of
/// the outer surface by <c>wallThickness</c> — so the result is two nested closed shells that
/// together describe the printable wall material. The two shells are individually manifold and
/// correctly oriented (outer normals point outward as before; inner normals point into the
/// cavity), but the *combined* mesh is intentionally non-manifold at the level of "one watertight
/// solid", the same way every other hollowed/shelled mesh (Meshmixer included) is: slicers handle
/// this shape natively, walking each shell's own boundary independently.
/// </summary>
/// <remarks>
/// Uses the same SDF + marching-cubes machinery as <see cref="Repair.VoxelRemeshRepair"/>
/// (<see cref="MeshSignedDistanceGrid"/> / <see cref="MarchingCubes"/>), but at a negative
/// <see cref="MarchingCubes.IsoValue"/> instead of zero — i.e. it extracts the offset surface
/// where the signed distance to the original mesh equals <c>-wallThickness</c> (the sign
/// convention used by <see cref="MeshSignedDistanceGrid"/> is negative *inside* the mesh), rather
/// than the zero level set that reconstructs the surface itself.
///
/// A single non-obvious correctness point: <see cref="MeshSignedDistanceGrid"/> only computes
/// *exact* distances within a band around the surface controlled by
/// <see cref="MeshSignedDistanceGrid.ExactBandWidth"/> (measured in grid cells); everywhere else
/// the grid holds a coarse upper-bound sentinel with the correct sign but no usable gradient. The
/// default band width (1 cell) is enough for a zero-isovalue reconstruction, but not for an
/// offset several cells deep — marching cubes would find no real crossing there. This type widens
/// the band to comfortably cover <c>wallThickness</c> before computing the field.
/// </remarks>
public sealed class HollowShell
{
    /// <summary>
    /// Hollows <paramref name="mesh"/> in place: the outer surface (its existing vertices and
    /// triangles) is left untouched, and a new inner cavity-wall shell is appended.
    /// </summary>
    /// <param name="mesh">Mesh to hollow, in mesh units (mm, per this app's convention). Mutated in place.</param>
    /// <param name="wallThickness">Desired wall thickness, in the same units as <paramref name="mesh"/>. Must be positive.</param>
    /// <param name="longestAxisResolution">
    /// Number of SDF grid cells along the mesh's bounding-box longest axis — the same knob
    /// <see cref="Repair.VoxelRemeshRepair"/> exposes. Higher values resolve the cavity wall more
    /// precisely at increasing memory/time cost, which for Hollow also scales with how many cells
    /// deep <paramref name="wallThickness"/> reaches (see remarks on <see cref="HollowShell"/>).
    /// </param>
    public HollowResult Hollow(DMesh3 mesh, double wallThickness, int longestAxisResolution = DefaultLongestAxisResolution)
    {
        if (wallThickness <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(wallThickness), wallThickness, "Wall thickness must be positive.");
        }

        if (longestAxisResolution < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(longestAxisResolution), longestAxisResolution, "Grid resolution must be at least 2.");
        }

        int trianglesBefore = mesh.TriangleCount;
        double volumeBefore = ComputeVolumeMagnitude(mesh);

        double cellSize = mesh.CachedBounds.MaxDim / longestAxisResolution;

        // Widen the exact-distance band so it reaches wallThickness deep into the interior (see
        // remarks above) — otherwise the offset isosurface would fall in the coarse upper-bound
        // region, where the SDF grid has the right sign but no real gradient to root-find against.
        int exactBandWidth = Math.Max(2, (int)Math.Ceiling(wallThickness / cellSize) + 2);

        var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
        var sdf = new MeshSignedDistanceGrid(mesh, cellSize, spatial) { ExactBandWidth = exactBandWidth };
        sdf.Compute();

        Vector3f origin = sdf.GridOrigin;
        Vector3i dims = sdf.Dimensions;
        var boundsMin = new Vector3d(origin.x, origin.y, origin.z);
        var boundsMax = boundsMin + new Vector3d(
            (dims.x - 1) * cellSize, (dims.y - 1) * cellSize, (dims.z - 1) * cellSize);

        var marchingCubes = new MarchingCubes
        {
            Implicit = new OffsetImplicit(sdf),
            Bounds = new AxisAlignedBox3d(boundsMin, boundsMax),
            CubeSize = cellSize,
            // Negative: the SDF is negative inside the mesh, so this asks for the surface
            // wallThickness deep in the interior — the cavity wall.
            IsoValue = -wallThickness,
        };
        marchingCubes.Generate();

        DMesh3 innerShell = marchingCubes.Mesh;

        if (innerShell.TriangleCount == 0)
        {
            // No interior remains at this offset: wallThickness exceeds the mesh's local
            // thickness everywhere (a thin-walled or small input, or an unreasonably large
            // request). Rather than emit a self-intersecting or inside-out shell, leave the mesh
            // untouched — the caller (HollowOperation) turns this into a plain-language message,
            // matching how HoleFillRepair/SelfIntersectionRepair document their own known limits
            // instead of guessing at a smaller thickness on the caller's behalf.
            return new HollowResult(
                TrianglesBefore: trianglesBefore,
                TrianglesAfter: trianglesBefore,
                VolumeBefore: volumeBefore,
                VolumeAfter: volumeBefore,
                WallThickness: wallThickness,
                LongestAxisResolution: longestAxisResolution,
                CavityAdded: false);
        }

        // The isosurface marching cubes extracts at IsoValue = -wallThickness bounds the *deeper*
        // interior region (field < -wallThickness, i.e. the cavity) with normals pointing away
        // from it by the same convention that gives the zero level set outward-pointing normals —
        // so here that means pointing from the cavity into the shell material. Reverse it so the
        // inner shell's normals face into the cavity, as they must for the shell material to read
        // as the region *between* the two surfaces.
        innerShell.ReverseOrientation(bFlipNormals: true);

        var vertexMap = new Dictionary<int, int>(innerShell.VertexCount);
        foreach (int vid in innerShell.VertexIndices())
        {
            vertexMap[vid] = mesh.AppendVertex(innerShell, vid);
        }

        foreach (int tid in innerShell.TriangleIndices())
        {
            Index3i tri = innerShell.GetTriangle(tid);
            mesh.AppendTriangle(vertexMap[tri.a], vertexMap[tri.b], vertexMap[tri.c]);
        }

        double volumeAfter = ComputeVolumeMagnitude(mesh);

        return new HollowResult(
            TrianglesBefore: trianglesBefore,
            TrianglesAfter: mesh.TriangleCount,
            VolumeBefore: volumeBefore,
            VolumeAfter: volumeAfter,
            WallThickness: wallThickness,
            LongestAxisResolution: longestAxisResolution,
            CavityAdded: true);
    }

    private const int DefaultLongestAxisResolution = 128;

    /// <summary>
    /// Signed-tetrahedron-volume sum via the divergence theorem, same formula
    /// <see cref="Diagnostics.MeshStatistics"/> uses. Run over the combined (outer + reversed
    /// inner) mesh, this naturally yields the shell *material* volume: the outer shell
    /// contributes its full enclosed volume and the inner shell — wound the opposite way —
    /// subtracts the cavity's volume, with no special-casing needed.
    /// </summary>
    private static double ComputeVolumeMagnitude(DMesh3 mesh)
    {
        double volume = 0.0;
        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);
            volume += v0.Dot(v1.Cross(v2)) / 6.0;
        }
        return Math.Abs(volume);
    }

    /// <summary>
    /// Adapts <see cref="MeshSignedDistanceGrid"/>'s discrete voxel grid to the continuous
    /// <see cref="ImplicitFunction3d"/> interface <see cref="MarchingCubes"/> samples, via
    /// trilinear interpolation of the eight grid cells surrounding each query point. Identical to
    /// the adapter <see cref="Repair.VoxelRemeshRepair"/> uses internally; duplicated rather than
    /// shared because it's a small, private implementation detail of each caller and not part of
    /// the vendored g3 surface (§6.2 only vendors <c>MarchingCubes</c>/<c>MeshSignedDistanceGrid</c>
    /// themselves).
    /// </summary>
    private sealed class OffsetImplicit : ImplicitFunction3d
    {
        private readonly MeshSignedDistanceGrid _sdf;

        public OffsetImplicit(MeshSignedDistanceGrid sdf)
        {
            _sdf = sdf;
        }

        public double Value(ref Vector3d pt)
        {
            Vector3f origin = _sdf.GridOrigin;
            Vector3i dims = _sdf.Dimensions;
            float cellSize = _sdf.CellSize;

            double gx = (pt.x - origin.x) / cellSize;
            double gy = (pt.y - origin.y) / cellSize;
            double gz = (pt.z - origin.z) / cellSize;

            int i0 = MathUtil.Clamp((int)Math.Floor(gx), 0, dims.x - 2);
            int j0 = MathUtil.Clamp((int)Math.Floor(gy), 0, dims.y - 2);
            int k0 = MathUtil.Clamp((int)Math.Floor(gz), 0, dims.z - 2);

            double fx = MathUtil.Clamp(gx - i0, 0.0, 1.0);
            double fy = MathUtil.Clamp(gy - j0, 0.0, 1.0);
            double fz = MathUtil.Clamp(gz - k0, 0.0, 1.0);

            double c00 = Lerp(_sdf[i0, j0, k0], _sdf[i0 + 1, j0, k0], fx);
            double c10 = Lerp(_sdf[i0, j0 + 1, k0], _sdf[i0 + 1, j0 + 1, k0], fx);
            double c01 = Lerp(_sdf[i0, j0, k0 + 1], _sdf[i0 + 1, j0, k0 + 1], fx);
            double c11 = Lerp(_sdf[i0, j0 + 1, k0 + 1], _sdf[i0 + 1, j0 + 1, k0 + 1], fx);

            double c0 = c00 * (1.0 - fy) + c10 * fy;
            double c1 = c01 * (1.0 - fy) + c11 * fy;

            return c0 * (1.0 - fz) + c1 * fz;
        }

        private static double Lerp(float a, float b, double t) => a * (1.0 - t) + b * t;
    }
}

/// <summary>Outcome of <see cref="HollowShell.Hollow"/>.</summary>
/// <param name="CavityAdded">
/// False when <paramref name="WallThickness"/> exceeded the mesh's local thickness everywhere, so
/// no interior offset surface existed to add — the mesh was left unchanged (§5.1's degenerate
/// case: too-thin-for-wall-thickness input).
/// </param>
public readonly record struct HollowResult(
    int TrianglesBefore,
    int TrianglesAfter,
    double VolumeBefore,
    double VolumeAfter,
    double WallThickness,
    int LongestAxisResolution,
    bool CavityAdded);
