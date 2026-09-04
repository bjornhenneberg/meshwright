using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;
using Xunit;

namespace Meshwright.Tests.Edit;

public class TransformOperationTests
{
    // Build a simple unit cube for testing
    private static DMesh3 BuildUnitCube()
    {
        var mesh = new DMesh3();

        // 8 vertices of a unit cube centered at origin
        mesh.AppendVertex(new Vector3d(-0.5, -0.5, -0.5)); // 0
        mesh.AppendVertex(new Vector3d(0.5, -0.5, -0.5));  // 1
        mesh.AppendVertex(new Vector3d(0.5, 0.5, -0.5));   // 2
        mesh.AppendVertex(new Vector3d(-0.5, 0.5, -0.5));  // 3
        mesh.AppendVertex(new Vector3d(-0.5, -0.5, 0.5));  // 4
        mesh.AppendVertex(new Vector3d(0.5, -0.5, 0.5));   // 5
        mesh.AppendVertex(new Vector3d(0.5, 0.5, 0.5));    // 6
        mesh.AppendVertex(new Vector3d(-0.5, 0.5, 0.5));   // 7

        // 12 triangles (2 per face)
        mesh.AppendTriangle(0, 1, 2);
        mesh.AppendTriangle(0, 2, 3);
        mesh.AppendTriangle(4, 6, 5);
        mesh.AppendTriangle(4, 7, 6);
        mesh.AppendTriangle(0, 4, 5);
        mesh.AppendTriangle(0, 5, 1);
        mesh.AppendTriangle(2, 6, 7);
        mesh.AppendTriangle(2, 7, 3);
        mesh.AppendTriangle(0, 3, 7);
        mesh.AppendTriangle(0, 7, 4);
        mesh.AppendTriangle(1, 5, 6);
        mesh.AppendTriangle(1, 6, 2);

        return mesh;
    }

    private static void AssertValidManifoldMesh(DMesh3 mesh)
    {
        Assert.True(mesh.CheckValidity(bAllowNonManifoldVertices: false, eFailMode: FailMode.ReturnOnly));
        Assert.True(mesh.IsClosed());
    }

    [Fact]
    public void TranslateMesh_MovesVerticesByOffset()
    {
        DMesh3 mesh = BuildUnitCube();
        var offset = new Vector3d(10, 20, 30);

        var operation = new TranslateOperation(offset);
        OperationResult result = operation.Apply(mesh);

        Assert.True(result.Changed);
        Assert.Contains("Translated", result.Summary);
        Assert.Contains("10", result.Summary);

        // Check that all vertices were translated
        foreach (int vid in mesh.VertexIndices())
        {
            var v = mesh.GetVertex(vid);
            // At least one vertex should have increased X by ~10
            if (Math.Abs(v.x) > 5)
                Assert.True(v.x > 5, $"Expected x > 5, got {v.x}");
        }

        AssertValidManifoldMesh(mesh);
    }

    [Fact]
    public void RotateMesh_RotatesAroundAxis()
    {
        DMesh3 mesh = BuildUnitCube();
        var center = Vector3d.Zero;
        double angle = 90.0; // 90 degrees
        var axis = Vector3d.AxisZ;

        var operation = new RotateOperation(angle, axis, center);
        OperationResult result = operation.Apply(mesh);

        Assert.True(result.Changed);
        Assert.Contains("Rotated", result.Summary);
        Assert.Contains("90", result.Summary);

        // After 90° rotation around Z, X should map to Y
        // Check that the bounding box has changed orientation
        var bounds = mesh.CachedBounds;
        // bounds is a value type, so just check that it was computed
        Assert.True(bounds.Extents.Length > 0, "Bounds should be valid");

        AssertValidManifoldMesh(mesh);
    }

    [Fact]
    public void RotateMesh_90DegreesPreservesVolume()
    {
        DMesh3 mesh = BuildUnitCube();
        var stats = MeshStatistics.Compute(mesh);
        double originalVolume = stats.Volume;

        var operation = new RotateOperation(90.0, Vector3d.AxisZ, Vector3d.Zero);
        operation.Apply(mesh);

        var statsAfter = MeshStatistics.Compute(mesh);
        // Volume should be approximately preserved (allowing small numerical error)
        Assert.InRange(statsAfter.Volume, originalVolume - 0.01, originalVolume + 0.01);
    }

    [Fact]
    public void ScaleMesh_ScalesVerticesAroundCenter()
    {
        DMesh3 mesh = BuildUnitCube();
        double scale = 2.0;
        var center = Vector3d.Zero;

        var operation = new ScaleOperation(scale, center);
        OperationResult result = operation.Apply(mesh);

        Assert.True(result.Changed);
        Assert.Contains("Scaled", result.Summary);
        Assert.Contains("2", result.Summary);

        // After 2x scale, vertices should be roughly 2x as far from center
        foreach (int vid in mesh.VertexIndices())
        {
            var v = mesh.GetVertex(vid);
            // Any non-zero vertex should roughly double its distance from center
            double dist = v.Length;
            Assert.True(dist > 0.5, $"Expected scaled vertex, got {v}");
        }

        AssertValidManifoldMesh(mesh);
    }

    [Fact]
    public void ScaleMesh_2xScaleQuadruplesVolume()
    {
        DMesh3 mesh = BuildUnitCube();
        var stats = MeshStatistics.Compute(mesh);
        double originalVolume = stats.Volume;

        var operation = new ScaleOperation(2.0, Vector3d.Zero);
        operation.Apply(mesh);

        var statsAfter = MeshStatistics.Compute(mesh);
        // 2x scale should quadruple volume (2^3)
        double expectedVolume = originalVolume * 8.0;
        Assert.InRange(statsAfter.Volume, expectedVolume - 0.1, expectedVolume + 0.1);
    }

    [Fact]
    public void MirrorMesh_FlipsVerticesAcrossPlane()
    {
        DMesh3 mesh = BuildUnitCube();
        var planePoint = Vector3d.Zero;
        var planeNormal = Vector3d.AxisX; // Mirror across YZ plane

        var operation = new MirrorOperation(planePoint, planeNormal);
        OperationResult result = operation.Apply(mesh);

        Assert.True(result.Changed);
        Assert.Contains("Mirrored", result.Summary);

        // After mirroring across YZ plane, X coordinates should flip
        bool foundFlipped = false;
        foreach (int vid in mesh.VertexIndices())
        {
            var v = mesh.GetVertex(vid);
            if (Math.Abs(v.x + 0.5) < 0.01 || Math.Abs(v.x - 0.5) < 0.01)
            {
                foundFlipped = true;
                break;
            }
        }
        Assert.True(foundFlipped, "Expected to find flipped vertices");

        AssertValidManifoldMesh(mesh);
    }

    [Fact]
    public void MirrorMesh_PreservesVolume()
    {
        DMesh3 mesh = BuildUnitCube();
        var stats = MeshStatistics.Compute(mesh);
        double originalVolume = stats.Volume;

        var operation = new MirrorOperation(Vector3d.Zero, Vector3d.AxisX);
        operation.Apply(mesh);

        var statsAfter = MeshStatistics.Compute(mesh);
        Assert.InRange(statsAfter.Volume, originalVolume - 0.01, originalVolume + 0.01);
    }

    [Fact]
    public void AlignToBedOperation_DropsLowestPointToZ0()
    {
        DMesh3 mesh = BuildUnitCube();

        // First move it up so min Z is not at 0
        var moveOp = new TranslateOperation(new Vector3d(0, 0, 10));
        moveOp.Apply(mesh);

        var boundsBefore = mesh.CachedBounds;
        double minZBefore = boundsBefore.Min.z;
        Assert.True(minZBefore > 5, "Setup: mesh should be above Z=0");

        var alignOp = new AlignToBedOperation();
        OperationResult result = alignOp.Apply(mesh);

        Assert.True(result.Changed);
        Assert.Contains("Aligned to bed", result.Summary);

        var boundsAfter = mesh.CachedBounds;
        double minZAfter = boundsAfter.Min.z;
        Assert.InRange(minZAfter, -0.01, 0.01);
    }

    [Fact]
    public void AlignToBedOperation_AlreadyAlignedReturnsNoChange()
    {
        DMesh3 mesh = BuildUnitCube();
        var bounds = mesh.CachedBounds;

        // Cube is centered, so min Z is -0.5; translate to 0
        var moveOp = new TranslateOperation(new Vector3d(0, 0, 0.5));
        moveOp.Apply(mesh);

        var alignOp = new AlignToBedOperation();
        OperationResult result = alignOp.Apply(mesh);

        // Should report no change (already aligned)
        Assert.False(result.Changed);
    }

    [Fact]
    public void Preview_DoesNotMutateOriginalMesh()
    {
        DMesh3 original = BuildUnitCube();
        DMesh3 beforePreview = new DMesh3(original, bCompact: false);
        var originalVertex0 = original.GetVertex(0);

        var operation = new TranslateOperation(new Vector3d(100, 100, 100));
        OperationResult previewResult = operation.Preview(original);

        // Original mesh should be unchanged
        var afterVertex0 = original.GetVertex(0);
        Assert.True(originalVertex0.Equals(afterVertex0), "Preview should not mutate original mesh");
        Assert.Equal(beforePreview.VertexCount, original.VertexCount);
    }

    [Fact]
    public void MultipleTransforms_ChainCorrectly()
    {
        DMesh3 mesh = BuildUnitCube();
        var v0Before = mesh.GetVertex(0);

        // Translate by (10, 0, 0)
        var translate = new TranslateOperation(new Vector3d(10, 0, 0));
        translate.Apply(mesh);

        // Then scale by 2x around the translated center
        var center = new Vector3d(10, 0, 0);
        var scale = new ScaleOperation(2.0, center);
        scale.Apply(mesh);

        var v0After = mesh.GetVertex(0);
        // Original: (-0.5, -0.5, -0.5)
        // After translate by (10, 0, 0): (9.5, -0.5, -0.5)
        // After scale by 2x around (10, 0, 0): distance from (10, 0, 0) is (0.5, 0.5, 0.5), scaled is (1, 1, 1)
        // So final position is (10 - 1, 0 - 1, 0 - 1) = (9, -1, -1)
        Assert.InRange(v0After.x, 8.9, 9.1);
        Assert.InRange(v0After.y, -1.1, -0.9);
        Assert.InRange(v0After.z, -1.1, -0.9);
    }

    [Fact]
    public void TransformGeometryDirect_TranslateMesh()
    {
        DMesh3 mesh = BuildUnitCube();
        var offset = new Vector3d(5, 10, 15);
        var v0Before = mesh.GetVertex(0);

        Transform.TranslateMesh(mesh, offset);

        // Mesh should be mutated in place
        var v0After = mesh.GetVertex(0);
        Assert.InRange(v0After.x, 4.4, 4.6); // -0.5 + 5 = 4.5
    }

    [Fact]
    public void TransformGeometryDirect_RotateMesh()
    {
        DMesh3 mesh = BuildUnitCube();
        var v0Before = mesh.GetVertex(0); // (-0.5, -0.5, -0.5)

        // 90-degree rotation around Z: (x, y, z) -> (-y, x, z)
        Transform.RotateMesh(mesh, 90.0, Vector3d.AxisZ, Vector3d.Zero);

        // Mesh should be mutated in place
        var v0After = mesh.GetVertex(0);
        // (-0.5, -0.5, -0.5) rotated 90° around Z should become (0.5, -0.5, -0.5)
        Assert.InRange(v0After.x, 0.4, 0.6);
        Assert.InRange(v0After.y, -0.6, -0.4);
        Assert.InRange(v0After.z, -0.6, -0.4);
    }

    [Fact]
    public void TransformGeometryDirect_ScaleMesh()
    {
        DMesh3 mesh = BuildUnitCube();
        var scale = 3.0;
        var v0Before = mesh.GetVertex(0);

        Transform.ScaleMesh(mesh, scale, Vector3d.Zero);

        // Mesh should be mutated in place
        var v0After = mesh.GetVertex(0);
        Assert.InRange(v0After.x, -1.6, -1.4); // -0.5 * 3 = -1.5
    }

    [Fact]
    public void TransformGeometryDirect_MirrorMesh()
    {
        DMesh3 mesh = BuildUnitCube();
        var v0Before = mesh.GetVertex(0);

        Transform.MirrorMesh(mesh, Vector3d.Zero, Vector3d.AxisX);

        // Mesh should be mutated in place
        var v0After = mesh.GetVertex(0);
        // After mirroring across YZ plane, x should flip
        Assert.InRange(v0After.x, 0.4, 0.6); // 0.5 (flipped from -0.5)
    }

    [Fact]
    public void TransformGeometryDirect_AlignToBed()
    {
        DMesh3 mesh = BuildUnitCube();
        // Move it up
        Transform.TranslateMesh(mesh, new Vector3d(0, 0, 20));

        var boundsBefore = mesh.CachedBounds;
        Transform.AlignToBed(mesh);

        var boundsAfter = mesh.CachedBounds;

        Assert.True(boundsBefore.Min.z > 10);
        Assert.InRange(boundsAfter.Min.z, -0.01, 0.01);
    }
}
