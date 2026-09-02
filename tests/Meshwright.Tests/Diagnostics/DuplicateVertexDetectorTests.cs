using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class DuplicateVertexDetectorTests
{
    private static readonly DuplicateVertexDetector Detector = new();

    [Fact]
    public void WeldedCube_HasNoDuplicateVertexIssues()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v4 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int v5 = mesh.AppendVertex(new Vector3d(1, 0, 1));
        int v6 = mesh.AppendVertex(new Vector3d(1, 1, 1));
        int v7 = mesh.AppendVertex(new Vector3d(0, 1, 1));

        // Bottom, top, and one side face are enough to exercise the detector;
        // full cube topology isn't required for this check.
        mesh.AppendTriangle(v0, v1, v2);
        mesh.AppendTriangle(v0, v2, v3);
        mesh.AppendTriangle(v4, v6, v5);
        mesh.AppendTriangle(v4, v7, v6);
        mesh.AppendTriangle(v0, v4, v5);
        mesh.AppendTriangle(v0, v5, v1);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        Assert.Empty(issues);
    }

    [Fact]
    public void TwoVerticesAtSameCoordinate_AreFlaggedAsOneGroupOfTwo()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int aDuplicate = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));

        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(aDuplicate, c, b);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal("DuplicateVertex", issue.Category);
        Assert.Equal(MeshIssueSeverity.Warning, issue.Severity);
        Assert.Equal("2 duplicate vertices at the same position", issue.Message);
        Assert.Equal(new[] { a, aDuplicate }.OrderBy(id => id), issue.VertexIds.OrderBy(id => id));
    }

    [Fact]
    public void ThreeVerticesAtSameCoordinate_AreFlaggedAsOneGroupOfThree()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(2, 2, 2));
        int aDuplicate1 = mesh.AppendVertex(new Vector3d(2, 2, 2));
        int aDuplicate2 = mesh.AppendVertex(new Vector3d(2, 2, 2));
        int b = mesh.AppendVertex(new Vector3d(3, 2, 2));
        int c = mesh.AppendVertex(new Vector3d(2, 3, 2));

        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(aDuplicate1, c, b);
        mesh.AppendTriangle(aDuplicate2, b, c);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal("DuplicateVertex", issue.Category);
        Assert.Equal(MeshIssueSeverity.Warning, issue.Severity);
        Assert.Equal("3 duplicate vertices at the same position", issue.Message);
        Assert.Equal(
            new[] { a, aDuplicate1, aDuplicate2 }.OrderBy(id => id),
            issue.VertexIds.OrderBy(id => id));
    }
}
