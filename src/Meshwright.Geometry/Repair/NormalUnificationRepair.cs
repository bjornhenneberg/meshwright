using g3;

namespace Meshwright.Geometry.Repair;

/// <summary>
/// Makes triangle winding consistent within each connected shell, then orients each shell so its
/// consistent winding faces outward. This is the repair counterpart to
/// <see cref="Diagnostics.InvertedNormalDetector"/>: it walks triangle adjacency using the exact
/// same "do these two triangles traverse their shared edge in opposite directions" test, flipping
/// a triangle instead of merely flagging it.
/// </summary>
public static class NormalUnificationRepair
{
    /// <summary>Below this magnitude a shell's signed volume is treated as "no well-defined inside"
    /// (e.g. an open or non-manifold shell) rather than as evidence the shell is inside-out, so the
    /// whole-shell outward-orientation pass is skipped for it. This is an absolute, not
    /// scale-relative, threshold — see the type's remarks for the resulting limitation.</summary>
    private const double NearZeroVolumeThreshold = 1e-9;

    /// <summary>Outcome of a single <see cref="Apply"/> call.</summary>
    public readonly record struct Result(int FlippedTriangleCount, int ShellCount);

    /// <summary>
    /// Mutates <paramref name="mesh"/> in place: unifies winding within each connected shell, then
    /// flips shells whose consistent winding faces inward. Returns how many triangles were flipped
    /// in total and how many shells were processed.
    /// </summary>
    public static Result Apply(DMesh3 mesh)
    {
        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();

        int flippedCount = 0;
        foreach (MeshConnectedComponents.Component component in components.Components)
        {
            flippedCount += UnifyShell(mesh, component.Indices);
        }

        return new Result(flippedCount, components.Components.Count);
    }

    /// <summary>Unifies winding across one shell's triangles, then corrects the whole shell's
    /// orientation if it came out consistently inward-facing. Returns triangles flipped.</summary>
    private static int UnifyShell(DMesh3 mesh, int[] triangleIds)
    {
        int flippedCount = MakeWindingConsistent(mesh, triangleIds);

        double signedVolume = SignedVolume(mesh, triangleIds);
        if (Math.Abs(signedVolume) < NearZeroVolumeThreshold)
        {
            // No reliable "inside" to orient towards (open boundary, non-manifold shell, or a
            // degenerate flat shell) - leave the winding-consistent result as-is.
            return flippedCount;
        }

        if (signedVolume < 0.0)
        {
            foreach (int tid in triangleIds)
            {
                mesh.ReverseTriOrientation(tid);
            }

            flippedCount += triangleIds.Length;
        }

        return flippedCount;
    }

    /// <summary>Breadth-first walk over triangle adjacency, flipping any triangle that disagrees
    /// with the already-visited (assumed-correct) neighbor it was reached from. Returns triangles
    /// flipped.</summary>
    private static int MakeWindingConsistent(DMesh3 mesh, int[] triangleIds)
    {
        int flippedCount = 0;

        var visited = new HashSet<int>();
        var frontier = new Stack<int>();
        frontier.Push(triangleIds[0]);
        visited.Add(triangleIds[0]);

        while (frontier.Count > 0)
        {
            int tid = frontier.Pop();
            Index3i triEdges = mesh.GetTriEdges(tid);

            for (int i = 0; i < 3; i++)
            {
                int edgeId = triEdges[i];
                Index2i edgeTris = mesh.GetEdgeT(edgeId);
                if (edgeTris.b == DMesh3.InvalidID)
                {
                    continue; // boundary edge, no neighbor across it
                }

                int neighbor = edgeTris.a == tid ? edgeTris.b : edgeTris.a;
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                if (!AgreesAcrossEdge(mesh, edgeId, tid, neighbor))
                {
                    mesh.ReverseTriOrientation(neighbor);
                    flippedCount++;
                }

                frontier.Push(neighbor);
            }
        }

        return flippedCount;
    }

    /// <summary>Same consistency test as <see cref="Diagnostics.InvertedNormalDetector"/>: two
    /// triangles sharing an edge agree iff they traverse it in opposite directions.</summary>
    private static bool AgreesAcrossEdge(DMesh3 mesh, int edgeId, int triA, int triB)
    {
        Index2i edgeVerts = mesh.GetEdgeV(edgeId);

        int a0 = edgeVerts.a, b0 = edgeVerts.b;
        IndexUtil.orient_tri_edge_and_find_other_vtx(ref a0, ref b0, mesh.GetTriangle(triA));

        int a1 = edgeVerts.a, b1 = edgeVerts.b;
        IndexUtil.orient_tri_edge_and_find_other_vtx(ref a1, ref b1, mesh.GetTriangle(triB));

        bool sameDirection = a0 == a1 && b0 == b1;
        return !sameDirection;
    }

    /// <summary>Signed volume of the shell under the shoelace-style tetrahedron-sum formula, kept
    /// signed (unlike <see cref="Diagnostics.DisconnectedShellDetector"/>'s unsigned version) so its
    /// sign reveals whether the shell's current winding faces outward (positive) or inward
    /// (negative).</summary>
    private static double SignedVolume(DMesh3 mesh, int[] triangleIds)
    {
        double volume = 0.0;
        foreach (int tid in triangleIds)
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);

            volume += v0.Dot(v1.Cross(v2)) / 6.0;
        }

        return volume;
    }
}
