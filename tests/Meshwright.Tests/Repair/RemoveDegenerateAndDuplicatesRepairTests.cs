using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Repair;

public class RemoveDegenerateAndDuplicatesRepairTests
{
    [Fact]
    public void Run_DuplicateVertexFeedingASharedTriangle_WeldsVertexAndKeepsMeshValid()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int aDuplicate = mesh.AppendVertex(new Vector3d(0, 0, 0)); // coincident with a
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int d = mesh.AppendVertex(new Vector3d(1, 1, 0));

        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(aDuplicate, d, b); // references the duplicate id instead of a

        int originalVertexCount = mesh.VertexCount;
        int originalTriangleCount = mesh.TriangleCount;

        RemoveDegenerateAndDuplicatesResult result = RemoveDegenerateAndDuplicatesRepair.Run(mesh);

        Assert.Equal(1, result.MergedVertexCount);
        Assert.Equal(0, result.RemovedTriangleCount);
        Assert.Equal(originalVertexCount - 1, mesh.VertexCount);
        Assert.Equal(originalTriangleCount, mesh.TriangleCount);
        Assert.True(mesh.CheckValidity(bAllowNonManifoldVertices: true, eFailMode: FailMode.ReturnOnly));

        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Assert.True(mesh.IsVertex(tri.a));
            Assert.True(mesh.IsVertex(tri.b));
            Assert.True(mesh.IsVertex(tri.c));
        }
    }

    [Fact]
    public void Run_ZeroAreaSliverTriangle_IsRemovedAndWellFormedTriangleSurvives()
    {
        var mesh = new DMesh3();

        // Well-formed unit triangle: must survive.
        int a0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b0 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c0 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a0, b0, c0);

        // Sliver: three collinear points, zero area.
        int p0 = mesh.AppendVertex(new Vector3d(5, 5, 5));
        int p1 = mesh.AppendVertex(new Vector3d(6, 5, 5));
        int p2 = mesh.AppendVertex(new Vector3d(7, 5, 5));
        mesh.AppendTriangle(p0, p1, p2);

        RemoveDegenerateAndDuplicatesResult result = RemoveDegenerateAndDuplicatesRepair.Run(mesh);

        Assert.Equal(0, result.MergedVertexCount);
        Assert.Equal(1, result.RemovedTriangleCount);
        Assert.Equal(1, mesh.TriangleCount);

        Index3i survivor = mesh.GetTriangle(mesh.TriangleIndices().Single());
        var survivorPositions = new[] { mesh.GetVertex(survivor.a), mesh.GetVertex(survivor.b), mesh.GetVertex(survivor.c) };
        Assert.Contains(new Vector3d(0, 0, 0), survivorPositions);
        Assert.Contains(new Vector3d(1, 0, 0), survivorPositions);
        Assert.Contains(new Vector3d(0, 1, 0), survivorPositions);
    }

    [Fact]
    public void Run_CleanTetrahedron_ReportsNoChanges()
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

        int originalVertexCount = mesh.VertexCount;
        int originalTriangleCount = mesh.TriangleCount;

        RemoveDegenerateAndDuplicatesResult result = RemoveDegenerateAndDuplicatesRepair.Run(mesh);

        Assert.Equal(0, result.MergedVertexCount);
        Assert.Equal(0, result.RemovedTriangleCount);
        Assert.Equal(originalVertexCount, mesh.VertexCount);
        Assert.Equal(originalTriangleCount, mesh.TriangleCount);
    }

    [Fact]
    public void Operation_Apply_MutatesMesh_PreviewDoesNot()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int aDuplicate = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));

        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(aDuplicate, c, b);

        int originalVertexCount = mesh.VertexCount;
        int originalTriangleCount = mesh.TriangleCount;

        var operation = new RemoveDegenerateAndDuplicatesOperation();

        OperationResult previewResult = operation.Preview(mesh);
        Assert.True(previewResult.Changed);
        Assert.Equal("Merged 1 duplicate vertex.", previewResult.Summary);
        Assert.Equal(originalVertexCount, mesh.VertexCount);
        Assert.Equal(originalTriangleCount, mesh.TriangleCount);

        OperationResult applyResult = operation.Apply(mesh);
        Assert.True(applyResult.Changed);
        Assert.Equal("Merged 1 duplicate vertex.", applyResult.Summary);
        Assert.Equal(originalVertexCount - 1, mesh.VertexCount);
        Assert.Equal(originalTriangleCount, mesh.TriangleCount);
    }
}
