using g3;
using Meshwright.Geometry.Mesh;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Detects open boundary loops (holes) in a mesh.
///
/// <para>
/// Loops made entirely of seam edges are ignored. Where a file contains non-manifold geometry,
/// <see cref="NonManifoldMeshBuilder"/> keeps it by cutting the connectivity, which leaves edges
/// that <see cref="DMesh3"/> calls boundaries even though another edge sits at the same two
/// positions and the surface plainly continues. Counting those would report thousands of holes in a
/// closed model — 13,348 on one file of the M4-1 corpus. A loop with any genuinely open edge is
/// still a hole and still reported.
/// </para>
/// </summary>
public sealed class BoundaryHoleDetector : IMeshDetector
{
    public string Category => "BoundaryHole";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        var boundaryLoops = new MeshBoundaryLoops(mesh);
        HashSet<int> seams = PositionTopology.SeamEdges(mesh);
        var issues = new List<MeshIssue>();

        foreach (EdgeLoop loop in boundaryLoops.Loops)
        {
            if (loop.Edges.All(seams.Contains))
            {
                continue;
            }

            var edgeIds = new List<Index2i>(loop.Edges.Length);
            foreach (int edgeId in loop.Edges)
            {
                edgeIds.Add(mesh.GetEdgeV(edgeId));
            }

            issues.Add(new MeshIssue(
                Category: Category,
                Severity: MeshIssueSeverity.Error,
                Message: $"Hole bounded by {loop.Edges.Length} edges",
                VertexIds: loop.Vertices.ToList(),
                EdgeIds: edgeIds));
        }

        return issues;
    }
}
