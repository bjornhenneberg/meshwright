using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class DisconnectedShellDetectorTests
{
    private static DMesh3 BuildTetrahedron(Vector3d origin, double scale)
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(origin + new Vector3d(0, 0, 0) * scale);
        int b = mesh.AppendVertex(origin + new Vector3d(1, 0, 0) * scale);
        int c = mesh.AppendVertex(origin + new Vector3d(0, 1, 0) * scale);
        int d = mesh.AppendVertex(origin + new Vector3d(0, 0, 1) * scale);

        mesh.AppendTriangle(a, c, b); // base, outward normal -z
        mesh.AppendTriangle(a, b, d);
        mesh.AppendTriangle(b, c, d);
        mesh.AppendTriangle(c, a, d);

        return mesh;
    }

    private static DMesh3 BuildCube(Vector3d origin, double size)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(origin + new Vector3d(0, 0, 0) * size);
        int v100 = mesh.AppendVertex(origin + new Vector3d(1, 0, 0) * size);
        int v110 = mesh.AppendVertex(origin + new Vector3d(1, 1, 0) * size);
        int v010 = mesh.AppendVertex(origin + new Vector3d(0, 1, 0) * size);
        int v001 = mesh.AppendVertex(origin + new Vector3d(0, 0, 1) * size);
        int v101 = mesh.AppendVertex(origin + new Vector3d(1, 0, 1) * size);
        int v111 = mesh.AppendVertex(origin + new Vector3d(1, 1, 1) * size);
        int v011 = mesh.AppendVertex(origin + new Vector3d(0, 1, 1) * size);

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
        }

        Quad(v000, v010, v110, v100); // bottom (z=0), outward normal -z
        Quad(v001, v101, v111, v011); // top (z=1), outward normal +z
        Quad(v000, v100, v101, v001); // front (y=0), outward normal -y
        Quad(v010, v011, v111, v110); // back (y=1), outward normal +y
        Quad(v000, v001, v011, v010); // left (x=0), outward normal -x
        Quad(v100, v110, v111, v101); // right (x=1), outward normal +x

        return mesh;
    }

    private static void AppendMesh(DMesh3 target, DMesh3 source)
    {
        var remap = new Dictionary<int, int>();
        foreach (int vid in source.VertexIndices())
        {
            remap[vid] = target.AppendVertex(source.GetVertex(vid));
        }

        foreach (int tid in source.TriangleIndices())
        {
            Index3i tri = source.GetTriangle(tid);
            target.AppendTriangle(remap[tri.a], remap[tri.b], remap[tri.c]);
        }
    }

    [Fact]
    public void Detect_SingleShell_ReportsNoIssues()
    {
        DMesh3 mesh = BuildTetrahedron(Vector3d.Zero, 1.0);

        var detector = new DisconnectedShellDetector();
        IReadOnlyList<MeshIssue> issues = detector.Detect(mesh);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_LargeCubeWithTinyFarTetrahedron_FlagsOnlyTheTinyShell()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);
        DMesh3 tiny = BuildTetrahedron(new Vector3d(1000, 1000, 1000), 0.1);
        AppendMesh(mesh, tiny);

        var detector = new DisconnectedShellDetector();
        IReadOnlyList<MeshIssue> issues = detector.Detect(mesh);

        MeshIssue issue = Assert.Single(issues);
        Assert.Equal("DisconnectedShell", issue.Category);
        Assert.Equal(MeshIssueSeverity.Warning, issue.Severity);
        Assert.Equal(4, issue.TriangleIds.Count);
        Assert.Contains("Stray disconnected shell", issue.Message);
        Assert.Contains("4 triangles", issue.Message);

        double cubeVolume = 1000.0;
        double tinyVolume = (0.1 * 0.1 * 0.1) / 6.0;
        double expectedPercent = (tinyVolume / (cubeVolume + tinyVolume)) * 100.0;
        Assert.Contains($"{expectedPercent:0.##}%", issue.Message);
    }

    [Fact]
    public void Detect_LargeCubeWithTinyFarTetrahedron_NeverFlagsTheLargeShell()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);
        DMesh3 tiny = BuildTetrahedron(new Vector3d(1000, 1000, 1000), 0.1);
        AppendMesh(mesh, tiny);

        var detector = new DisconnectedShellDetector();
        IReadOnlyList<MeshIssue> issues = detector.Detect(mesh);

        foreach (MeshIssue issue in issues)
        {
            Assert.DoesNotContain(issue.TriangleIds, id => id < 12);
        }
    }
}
