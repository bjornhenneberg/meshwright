using g3;
using Meshwright.Geometry.Spatial;

namespace Meshwright.Geometry.Repair;

/// <summary>
/// Removes triangles involved in self-intersections. This is the repair counterpart to
/// <see cref="Diagnostics.SelfIntersectionDetector"/>: both run the same
/// <see cref="SelfIntersectionSearch"/>, so this operation removes exactly what that detector
/// flags and the two cannot drift apart.
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
        int pairsFound = 0;
        var toRemove = new HashSet<int>();

        foreach ((int a, int b) in SelfIntersectionSearch.FindPairs(mesh))
        {
            pairsFound++;
            toRemove.Add(a);
            toRemove.Add(b);
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

}
