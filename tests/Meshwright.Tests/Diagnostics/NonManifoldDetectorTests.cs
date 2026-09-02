using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class NonManifoldDetectorTests
{
    private static readonly NonManifoldDetector Detector = new();

    [Fact]
    public void Tetrahedron_HasNoNonManifoldIssues()
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
    public void EdgeSharedByThreeTriangles_IsFlaggedAsNonManifoldEdge()
    {
        // DMesh3 refuses to give a single (vertex-id) edge a third triangle, so the
        // third triangle here is built on its own coincident-but-distinct pair of
        // vertices — the same real-world shape unwelded/duplicated STL geometry
        // takes when three triangles meet along one spatial edge.
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v0b = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1b = mesh.AppendVertex(new Vector3d(1, 0, 0));

        int opposite1 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int opposite2 = mesh.AppendVertex(new Vector3d(0, -1, 0));
        int opposite3 = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(v0, v1, opposite1);
        mesh.AppendTriangle(v1, v0, opposite2);
        mesh.AppendTriangle(v0b, v1b, opposite3);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal("NonManifoldEdge", issue.Category);
        Assert.Equal(MeshIssueSeverity.Error, issue.Severity);
        Assert.Equal("Non-manifold edge shared by 3 triangles", issue.Message);
        Assert.Equal(3, issue.TriangleIds.Count);
        // Two distinct edge ids: (v0,v1) carrying 2 triangles, and (v0b,v1b) carrying the third.
        Assert.Equal(2, issue.EdgeIds.Count);
    }

    [Fact]
    public void TwoFansTouchingOnlyAtOneVertex_IsFlaggedAsNonManifoldVertex()
    {
        var mesh = new DMesh3();
        int shared = mesh.AppendVertex(new Vector3d(0, 0, 0));

        int fanA1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int fanA2 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int fanA3 = mesh.AppendVertex(new Vector3d(0, 1, 0));

        int fanB1 = mesh.AppendVertex(new Vector3d(-1, 0, 0));
        int fanB2 = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int fanB3 = mesh.AppendVertex(new Vector3d(0, -1, 0));

        mesh.AppendTriangle(shared, fanA1, fanA2);
        mesh.AppendTriangle(shared, fanA2, fanA3);

        mesh.AppendTriangle(shared, fanB1, fanB2);
        mesh.AppendTriangle(shared, fanB2, fanB3);

        IReadOnlyList<MeshIssue> issues = Detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal("NonManifoldEdge", issue.Category);
        Assert.Equal(MeshIssueSeverity.Error, issue.Severity);
        Assert.Equal("Non-manifold vertex where 2 separate surface fans meet", issue.Message);
        Assert.Equal(new[] { shared }, issue.VertexIds);
        Assert.Equal(4, issue.TriangleIds.Count);
    }
}
