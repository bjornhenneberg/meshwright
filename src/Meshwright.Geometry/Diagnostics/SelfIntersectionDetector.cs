using g3;
using Meshwright.Geometry.Spatial;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Flags pairs of non-adjacent triangles whose geometry actually intersects.
///
/// <para>
/// The search itself lives in <see cref="SelfIntersectionSearch"/>, shared with
/// <c>SelfIntersectionRepair</c>. It was an all-pairs O(n^2) scan until the real-world corpus
/// (M4-1) made the cost visible: 2.5s on a 5,800-triangle mesh, against §6.4's budget of 5s for a
/// 500,000-triangle auto-repair, and hours on the corpus's larger models. The broadphase the repair
/// side already used brings it in line.
/// </para>
/// </summary>
public sealed class SelfIntersectionDetector : IMeshDetector
{
    public string Category => "SelfIntersection";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        var issues = new List<MeshIssue>();

        foreach ((int a, int b) in SelfIntersectionSearch.FindPairs(mesh))
        {
            issues.Add(new MeshIssue(
                Category,
                MeshIssueSeverity.Error,
                $"Self-intersection between triangle {a} and triangle {b}",
                TriangleIds: new[] { a, b }));
        }

        return issues;
    }
}
