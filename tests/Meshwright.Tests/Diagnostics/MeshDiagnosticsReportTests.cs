using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class MeshDiagnosticsReportTests
{
    private static readonly MeshStatistics EmptyStats = new(
        TriangleCount: 0,
        VertexCount: 0,
        Volume: 0,
        SurfaceArea: 0,
        BoundingBox: AxisAlignedBox3d.Empty,
        ShellCount: 0);

    [Fact]
    public void Summary_NoIssues_ReportsClean()
    {
        var report = new MeshDiagnosticsReport(EmptyStats, Array.Empty<MeshIssue>());

        Assert.Equal("No issues found.", report.Summary);
    }

    [Fact]
    public void Summary_MultipleCategories_CombinesCountsIntoOneSentence()
    {
        var issues = new List<MeshIssue>
        {
            new("BoundaryHole", MeshIssueSeverity.Error, "Hole near tip."),
            new("BoundaryHole", MeshIssueSeverity.Error, "Hole near base."),
            new("BoundaryHole", MeshIssueSeverity.Error, "Hole on side."),
            new("DisconnectedShell", MeshIssueSeverity.Warning, "Stray shell (0.02% of volume)."),
            new("InvertedNormal", MeshIssueSeverity.Warning, "Flipped face."),
        };
        for (int i = 0; i < 13; i++)
        {
            issues.Add(new MeshIssue("InvertedNormal", MeshIssueSeverity.Warning, "Flipped face."));
        }

        var report = new MeshDiagnosticsReport(EmptyStats, issues);

        Assert.Equal(
            "3 holes, 1 stray shell, 14 flipped faces found.",
            report.Summary);
    }
}
