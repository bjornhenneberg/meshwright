using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class DegenerateTriangleDetectorTests
{
    private static readonly DegenerateTriangleDetector Detector = new();

    [Fact]
    public void Detect_UnitTriangle_ReportsNoIssues()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a, b, c);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_Tetrahedron_ReportsNoIssues()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int d = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(a, c, b);
        mesh.AppendTriangle(a, b, d);
        mesh.AppendTriangle(b, c, d);
        mesh.AppendTriangle(c, a, d);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_TriangleWithCoincidentVertices_IsFlagged()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 0, 0)); // coincident with a
        int tid = mesh.AppendTriangle(a, b, c);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal("DegenerateTriangle", issue.Category);
        Assert.Equal(MeshIssueSeverity.Warning, issue.Severity);
        Assert.Contains(tid.ToString(), issue.Message);
        Assert.Equal(new[] { tid }, issue.TriangleIds);
    }

    [Fact]
    public void Detect_NearlyCollinearSliverTriangle_IsFlagged()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0.5, 1e-8, 0)); // nearly on the a-b line
        int tid = mesh.AppendTriangle(a, b, c);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal(new[] { tid }, issue.TriangleIds);
    }

    [Fact]
    public void Detect_MixOfDegenerateAndWellFormedTriangles_OnlyFlagsDegenerateOnes()
    {
        var mesh = new DMesh3();

        // Well-formed unit triangle: must NOT be flagged.
        int a0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b0 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c0 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int goodTid = mesh.AppendTriangle(a0, b0, c0);

        // Degenerate: coincident vertices.
        int a1 = mesh.AppendVertex(new Vector3d(5, 5, 5));
        int b1 = mesh.AppendVertex(new Vector3d(6, 5, 5));
        int c1 = mesh.AppendVertex(new Vector3d(5, 5, 5));
        int badTid = mesh.AppendTriangle(a1, b1, c1);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal(new[] { badTid }, issue.TriangleIds);
        Assert.DoesNotContain(issues, i => i.TriangleIds.Contains(goodTid));
    }
}
