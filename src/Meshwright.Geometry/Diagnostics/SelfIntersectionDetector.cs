using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Flags pairs of non-adjacent triangles whose geometry actually intersects.
/// Uses a straightforward O(n^2) all-pairs check; broadphase acceleration
/// (e.g. <see cref="DMeshAABBTree3"/>) is out of scope for M1.
/// </summary>
public sealed class SelfIntersectionDetector : IMeshDetector
{
    public string Category => "SelfIntersection";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        var issues = new List<MeshIssue>();
        int[] triangleIds = mesh.TriangleIndices().ToArray();

        for (int i = 0; i < triangleIds.Length; i++)
        {
            int ti = triangleIds[i];
            Index3i triI = mesh.GetTriangle(ti);

            for (int j = i + 1; j < triangleIds.Length; j++)
            {
                int tj = triangleIds[j];
                Index3i triJ = mesh.GetTriangle(tj);

                if (SharesVertex(triI, triJ))
                    continue;

                IntrTriangle3Triangle3 intr = MeshQueries.TrianglesIntersection(mesh, ti, mesh, tj);
                if (intr == null || !intr.Find())
                    continue;

                issues.Add(new MeshIssue(
                    Category,
                    MeshIssueSeverity.Error,
                    $"Self-intersection between triangle {ti} and triangle {tj}",
                    TriangleIds: new[] { ti, tj }));
            }
        }

        return issues;
    }

    private static bool SharesVertex(Index3i a, Index3i b)
    {
        return a.a == b.a || a.a == b.b || a.a == b.c
            || a.b == b.a || a.b == b.b || a.b == b.c
            || a.c == b.a || a.c == b.b || a.c == b.c;
    }
}
