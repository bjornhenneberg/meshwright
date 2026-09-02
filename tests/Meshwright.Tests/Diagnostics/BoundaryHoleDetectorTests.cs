using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class BoundaryHoleDetectorTests
{
    private static DMesh3 BuildTetrahedron()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(v0, v2, v1); // bottom
        mesh.AppendTriangle(v0, v1, v3);
        mesh.AppendTriangle(v1, v2, v3);
        mesh.AppendTriangle(v2, v0, v3);

        return mesh;
    }

    private static DMesh3 BuildCubeMissingOneFace()
    {
        // Unit cube [0,1]^3, triangulated with outward-facing winding, top face omitted.
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v110 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int v010 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int v101 = mesh.AppendVertex(new Vector3d(1, 0, 1));
        int v111 = mesh.AppendVertex(new Vector3d(1, 1, 1));
        int v011 = mesh.AppendVertex(new Vector3d(0, 1, 1));

        void Quad(int a, int b, int c, int d)
        {
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, d);
        }

        Quad(v000, v010, v110, v100); // bottom (z=0)
        // top (z=1) omitted -> one boundary loop of 4 edges
        Quad(v000, v100, v101, v001); // front (y=0)
        Quad(v010, v011, v111, v110); // back (y=1)
        Quad(v000, v001, v011, v010); // left (x=0)
        Quad(v100, v110, v111, v101); // right (x=1)

        return mesh;
    }

    private static DMesh3 BuildCubeWithTwoHoles()
    {
        // Unit cube [0,1]^3, top and bottom faces both omitted -> two boundary loops.
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v110 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int v010 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int v101 = mesh.AppendVertex(new Vector3d(1, 0, 1));
        int v111 = mesh.AppendVertex(new Vector3d(1, 1, 1));
        int v011 = mesh.AppendVertex(new Vector3d(0, 1, 1));

        void Quad(int a, int b, int c, int d)
        {
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, d);
        }

        // bottom (z=0) omitted
        // top (z=1) omitted
        Quad(v000, v100, v101, v001); // front (y=0)
        Quad(v010, v011, v111, v110); // back (y=1)
        Quad(v000, v001, v011, v010); // left (x=0)
        Quad(v100, v110, v111, v101); // right (x=1)

        return mesh;
    }

    [Fact]
    public void Detect_ClosedTetrahedron_ReportsNoIssues()
    {
        DMesh3 mesh = BuildTetrahedron();
        var detector = new BoundaryHoleDetector();

        IReadOnlyList<MeshIssue> issues = detector.Detect(mesh);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_CubeMissingOneFace_ReportsOneHoleWithFourEdges()
    {
        DMesh3 mesh = BuildCubeMissingOneFace();
        var detector = new BoundaryHoleDetector();

        IReadOnlyList<MeshIssue> issues = detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal("BoundaryHole", issue.Category);
        Assert.Equal(MeshIssueSeverity.Error, issue.Severity);
        Assert.Equal(4, issue.EdgeIds.Count);
        Assert.Equal(4, issue.VertexIds.Count);
        Assert.Contains("4 edges", issue.Message);
    }

    [Fact]
    public void Detect_CubeWithTwoHoles_ReportsTwoBoundaryLoops()
    {
        DMesh3 mesh = BuildCubeWithTwoHoles();
        var detector = new BoundaryHoleDetector();

        IReadOnlyList<MeshIssue> issues = detector.Detect(mesh);

        Assert.Equal(2, issues.Count);
        Assert.All(issues, issue =>
        {
            Assert.Equal("BoundaryHole", issue.Category);
            Assert.Equal(4, issue.EdgeIds.Count);
        });
    }
}
