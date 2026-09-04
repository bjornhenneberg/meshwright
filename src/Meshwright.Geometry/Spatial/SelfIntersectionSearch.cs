using g3;

namespace Meshwright.Geometry.Spatial;

/// <summary>
/// Finds self-intersecting triangle pairs, shared by
/// <c>Meshwright.Geometry.Diagnostics.SelfIntersectionDetector</c> (which reports them) and
/// <c>Meshwright.Geometry.Repair.SelfIntersectionRepair</c> (which removes them), so the two can
/// never disagree about what counts as a self-intersection.
///
/// <para>
/// Broadphase-accelerated with a <see cref="DMeshAABBTree3"/>: only pairs whose bounding boxes
/// overlap are given the exact — and far more expensive — <see cref="MeshQueries.TrianglesIntersection"/>
/// test. Two intersecting triangles necessarily have overlapping bounds, so this cannot miss a true
/// intersection; it only skips exact tests that would have failed anyway.
/// </para>
/// </summary>
public static class SelfIntersectionSearch
{
    /// <summary>
    /// Every non-adjacent, genuinely intersecting triangle pair, each unordered pair yielded once
    /// with <c>A &lt; B</c>, in ascending order of <c>A</c> then <c>B</c> so results are
    /// deterministic and stable across runs.
    /// </summary>
    public static IEnumerable<(int A, int B)> FindPairs(DMesh3 mesh)
    {
        int[] triangleIds = mesh.TriangleIndices().ToArray();
        if (triangleIds.Length < 2)
        {
            yield break;
        }

        var tree = new DMeshAABBTree3(mesh, autoBuild: true);
        var candidates = new List<int>();

        Array.Sort(triangleIds);
        foreach (int ti in triangleIds)
        {
            if (IsDegenerate(mesh, ti))
            {
                continue;
            }

            Index3i triI = mesh.GetTriangle(ti);

            CollectCandidates(tree, mesh, ti, candidates);
            candidates.Sort();

            foreach (int tj in candidates)
            {
                if (SharesVertex(triI, mesh.GetTriangle(tj)) || IsDegenerate(mesh, tj))
                {
                    continue;
                }

                IntrTriangle3Triangle3 intersection = MeshQueries.TrianglesIntersection(mesh, ti, mesh, tj);
                if (intersection is null || !intersection.Find())
                {
                    continue;
                }

                yield return (ti, tj);
            }
        }
    }

    /// <summary>
    /// Triangles whose bounds overlap <paramref name="ti"/>'s, restricted to ids greater than
    /// <paramref name="ti"/> so each unordered pair is visited exactly once.
    /// </summary>
    private static void CollectCandidates(DMeshAABBTree3 tree, DMesh3 mesh, int ti, List<int> candidates)
    {
        candidates.Clear();
        var bounds = (AxisAlignedBox3f)mesh.GetTriBounds(ti);

        var traversal = new DMeshAABBTree3.TreeTraversal
        {
            NextBoxF = (box, depth) => box.Intersects(bounds),
            NextTriangleF = tid =>
            {
                if (tid > ti)
                {
                    candidates.Add(tid);
                }
            },
        };

        tree.DoTraversal(traversal);
    }

    /// <summary>
    /// True for a triangle with no usable area. Such triangles are excluded from the exact
    /// intersection test for two reasons. Semantically, a triangle with no interior cannot
    /// meaningfully pass through another, and it is already reported in its own right by
    /// <c>DegenerateTriangleDetector</c>, so nothing is lost by skipping it here. Practically, the
    /// vendored exact test cannot handle them: a fully-collinear triangle puts all three vertices
    /// on the intersection line, and <c>IntrLine2Triangle2.GetInterval</c> throws "too many
    /// intersections!" — which took down the whole import of a real corpus file until this guard
    /// existed.
    /// </summary>
    private static bool IsDegenerate(DMesh3 mesh, int triangleId)
    {
        Index3i tri = mesh.GetTriangle(triangleId);
        Vector3d v0 = mesh.GetVertex(tri.a);
        Vector3d v1 = mesh.GetVertex(tri.b);
        Vector3d v2 = mesh.GetVertex(tri.c);

        return 0.5 * (v1 - v0).Cross(v2 - v0).Length < DegenerateAreaEpsilon;
    }

    /// <summary>
    /// Absolute zero-area floor. Deliberately far tighter than
    /// <c>DegenerateTriangleDetector</c>'s scale-relative sliver threshold: the job here is only
    /// to exclude triangles the exact predicate cannot process, not to decide what counts as a
    /// sliver, so a genuine thin-but-real triangle is still tested for intersections.
    /// </summary>
    private const double DegenerateAreaEpsilon = 1e-12;

    /// <summary>
    /// Triangles sharing a vertex touch by construction; only non-adjacent pairs are defects.
    /// </summary>
    public static bool SharesVertex(Index3i a, Index3i b) =>
        a.a == b.a || a.a == b.b || a.a == b.c
        || a.b == b.a || a.b == b.b || a.b == b.c
        || a.c == b.a || a.c == b.b || a.c == b.c;
}
