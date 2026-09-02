using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>Aggregate mesh statistics, independent of any detected issues.</summary>
/// <param name="TriangleCount">Number of triangles in the mesh.</param>
/// <param name="VertexCount">Number of vertices in the mesh.</param>
/// <param name="Volume">Signed enclosed volume, computed via the divergence theorem.</param>
/// <param name="SurfaceArea">Total surface area across all triangles.</param>
/// <param name="BoundingBox">Axis-aligned bounding box of all vertices.</param>
/// <param name="ShellCount">Number of connected components (separate shells).</param>
public sealed record MeshStatistics(
    int TriangleCount,
    int VertexCount,
    double Volume,
    double SurfaceArea,
    AxisAlignedBox3d BoundingBox,
    int ShellCount)
{
    /// <summary>
    /// Computes statistics for <paramref name="mesh"/>: volume and surface area are
    /// accumulated directly over triangles (signed tetrahedron volumes from the
    /// origin, per the divergence theorem), and shell count uses
    /// <see cref="MeshConnectedComponents"/>.
    /// </summary>
    public static MeshStatistics Compute(DMesh3 mesh)
    {
        double volume = 0.0;
        double area = 0.0;

        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);

            volume += v0.Dot(v1.Cross(v2)) / 6.0;
            area += 0.5 * (v1 - v0).Cross(v2 - v0).Length;
        }

        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();

        return new MeshStatistics(
            TriangleCount: mesh.TriangleCount,
            VertexCount: mesh.VertexCount,
            Volume: Math.Abs(volume),
            SurfaceArea: area,
            BoundingBox: mesh.GetBounds(),
            ShellCount: components.Count);
    }
}
