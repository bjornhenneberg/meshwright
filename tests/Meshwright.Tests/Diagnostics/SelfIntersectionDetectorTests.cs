using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class SelfIntersectionDetectorTests
{
    [Fact]
    public void Detect_Tetrahedron_ReportsNoIssues()
    {
        // Every pair of faces in a tetrahedron shares an edge (two vertices),
        // so all pairs are adjacent and none should be tested for intersection.
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        mesh.AppendTriangle(v0, v2, v1);
        mesh.AppendTriangle(v0, v1, v3);
        mesh.AppendTriangle(v0, v3, v2);
        mesh.AppendTriangle(v1, v2, v3);

        var detector = new SelfIntersectionDetector();
        var issues = detector.Detect(mesh);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_NonAdjacentTrianglesFarApart_ReportsNoIssues()
    {
        var mesh = new DMesh3();
        int a0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int a1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int a2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a0, a1, a2);

        int b0 = mesh.AppendVertex(new Vector3d(100, 100, 100));
        int b1 = mesh.AppendVertex(new Vector3d(101, 100, 100));
        int b2 = mesh.AppendVertex(new Vector3d(100, 101, 100));
        mesh.AppendTriangle(b0, b1, b2);

        var detector = new SelfIntersectionDetector();
        var issues = detector.Detect(mesh);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_NonAdjacentTrianglesThatPierceEachOther_ReportsOneIssue()
    {
        // Triangle A lies in the z=0 plane; at y=0 its span is x in [-0.5, 0.5],
        // so the origin (0,0,0) lies strictly inside it.
        var mesh = new DMesh3();
        int a0 = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int a1 = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int a2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int triA = mesh.AppendTriangle(a0, a1, a2);

        // Triangle B lies in the x=0 plane; its edge from (0,0,-1) to (0,0,1)
        // crosses z=0 exactly at the origin, which is inside triangle A.
        int b0 = mesh.AppendVertex(new Vector3d(0, 0, -1));
        int b1 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int b2 = mesh.AppendVertex(new Vector3d(0, 2, 0));
        int triB = mesh.AppendTriangle(b0, b1, b2);

        var detector = new SelfIntersectionDetector();
        var issues = detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal("SelfIntersection", issue.Category);
        Assert.Equal(MeshIssueSeverity.Error, issue.Severity);
        Assert.Equal(new[] { triA, triB }, issue.TriangleIds);
    }
}
