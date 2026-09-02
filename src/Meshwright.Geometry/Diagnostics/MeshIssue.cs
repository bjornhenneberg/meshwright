using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// One detected problem with a mesh, in plain language, plus the specific
/// elements involved so the viewport can highlight them.
/// </summary>
/// <param name="Category">Machine-stable name, e.g. "NonManifoldEdge".</param>
/// <param name="Severity">How much this issue matters for printing/slicing.</param>
/// <param name="Message">Plain-language description, e.g. "3 holes found."</param>
/// <param name="TriangleIds">Triangle ids implicated by this issue, if any.</param>
/// <param name="VertexIds">Vertex ids implicated by this issue, if any.</param>
/// <param name="EdgeIds">Edges (as vertex pairs) implicated by this issue, if any.</param>
public sealed record MeshIssue(
    string Category,
    MeshIssueSeverity Severity,
    string Message,
    IReadOnlyList<int>? TriangleIds = null,
    IReadOnlyList<int>? VertexIds = null,
    IReadOnlyList<Index2i>? EdgeIds = null)
{
    public IReadOnlyList<int> TriangleIds { get; init; } = TriangleIds ?? Array.Empty<int>();
    public IReadOnlyList<int> VertexIds { get; init; } = VertexIds ?? Array.Empty<int>();
    public IReadOnlyList<Index2i> EdgeIds { get; init; } = EdgeIds ?? Array.Empty<Index2i>();
}
