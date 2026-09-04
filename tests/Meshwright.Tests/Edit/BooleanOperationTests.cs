using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;
using Xunit;

namespace Meshwright.Tests.Edit;

public class BooleanOperationTests
{
    /// <summary>
    /// Create a simple cube mesh at the origin (±0.5 in each dimension).
    /// </summary>
    private static DMesh3 CreateUnitCube()
    {
        var mesh = new DMesh3(true);
        double s = 0.5;

        var v0 = mesh.AppendVertex(new Vector3d(-s, -s, -s));
        var v1 = mesh.AppendVertex(new Vector3d(s, -s, -s));
        var v2 = mesh.AppendVertex(new Vector3d(s, s, -s));
        var v3 = mesh.AppendVertex(new Vector3d(-s, s, -s));
        var v4 = mesh.AppendVertex(new Vector3d(-s, -s, s));
        var v5 = mesh.AppendVertex(new Vector3d(s, -s, s));
        var v6 = mesh.AppendVertex(new Vector3d(s, s, s));
        var v7 = mesh.AppendVertex(new Vector3d(-s, s, s));

        // Bottom face
        mesh.AppendTriangle(v0, v2, v1);
        mesh.AppendTriangle(v0, v3, v2);

        // Top face
        mesh.AppendTriangle(v4, v5, v6);
        mesh.AppendTriangle(v4, v6, v7);

        // Front face
        mesh.AppendTriangle(v0, v1, v5);
        mesh.AppendTriangle(v0, v5, v4);

        // Back face
        mesh.AppendTriangle(v2, v3, v7);
        mesh.AppendTriangle(v2, v7, v6);

        // Left face
        mesh.AppendTriangle(v0, v7, v3);
        mesh.AppendTriangle(v0, v4, v7);

        // Right face
        mesh.AppendTriangle(v1, v6, v5);
        mesh.AppendTriangle(v1, v2, v6);

        return mesh;
    }

    /// <summary>
    /// Create a unit cube offset by the given vector.
    /// </summary>
    private static DMesh3 CreateOffsetCube(double offsetX, double offsetY, double offsetZ)
    {
        var mesh = CreateUnitCube();
        foreach (int vid in mesh.VertexIndices())
        {
            var pos = mesh.GetVertex(vid);
            mesh.SetVertex(vid, pos + new Vector3d(offsetX, offsetY, offsetZ));
        }
        return mesh;
    }

    private static void AssertValidManifoldMesh(DMesh3 mesh)
    {
        Assert.True(mesh.CheckValidity(bAllowNonManifoldVertices: false, eFailMode: FailMode.ReturnOnly));
        Assert.True(mesh.IsClosed());
        Assert.Empty(new NonManifoldDetector().Detect(mesh));
        Assert.Empty(new DegenerateTriangleDetector().Detect(mesh));
    }

    [Fact]
    public void Union_NonOverlappingCubes_ProducesSingleValidMesh()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(1.5, 0.0, 0.0);

        DMesh3 result = BooleanOperations.Union(cube1, cube2);

        Assert.True(result.TriangleCount > 0);
        Assert.True(result.VertexCount > 0);
        AssertValidManifoldMesh(result);
    }

    [Fact]
    public void Union_OverlappingCubes_ProducesMeshWithReducedVolume()
    {
        var cube1 = CreateUnitCube(); // volume ≈ 1.0
        var cube2 = CreateOffsetCube(0.5, 0.5, 0.0); // overlaps partially

        var stats1 = MeshStatistics.Compute(cube1);
        var stats2 = MeshStatistics.Compute(cube2);

        DMesh3 result = BooleanOperations.Union(cube1, cube2);
        var statsResult = MeshStatistics.Compute(result);

        // Union volume should be less than sum of inputs (due to overlap)
        double sumVolumes = stats1.Volume + stats2.Volume;
        Assert.True(
            statsResult.Volume < sumVolumes && statsResult.Volume > 0,
            $"Expected union volume < {sumVolumes}, got {statsResult.Volume}");

        AssertValidManifoldMesh(result);
    }

    [Fact]
    public void Difference_SubtractsSmallerCubeFromLarger()
    {
        var largeCube = CreateUnitCube();
        var smallCube = CreateOffsetCube(0.1, 0.1, 0.0); // smaller, overlapping

        var statsLarge = MeshStatistics.Compute(largeCube);

        DMesh3 result = BooleanOperations.Difference(largeCube, smallCube);
        var statsResult = MeshStatistics.Compute(result);

        // Result volume should be less than the original
        Assert.True(
            statsResult.Volume < statsLarge.Volume && statsResult.Volume > 0,
            $"Expected difference volume < {statsLarge.Volume}, got {statsResult.Volume}");

        AssertValidManifoldMesh(result);
    }

    [Fact]
    public void Difference_NonOverlappingCubes_ProducesOriginalMesh()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(2.0, 2.0, 2.0); // no overlap

        var stats1 = MeshStatistics.Compute(cube1);

        DMesh3 result = BooleanOperations.Difference(cube1, cube2);
        var statsResult = MeshStatistics.Compute(result);

        // No overlap means result should be close to the original
        Assert.True(
            System.Math.Abs(statsResult.Volume - stats1.Volume) < 0.01,
            $"Expected volume ≈ {stats1.Volume}, got {statsResult.Volume}");
    }

    [Fact]
    public void Intersection_OverlappingCubes_ProducesOverlapRegion()
    {
        var cube1 = CreateUnitCube(); // volume ≈ 1.0
        var cube2 = CreateOffsetCube(0.5, 0.0, 0.0); // overlaps half the volume

        var stats1 = MeshStatistics.Compute(cube1);

        DMesh3 result = BooleanOperations.Intersection(cube1, cube2);
        var statsResult = MeshStatistics.Compute(result);

        // Intersection should have less volume than either input
        Assert.True(
            statsResult.Volume > 0 && statsResult.Volume < stats1.Volume,
            $"Expected intersection volume between 0 and {stats1.Volume}, got {statsResult.Volume}");

        AssertValidManifoldMesh(result);
    }

    [Fact]
    public void Intersection_NonOverlappingCubes_Throws()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(2.0, 2.0, 2.0); // no overlap

        // Non-overlapping intersection should throw
        var ex = Assert.Throws<InvalidOperationException>(() =>
            BooleanOperations.Intersection(cube1, cube2));

        Assert.Contains("empty", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BooleanUnionOperation_ChangesPredicateIsTrue()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(0.5, 0.5, 0.0);

        var operation = new BooleanUnionOperation(cube2);
        OperationResult result = operation.Apply(cube1);

        Assert.True(result.Changed);
        Assert.Contains("Union", result.Summary);
        Assert.Contains("triangles", result.Summary, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BooleanDifferenceOperation_ChangesPredicateIsTrue()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(0.1, 0.1, 0.0);

        var operation = new BooleanDifferenceOperation(cube2);
        OperationResult result = operation.Apply(cube1);

        Assert.True(result.Changed);
        Assert.Contains("Difference", result.Summary);
    }

    [Fact]
    public void BooleanIntersectionOperation_ChangesPredicateIsTrue()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(0.5, 0.0, 0.0);

        var operation = new BooleanIntersectionOperation(cube2);
        OperationResult result = operation.Apply(cube1);

        Assert.True(result.Changed);
        Assert.Contains("Intersection", result.Summary);
    }

    [Fact]
    public void BooleanUnionOperation_Preview_DoesNotMutate()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(0.5, 0.5, 0.0);
        int beforeTriCount = cube1.TriangleCount;

        var operation = new BooleanUnionOperation(cube2);
        OperationResult previewResult = operation.Preview(cube1);

        Assert.True(previewResult.Changed);
        Assert.Equal(beforeTriCount, cube1.TriangleCount);
    }

    [Fact]
    public void BooleanDifferenceOperation_Preview_DoesNotMutate()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(0.1, 0.1, 0.0);
        int beforeTriCount = cube1.TriangleCount;

        var operation = new BooleanDifferenceOperation(cube2);
        OperationResult previewResult = operation.Preview(cube1);

        Assert.True(previewResult.Changed);
        Assert.Equal(beforeTriCount, cube1.TriangleCount);
    }

    [Fact]
    public void BooleanIntersectionOperation_Preview_DoesNotMutate()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(0.5, 0.0, 0.0);
        int beforeTriCount = cube1.TriangleCount;

        var operation = new BooleanIntersectionOperation(cube2);
        OperationResult previewResult = operation.Preview(cube1);

        Assert.True(previewResult.Changed);
        Assert.Equal(beforeTriCount, cube1.TriangleCount);
    }

    [Fact]
    public void BooleanUnionOperation_Apply_MutatesPrimaryMesh()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(0.5, 0.5, 0.0);
        int beforeTriCount = cube1.TriangleCount;

        var operation = new BooleanUnionOperation(cube2);
        OperationResult result = operation.Apply(cube1);

        Assert.True(result.Changed);
        Assert.NotEqual(beforeTriCount, cube1.TriangleCount);
    }

    [Fact]
    public void BooleanOperation_WithNullSecondaryMesh_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new BooleanUnionOperation(null!));
        Assert.Throws<ArgumentNullException>(() => new BooleanDifferenceOperation(null!));
        Assert.Throws<ArgumentNullException>(() => new BooleanIntersectionOperation(null!));
    }

    [Fact]
    public void BooleanOperation_SummaryContainsReadableMessage()
    {
        var cube1 = CreateUnitCube();
        var cube2 = CreateOffsetCube(0.5, 0.5, 0.0);

        var operation = new BooleanUnionOperation(cube2);
        OperationResult result = operation.Apply(cube1);

        // Summary should include useful information for the user
        Assert.NotEmpty(result.Summary);
        Assert.Contains("primary", result.Summary, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secondary", result.Summary, System.StringComparison.OrdinalIgnoreCase);
    }
}
