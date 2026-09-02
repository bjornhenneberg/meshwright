using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Flags hard non-manifold defects: edges shared by more than two triangles,
/// and vertices where more than one triangle fan meets (bowtie vertices).
/// Both will reliably break slicing.
/// </summary>
public sealed class NonManifoldDetector : IMeshDetector
{
    public string Category => "NonManifoldEdge";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        var issues = new List<MeshIssue>();
        issues.AddRange(DetectNonManifoldEdges(mesh));
        issues.AddRange(DetectNonManifoldVertices(mesh));
        return issues;
    }

    // DMesh3 rejects any AppendTriangle/InsertTriangle call that would give a single
    // (vertex-id) edge a third triangle, so a true non-manifold edge can only appear
    // in this data structure as several distinct edge ids that share the same pair of
    // vertex *positions* (e.g. duplicated/unwelded coincident vertices). Group edges
    // by position to catch that case.
    private static IEnumerable<MeshIssue> DetectNonManifoldEdges(DMesh3 mesh)
    {
        var byPosition = new Dictionary<((long, long, long) A, (long, long, long) B), List<int>>();

        foreach (int eid in mesh.EdgeIndices())
        {
            Index2i ev = mesh.GetEdgeV(eid);
            (long, long, long) a = Quantize(mesh.GetVertex(ev.a));
            (long, long, long) b = Quantize(mesh.GetVertex(ev.b));
            var key = a.CompareTo(b) <= 0 ? (a, b) : (b, a);

            if (!byPosition.TryGetValue(key, out List<int>? edgeIds))
            {
                edgeIds = new List<int>();
                byPosition[key] = edgeIds;
            }

            edgeIds.Add(eid);
        }

        foreach (List<int> edgeIds in byPosition.Values)
        {
            var triangleIds = new List<int>();
            foreach (int eid in edgeIds)
            {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.a != DMesh3.InvalidID)
                {
                    triangleIds.Add(et.a);
                }

                if (et.b != DMesh3.InvalidID)
                {
                    triangleIds.Add(et.b);
                }
            }

            if (triangleIds.Count > 2)
            {
                yield return new MeshIssue(
                    "NonManifoldEdge",
                    MeshIssueSeverity.Error,
                    $"Non-manifold edge shared by {triangleIds.Count} triangles",
                    TriangleIds: triangleIds,
                    EdgeIds: edgeIds.Select(mesh.GetEdgeV).ToArray());
            }
        }
    }

    // A vertex is a bowtie (non-manifold vertex) if its incident triangles don't form a
    // single connected fan. Union incident triangles that share an edge through this
    // vertex; more than one resulting component means separate fans meet only at the
    // vertex, with no shared edge between them.
    private static IEnumerable<MeshIssue> DetectNonManifoldVertices(DMesh3 mesh)
    {
        foreach (int vid in mesh.VertexIndices())
        {
            List<int> triangles = mesh.VtxTrianglesItr(vid).ToList();
            if (triangles.Count <= 1)
            {
                continue;
            }

            var parent = new Dictionary<int, int>();
            foreach (int tid in triangles)
            {
                parent[tid] = tid;
            }

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            foreach (int eid in mesh.VtxEdgesItr(vid))
            {
                Index2i et = mesh.GetEdgeT(eid);
                if (et.a == DMesh3.InvalidID || et.b == DMesh3.InvalidID)
                {
                    continue;
                }

                int rootA = Find(et.a);
                int rootB = Find(et.b);
                if (rootA != rootB)
                {
                    parent[rootA] = rootB;
                }
            }

            int componentCount = triangles.Select(Find).Distinct().Count();
            if (componentCount > 1)
            {
                yield return new MeshIssue(
                    "NonManifoldEdge",
                    MeshIssueSeverity.Error,
                    $"Non-manifold vertex where {componentCount} separate surface fans meet",
                    TriangleIds: triangles,
                    VertexIds: new[] { vid });
            }
        }
    }

    private static (long, long, long) Quantize(Vector3d v)
    {
        const double scale = 1e6;
        return ((long)Math.Round(v.x * scale), (long)Math.Round(v.y * scale), (long)Math.Round(v.z * scale));
    }
}
