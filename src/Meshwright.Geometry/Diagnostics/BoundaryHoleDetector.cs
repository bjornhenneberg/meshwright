using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>Detects open boundary loops (holes) in a mesh.</summary>
public sealed class BoundaryHoleDetector : IMeshDetector
{
    public string Category => "BoundaryHole";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        var boundaryLoops = new MeshBoundaryLoops(mesh);
        var issues = new List<MeshIssue>();

        foreach (EdgeLoop loop in boundaryLoops.Loops)
        {
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
