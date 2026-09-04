using g3;

namespace Meshwright.Geometry.Mesh;

/// <summary>
/// Topology as the printed object has it, rather than as <see cref="DMesh3"/>'s vertex indexing has
/// it.
///
/// <para>
/// <see cref="DMesh3"/> cannot represent a non-manifold junction, so
/// <see cref="NonManifoldMeshBuilder"/> keeps such geometry by duplicating vertices and cutting the
/// connectivity there (see its remarks). The surface is complete and correctly positioned, but the
/// cut leaves <em>seams</em>: edges the data structure calls boundaries even though the surface
/// plainly continues on the other side, at the very same coordinates.
/// </para>
///
/// <para>
/// Detectors that reason purely about vertex ids therefore see holes and separate shells that a
/// user does not have — on the M4-1 corpus that meant 13,348 phantom holes reported on one closed
/// file. Grouping by rounded position instead recovers the real topology.
/// <c>NonManifoldDetector</c> already did this for its own purposes; this is the shared version.
/// </para>
/// </summary>
public static class PositionTopology
{
    /// <summary>
    /// Rounds a position to a lattice so coincident-but-not-bitwise-identical vertices group
    /// together. The scale matches <c>NonManifoldDetector</c>'s long-standing choice, so the
    /// detectors agree on what "the same place" means.
    /// </summary>
    public static (long X, long Y, long Z) Quantize(Vector3d v)
    {
        const double scale = 1e6;
        return ((long)Math.Round(v.x * scale), (long)Math.Round(v.y * scale), (long)Math.Round(v.z * scale));
    }

    /// <summary>A position-based key for an edge, independent of which vertex ids it happens to use.</summary>
    public static ((long, long, long) A, (long, long, long) B) EdgeKey(DMesh3 mesh, int edgeId)
    {
        Index2i ev = mesh.GetEdgeV(edgeId);
        (long, long, long) a = Quantize(mesh.GetVertex(ev.a));
        (long, long, long) b = Quantize(mesh.GetVertex(ev.b));
        return a.CompareTo(b) <= 0 ? (a, b) : (b, a);
    }

    /// <summary>
    /// Edges that <see cref="DMesh3"/> reports as boundaries but which are only seams: another edge
    /// occupies the same two positions, so the surface does continue there and there is no hole.
    /// </summary>
    public static HashSet<int> SeamEdges(DMesh3 mesh)
    {
        var byPosition = new Dictionary<((long, long, long), (long, long, long)), List<int>>();
        foreach (int eid in mesh.EdgeIndices())
        {
            var key = EdgeKey(mesh, eid);
            if (!byPosition.TryGetValue(key, out List<int>? ids))
            {
                ids = new List<int>();
                byPosition[key] = ids;
            }

            ids.Add(eid);
        }

        var seams = new HashSet<int>();
        foreach (List<int> ids in byPosition.Values)
        {
            if (ids.Count < 2)
            {
                continue;
            }

            // Several edges at one position: the surface is stitched there by geometry even though
            // the indexing separates it, so none of them bounds a hole.
            foreach (int eid in ids)
            {
                if (mesh.IsBoundaryEdge(eid))
                {
                    seams.Add(eid);
                }
            }
        }

        return seams;
    }

    /// <summary>
    /// Connected components of triangles, joined across coincident edge positions as well as shared
    /// edges — so a surface cut by vertex duplication still counts as one shell.
    /// </summary>
    public static IReadOnlyList<List<int>> ConnectedComponents(DMesh3 mesh)
    {
        var parent = new Dictionary<int, int>();

        int Find(int x)
        {
            int root = x;
            while (parent[root] != root)
            {
                root = parent[root];
            }

            while (parent[x] != root)
            {
                (x, parent[x]) = (parent[x], root);
            }

            return root;
        }

        void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb)
            {
                parent[ra] = rb;
            }
        }

        foreach (int tid in mesh.TriangleIndices())
        {
            parent[tid] = tid;
        }

        var byPosition = new Dictionary<((long, long, long), (long, long, long)), int>();
        foreach (int eid in mesh.EdgeIndices())
        {
            Index2i et = mesh.GetEdgeT(eid);

            // Triangles meeting at this edge in the indexing.
            if (et.a != DMesh3.InvalidID && et.b != DMesh3.InvalidID)
            {
                Union(et.a, et.b);
            }

            // Triangles meeting at this edge's *position*, across a seam.
            var key = EdgeKey(mesh, eid);
            int representative = et.a != DMesh3.InvalidID ? et.a : et.b;
            if (representative == DMesh3.InvalidID)
            {
                continue;
            }

            if (byPosition.TryGetValue(key, out int other))
            {
                Union(representative, other);
            }
            else
            {
                byPosition[key] = representative;
            }
        }

        var components = new Dictionary<int, List<int>>();
        foreach (int tid in mesh.TriangleIndices())
        {
            int root = Find(tid);
            if (!components.TryGetValue(root, out List<int>? members))
            {
                members = new List<int>();
                components[root] = members;
            }

            members.Add(tid);
        }

        return components.Values.ToList();
    }
}
