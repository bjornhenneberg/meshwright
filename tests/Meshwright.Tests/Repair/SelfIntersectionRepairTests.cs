using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Repair;

public class SelfIntersectionRepairTests
{
    // Same fixture pattern as SelfIntersectionDetectorTests.Detect_NonAdjacentTrianglesThatPierceEachOther:
    // triangle A lies in the z=0 plane and contains the origin; triangle B lies in the x=0 plane
    // and crosses z=0 exactly at the origin. Neither shares a vertex with the other.
    private static DMesh3 BuildTwoPiercingTriangles(out int triA, out int triB)
    {
        var mesh = new DMesh3();
        int a0 = mesh.AppendVertex(new Vector3d(-1, -1, 0));
        int a1 = mesh.AppendVertex(new Vector3d(1, -1, 0));
        int a2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        triA = mesh.AppendTriangle(a0, a1, a2);

        int b0 = mesh.AppendVertex(new Vector3d(0, 0, -1));
        int b1 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int b2 = mesh.AppendVertex(new Vector3d(0, 2, 0));
        triB = mesh.AppendTriangle(b0, b1, b2);

        return mesh;
    }

    private static DMesh3 BuildCleanTetrahedron()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        mesh.AppendTriangle(v0, v2, v1);
        mesh.AppendTriangle(v0, v1, v3);
        mesh.AppendTriangle(v0, v3, v2);
        mesh.AppendTriangle(v1, v2, v3);
        return mesh;
    }

    [Fact]
    public void Resolve_PiercingTriangles_RemovesBothAndClearsIntersection()
    {
        DMesh3 mesh = BuildTwoPiercingTriangles(out int triA, out int triB);

        var repair = new SelfIntersectionRepair();
        SelfIntersectionRepair.Result result = repair.Resolve(mesh);

        Assert.Equal(1, result.PairsFound);
        Assert.Equal(2, result.TrianglesRemoved);
        Assert.False(mesh.IsTriangle(triA));
        Assert.False(mesh.IsTriangle(triB));

        Assert.Empty(new SelfIntersectionDetector().Detect(mesh));
    }

    [Fact]
    public void Resolve_CleanTetrahedron_NoOp()
    {
        DMesh3 mesh = BuildCleanTetrahedron();
        int originalTriangleCount = mesh.TriangleCount;

        var repair = new SelfIntersectionRepair();
        SelfIntersectionRepair.Result result = repair.Resolve(mesh);

        Assert.Equal(0, result.PairsFound);
        Assert.Equal(0, result.TrianglesRemoved);
        Assert.Equal(originalTriangleCount, mesh.TriangleCount);
    }

    [Fact]
    public void ResolveSelfIntersectionsOperation_ApplyMutatesPreviewDoesNot()
    {
        DMesh3 mesh = BuildTwoPiercingTriangles(out _, out _);
        var operation = new ResolveSelfIntersectionsOperation();

        OperationResult previewResult = operation.Preview(mesh);
        Assert.True(previewResult.Changed);
        // Preview must not have mutated the caller's mesh: both triangles are still there and
        // still intersect.
        Assert.Equal(2, mesh.TriangleCount);
        Assert.NotEmpty(new SelfIntersectionDetector().Detect(mesh));

        OperationResult applyResult = operation.Apply(mesh);
        Assert.True(applyResult.Changed);
        Assert.Empty(new SelfIntersectionDetector().Detect(mesh));
    }

    [Fact]
    public void ResolveSelfIntersectionsOperation_CleanMesh_ReportsNoChange()
    {
        DMesh3 mesh = BuildCleanTetrahedron();
        var operation = new ResolveSelfIntersectionsOperation();

        OperationResult result = operation.Apply(mesh);

        Assert.False(result.Changed);
        Assert.Equal("No self-intersections found.", result.Summary);
    }
}
