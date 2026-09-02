using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class MeshDiagnosticsRunnerTests
{
    private sealed class FakeDetector : IMeshDetector
    {
        private readonly IReadOnlyList<MeshIssue> _issues;

        public FakeDetector(string category, IReadOnlyList<MeshIssue> issues)
        {
            Category = category;
            _issues = issues;
        }

        public string Category { get; }

        public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh) => _issues;
    }

    private static DMesh3 BuildUnitCube()
    {
        // Unit cube [0,1]^3, triangulated with outward-facing winding.
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

        Quad(v000, v010, v110, v100); // bottom (z=0), outward normal -z
        Quad(v001, v101, v111, v011); // top (z=1), outward normal +z
        Quad(v000, v100, v101, v001); // front (y=0), outward normal -y
        Quad(v010, v011, v111, v110); // back (y=1), outward normal +y
        Quad(v000, v001, v011, v010); // left (x=0), outward normal -x
        Quad(v100, v110, v111, v101); // right (x=1), outward normal +x

        return mesh;
    }

    [Fact]
    public void Run_MultipleDetectors_ConcatenatesIssuesInSuppliedOrder()
    {
        DMesh3 mesh = BuildUnitCube();
        var first = new FakeDetector("First", new[]
        {
            new MeshIssue("First", MeshIssueSeverity.Warning, "first-a"),
            new MeshIssue("First", MeshIssueSeverity.Warning, "first-b"),
        });
        var second = new FakeDetector("Second", new[]
        {
            new MeshIssue("Second", MeshIssueSeverity.Error, "second-a"),
        });

        MeshDiagnosticsReport report = MeshDiagnosticsRunner.Run(mesh, new[] { first, second });

        Assert.Equal(
            new[] { "first-a", "first-b", "second-a" },
            report.Issues.Select(issue => issue.Message));
    }

    [Fact]
    public void Run_NoDetectors_ReturnsEmptyIssuesWithStatistics()
    {
        DMesh3 mesh = BuildUnitCube();

        MeshDiagnosticsReport report = MeshDiagnosticsRunner.Run(mesh, Array.Empty<IMeshDetector>());

        Assert.Empty(report.Issues);
        Assert.Equal(12, report.Statistics.TriangleCount);
        Assert.Equal(8, report.Statistics.VertexCount);
    }

    [Fact]
    public void Run_UnitCube_PopulatesStatisticsCorrectly()
    {
        DMesh3 mesh = BuildUnitCube();

        MeshDiagnosticsReport report = MeshDiagnosticsRunner.Run(mesh, Array.Empty<IMeshDetector>());

        Assert.Equal(1.0, report.Statistics.Volume, 6);
        Assert.Equal(6.0, report.Statistics.SurfaceArea, 6);
        Assert.Equal(1, report.Statistics.ShellCount);
        Assert.Equal(new Vector3d(0, 0, 0), report.Statistics.BoundingBox.Min);
        Assert.Equal(new Vector3d(1, 1, 1), report.Statistics.BoundingBox.Max);
    }
}
