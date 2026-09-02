using g3;
using Meshwright.Core;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests;

public class MeshDocumentTests
{
    [Fact]
    public void Load_CleanTetrahedron_PopulatesMeshAndReportWithNoIssues()
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

        var document = new MeshDocument();
        document.Load(mesh);

        Assert.Same(mesh, document.Mesh);
        Assert.NotNull(document.Report);
        Assert.Equal(4, document.Report!.Statistics.TriangleCount);
        Assert.Empty(document.Report.Issues);
        Assert.Equal("No issues found.", document.Report.Summary);
    }

    [Fact]
    public void Load_MeshWithNonManifoldEdge_ReportsIssueViaAllSevenDetectors()
    {
        // Three triangles sharing one spatial edge (built with duplicated coincident vertices,
        // as DMesh3 itself refuses a third triangle on one vertex-id edge) — the same shape
        // unwelded STL geometry takes. Exercises NonManifoldDetector end-to-end through
        // MeshDocument without needing to know every other detector's fixture requirements.
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

        var document = new MeshDocument();
        document.Load(mesh);

        MeshDiagnosticsReport report = document.Report!;
        Assert.Contains(report.Issues, issue => issue.Category == "NonManifoldEdge");
        Assert.Contains("non-manifold edge", report.Summary);
    }
}
