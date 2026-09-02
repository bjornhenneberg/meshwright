using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Repair;

public class SmallShellRemovalRepairTests
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

    private static DMesh3 BuildLargeShellWithTinyFarShell()
    {
        DMesh3 mesh = BuildTetrahedron(Vector3d.Zero, 10.0);
        DMesh3 tiny = BuildTetrahedron(new Vector3d(1000, 1000, 1000), 0.1);
        AppendMesh(mesh, tiny);
        return mesh;
    }

    [Fact]
    public void RemoveShellsBelowVolumeFraction_TinyFarShell_RemovesTinyShellOnly()
    {
        DMesh3 mesh = BuildLargeShellWithTinyFarShell();

        var repair = new SmallShellRemovalRepair();
        SmallShellRemovalResult result = repair.RemoveShellsBelowVolumeFraction(mesh, minVolumeFraction: 0.01);

        Assert.Equal(1, result.ShellsRemoved);
        Assert.Equal(4, result.TrianglesRemoved);
        Assert.Equal(4, mesh.TriangleCount);

        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();
        Assert.Equal(1, components.Count);
    }

    [Fact]
    public void RemoveShellsBelowVolumeFraction_TwoRoughlyEqualShells_RemovesNothing()
    {
        DMesh3 mesh = BuildTetrahedron(Vector3d.Zero, 10.0);
        DMesh3 other = BuildTetrahedron(new Vector3d(1000, 1000, 1000), 10.0);
        AppendMesh(mesh, other);
        int triangleCountBefore = mesh.TriangleCount;

        var repair = new SmallShellRemovalRepair();
        SmallShellRemovalResult result = repair.RemoveShellsBelowVolumeFraction(mesh, minVolumeFraction: 0.01);

        Assert.Equal(0, result.ShellsRemoved);
        Assert.Equal(0, result.TrianglesRemoved);
        Assert.Equal(triangleCountBefore, mesh.TriangleCount);
    }

    [Fact]
    public void RemoveShellsBelowVolumeFraction_SingleShell_IsNoOp()
    {
        DMesh3 mesh = BuildTetrahedron(Vector3d.Zero, 10.0);
        int triangleCountBefore = mesh.TriangleCount;

        var repair = new SmallShellRemovalRepair();
        SmallShellRemovalResult result = repair.RemoveShellsBelowVolumeFraction(mesh, minVolumeFraction: 0.01);

        Assert.Equal(0, result.ShellsRemoved);
        Assert.Equal(0, result.TrianglesRemoved);
        Assert.Equal(triangleCountBefore, mesh.TriangleCount);
    }

    [Fact]
    public void Operation_Apply_RemovesSmallShell_AndPreview_LeavesCallersMeshUntouched()
    {
        DMesh3 mesh = BuildLargeShellWithTinyFarShell();
        int triangleCountBefore = mesh.TriangleCount;

        var operation = new RemoveSmallShellsOperation();

        OperationResult previewResult = operation.Preview(mesh);
        Assert.True(previewResult.Changed);
        Assert.Equal(triangleCountBefore, mesh.TriangleCount); // Preview must not mutate the caller's mesh.

        OperationResult applyResult = operation.Apply(mesh);
        Assert.True(applyResult.Changed);
        Assert.Contains("1 small disconnected shell", applyResult.Summary);
        Assert.Contains("4 triangles", applyResult.Summary);
        Assert.Equal(4, mesh.TriangleCount);
    }

    [Fact]
    public void Operation_SingleShell_ReportsNoChange()
    {
        DMesh3 mesh = BuildTetrahedron(Vector3d.Zero, 10.0);

        var operation = new RemoveSmallShellsOperation();
        OperationResult result = operation.Apply(mesh);

        Assert.False(result.Changed);
        Assert.Equal("No small disconnected shells found.", result.Summary);
    }

    [Fact]
    public void Operation_CustomThreshold_RemovesShellThatDefaultThresholdKeeps()
    {
        // tinyScale^3 / (tinyScale^3 + largeScale^3) = 27 / 1027 ~= 2.63% of total volume:
        // above the default 1% threshold (kept) but below a custom 5% threshold (removed).
        const double largeScale = 10.0;
        const double tinyScale = 3.0;

        DMesh3 defaultMesh = BuildTetrahedron(Vector3d.Zero, largeScale);
        AppendMesh(defaultMesh, BuildTetrahedron(new Vector3d(1000, 1000, 1000), tinyScale));
        int triangleCountBefore = defaultMesh.TriangleCount;

        OperationResult defaultResult = new RemoveSmallShellsOperation().Apply(defaultMesh);
        Assert.False(defaultResult.Changed);
        Assert.Equal(triangleCountBefore, defaultMesh.TriangleCount);

        DMesh3 customMesh = BuildTetrahedron(Vector3d.Zero, largeScale);
        AppendMesh(customMesh, BuildTetrahedron(new Vector3d(1000, 1000, 1000), tinyScale));

        OperationResult customResult = new RemoveSmallShellsOperation(minVolumeFraction: 0.05).Apply(customMesh);
        Assert.True(customResult.Changed);
        Assert.Equal(4, customMesh.TriangleCount);
    }
}
