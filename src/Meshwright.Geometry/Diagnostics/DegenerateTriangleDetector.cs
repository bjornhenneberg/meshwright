using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>Flags triangles with zero or near-zero area: coincident-vertex and sliver/collinear triangles.</summary>
public sealed class DegenerateTriangleDetector : IMeshDetector
{
    // Absolute floor guards against exact/near-exact zero area regardless of mesh scale
    // (e.g. duplicate-vertex triangles). The relative term scales with the mesh's average
    // edge length squared so slivers are caught consistently on both tiny and huge meshes.
    private const double AbsoluteAreaEpsilon = 1e-10;
    private const double RelativeAreaEpsilonFactor = 1e-6;

    public string Category => "DegenerateTriangle";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        var issues = new List<MeshIssue>();

        double averageEdgeLengthSquared = ComputeAverageEdgeLengthSquared(mesh);
        double areaEpsilon = Math.Max(
            AbsoluteAreaEpsilon,
            RelativeAreaEpsilonFactor * averageEdgeLengthSquared);

        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);

            double area = 0.5 * (v1 - v0).Cross(v2 - v0).Length;

            if (area < areaEpsilon)
            {
                issues.Add(new MeshIssue(
                    Category,
                    MeshIssueSeverity.Warning,
                    $"Triangle {tid} has near-zero area (degenerate)",
                    TriangleIds: new[] { tid }));
            }
        }

        return issues;
    }

    private static double ComputeAverageEdgeLengthSquared(DMesh3 mesh)
    {
        double total = 0.0;
        int count = 0;

        foreach (int eid in mesh.EdgeIndices())
        {
            Index2i ev = mesh.GetEdgeV(eid);
            total += (mesh.GetVertex(ev.a) - mesh.GetVertex(ev.b)).LengthSquared;
            count++;
        }

        return count > 0 ? total / count : 1.0;
    }
}
