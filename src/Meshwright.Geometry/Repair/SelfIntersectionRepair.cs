using g3;

namespace Meshwright.Geometry.Repair;

/// <summary>
/// Removes triangles involved in self-intersections. This is the repair counterpart to
/// <see cref="Diagnostics.SelfIntersectionDetector"/>: reuses its exact detection approach
/// (<see cref="MeshQueries.TrianglesIntersection"/> over non-adjacent triangle pairs) so this
/// operation targets exactly what that detector flags, then deletes every offending triangle.
/// Deliberately does not attempt to re-triangulate a valid patch over the resulting gap - per
/// SPECIFICATION.md §9, robust self-intersection repair is genuinely hard, so this is a
/// best-effort local removal. It can legitimately leave boundary holes behind; closing them is a
/// separate hole-filling step run later in the Auto Repair pipeline.
/// </summary>
public sealed class SelfIntersectionRepair
{
    /// <summary>Outcome of <see cref="Resolve"/>.</summary>
    public readonly record struct Result(int PairsFound, int TrianglesRemoved);

    /// <summary>
    /// Finds every self-intersecting, non-adjacent triangle pair and removes every triangle
    /// involved in at least one of them. Broadphase-accelerated with a <see cref="DMeshAABBTree3"/>:
    /// only triangle pairs whose bounding boxes overlap are given the exact (and much more
    /// expensive) <see cref="MeshQueries.TrianglesIntersection"/> test, since two intersecting
    /// triangles must have overlapping bounds - so this cannot skip a true intersection, only
    /// avoid exact tests that would have failed anyway.
    /// </summary>
    public Result Resolve(DMesh3 mesh)
    {
        int[] triangleIds = mesh.TriangleIndices().ToArray();
        if (triangleIds.Length < 2)
        {
            return new Result(0, 0);
        }

        var tree = new DMeshAABBTree3(mesh, autoBuild: true);

        int pairsFound = 0;
        var toRemove = new HashSet<int>();

        foreach (int ti in triangleIds)
        {
            Index3i triI = mesh.GetTriangle(ti);

            foreach (int tj in FindCandidates(tree, mesh, ti))
            {
                Index3i triJ = mesh.GetTriangle(tj);
                if (SharesVertex(triI, triJ))
                {
                    continue;
                }

                IntrTriangle3Triangle3 intr = MeshQueries.TrianglesIntersection(mesh, ti, mesh, tj);
                if (intr == null || !intr.Find())
                {
                    continue;
                }

                pairsFound++;
                toRemove.Add(ti);
                toRemove.Add(tj);
            }
        }

        int removedCount = 0;
        foreach (int tid in toRemove)
        {
            // A triangle can appear in more than one intersecting pair, and removing it can also
            // remove a neighbor sharing an already-isolated vertex, so re-check validity here
            // rather than assuming every id collected above is still live.
            if (!mesh.IsTriangle(tid))
            {
                continue;
            }

            mesh.RemoveTriangle(tid, bRemoveIsolatedVertices: true, bPreserveManifold: false);
            removedCount++;
        }

        return new Result(pairsFound, removedCount);
    }

    /// <summary>Triangle ids whose bounding box overlaps <paramref name="ti"/>'s, restricted to
    /// ids greater than <paramref name="ti"/> so each unordered pair is visited exactly once
    /// (mirrors <see cref="Diagnostics.SelfIntersectionDetector"/>'s i &lt; j loop).</summary>
    private static List<int> FindCandidates(DMeshAABBTree3 tree, DMesh3 mesh, int ti)
    {
        var bounds = (AxisAlignedBox3f)mesh.GetTriBounds(ti);
        var candidates = new List<int>();

        var traversal = new DMeshAABBTree3.TreeTraversal
        {
            NextBoxF = (box, depth) => box.Intersects(bounds),
            NextTriangleF = tid =>
            {
                if (tid > ti)
                {
                    candidates.Add(tid);
                }
            }
        };
        tree.DoTraversal(traversal);

        return candidates;
    }

    private static bool SharesVertex(Index3i a, Index3i b)
    {
        return a.a == b.a || a.a == b.b || a.a == b.c
            || a.b == b.a || a.b == b.b || a.b == b.c
            || a.c == b.a || a.c == b.b || a.c == b.c;
    }
}
