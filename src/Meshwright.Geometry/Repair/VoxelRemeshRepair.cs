using g3;

namespace Meshwright.Geometry.Repair;

/// <summary>
/// Voxel remesh / solidify — the sledgehammer fallback for hopeless meshes (§5.1, §9). Rebuilds a
/// clean, watertight, manifold mesh from any input, however broken (non-manifold, self-intersecting,
/// full of holes), by computing a signed distance field over a voxel grid and extracting its
/// zero isosurface with marching cubes. Fine detail is not preserved — that's the trade this
/// operation makes for always producing something printable, regardless of input quality.
/// </summary>
public sealed class VoxelRemeshRepair
{
    /// <summary>
    /// Replaces <paramref name="mesh"/> in place with a voxel-remeshed reconstruction of itself.
    /// <paramref name="longestAxisResolution"/> is the number of grid cells along the mesh's
    /// bounding-box longest axis — higher values preserve more detail at the cost of a larger grid
    /// (memory and time both scale roughly with the cube of this value).
    /// </summary>
    public VoxelRemeshResult Remesh(DMesh3 mesh, int longestAxisResolution)
    {
        int trianglesBefore = mesh.TriangleCount;

        double cellSize = mesh.CachedBounds.MaxDim / longestAxisResolution;

        var spatial = new DMeshAABBTree3(mesh, autoBuild: true);
        var sdf = new MeshSignedDistanceGrid(mesh, cellSize, spatial);
        sdf.Compute();

        Vector3f origin = sdf.GridOrigin;
        Vector3i dims = sdf.Dimensions;
        var boundsMin = new Vector3d(origin.x, origin.y, origin.z);
        var boundsMax = boundsMin + new Vector3d(
            (dims.x - 1) * cellSize, (dims.y - 1) * cellSize, (dims.z - 1) * cellSize);

        var marchingCubes = new MarchingCubes
        {
            Implicit = new SignedDistanceGridImplicit(sdf),
            Bounds = new AxisAlignedBox3d(boundsMin, boundsMax),
            CubeSize = cellSize,
            IsoValue = 0.0,
        };
        marchingCubes.Generate();

        mesh.Copy(marchingCubes.Mesh);

        return new VoxelRemeshResult(trianglesBefore, mesh.TriangleCount, longestAxisResolution);
    }

    /// <summary>
    /// Adapts <see cref="MeshSignedDistanceGrid"/>'s discrete voxel grid to the continuous
    /// <see cref="ImplicitFunction3d"/> interface <see cref="MarchingCubes"/> samples, via
    /// trilinear interpolation of the eight grid cells surrounding each query point.
    /// </summary>
    private sealed class SignedDistanceGridImplicit : ImplicitFunction3d
    {
        private readonly MeshSignedDistanceGrid _sdf;

        public SignedDistanceGridImplicit(MeshSignedDistanceGrid sdf)
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

/// <summary>Outcome of <see cref="VoxelRemeshRepair.Remesh"/>.</summary>
public readonly record struct VoxelRemeshResult(int TrianglesBefore, int TrianglesAfter, int LongestAxisResolution);
