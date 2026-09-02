using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Flags shared edges where the two adjacent triangles traverse the edge in
/// the same direction instead of opposite directions, which is the local
/// signature of a flipped/inverted face relative to its neighbors.
/// </summary>
public sealed class InvertedNormalDetector : IMeshDetector
{
    public string Category => "InvertedNormal";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        var issues = new List<MeshIssue>();

        foreach (int edgeId in mesh.EdgeIndices())
        {
            Index2i edgeTriangles = mesh.GetEdgeT(edgeId);
            if (edgeTriangles.b == DMesh3.InvalidID)
            {
                continue; // boundary edge, only one adjacent triangle
            }

            int tri0 = edgeTriangles.a;
            int tri1 = edgeTriangles.b;

            Index2i edgeVerts = mesh.GetEdgeV(edgeId);

            int a0 = edgeVerts.a, b0 = edgeVerts.b;
            Index3i tri0Verts = mesh.GetTriangle(tri0);
            IndexUtil.orient_tri_edge_and_find_other_vtx(ref a0, ref b0, tri0Verts);

            int a1 = edgeVerts.a, b1 = edgeVerts.b;
            Index3i tri1Verts = mesh.GetTriangle(tri1);
            IndexUtil.orient_tri_edge_and_find_other_vtx(ref a1, ref b1, tri1Verts);

            // Consistent outward winding requires the shared edge to be
            // traversed in opposite directions by the two adjacent triangles.
            bool sameDirection = a0 == a1 && b0 == b1;
            if (!sameDirection)
            {
                continue;
            }

            issues.Add(new MeshIssue(
                Category,
                MeshIssueSeverity.Error,
                $"Triangle {tri0} and triangle {tri1} disagree on surface direction across their shared edge",
                TriangleIds: new[] { tri0, tri1 },
                EdgeIds: new[] { edgeVerts }));
        }

        return issues;
    }
}
