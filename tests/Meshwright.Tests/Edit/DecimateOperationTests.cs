using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Edit;

public class DecimateOperationTests
{
    // A watertight, 2-manifold, no-boundary box with gridded (subdivided) faces — a
    // higher-resolution fixture with a well-defined starting triangle count, built via the
    // vendored g3Sharp GridBox3Generator rather than a fixture file, since decimation tests
    // need a mesh with plenty of triangles to collapse rather than a specific defect pattern.
    private static DMesh3 BuildGridBox(int edgeVertices)
    {
        var generator = new GridBox3Generator
        {
            Box = Box3d.UnitZeroCentered,
            EdgeVertices = edgeVertices,
        };
        generator.Generate();
        return generator.MakeDMesh();
    }

    private static void AssertValidManifoldMesh(DMesh3 mesh)
    {
        Assert.True(mesh.CheckValidity(bAllowNonManifoldVertices: false, eFailMode: FailMode.ReturnOnly));
        Assert.True(mesh.IsClosed());
        Assert.Empty(new NonManifoldDetector().Detect(mesh));
        Assert.Empty(new DegenerateTriangleDetector().Detect(mesh));
    }

    [Fact]
    public void ToTriangleCount_ReducesToAtOrNearTarget_AndStaysValidManifold()
    {
        DMesh3 mesh = BuildGridBox(edgeVertices: 17); // 2*16*16*6 = 3072 triangles
        int before = mesh.TriangleCount;
        const int target = 200;

        var operation = DecimateOperation.ToTriangleCount(target);
        OperationResult result = operation.Apply(mesh);

        Assert.True(result.Changed);
        Assert.True(mesh.TriangleCount < before);
        // Reducer stops as soon as TriangleCount <= TargetCount, and each collapse removes at
        // most two triangles, so it should land within a couple of triangles of the target
        // rather than drastically overshooting or falling far short of it.
        Assert.InRange(mesh.TriangleCount, target - 2, target);
        Assert.Contains("Reduced from", result.Summary);
        Assert.Contains(before.ToString(), result.Summary);

        AssertValidManifoldMesh(mesh);
    }

    /// <summary>
    /// Collapses that would create non-manifold geometry are refused, so on an already-broken mesh
    /// the reducer stalls well above the target. Reporting only "reduced from N to M" presents
    /// that as an unqualified success: on a real 139,989-triangle scan, a request for 100
    /// triangles stopped at 55,817 and said nothing about it.
    /// </summary>
    [Fact]
    public void WhenItCannotReachTheTarget_TheSummarySaysSoAndSuggestsRepair()
    {
        // A closed surface bottoms out at a tetrahedron, so a single triangle is unreachable and
        // the reducer is guaranteed to stop short of it.
        DMesh3 mesh = BuildGridBox(edgeVertices: 2);
        const int target = 1;

        OperationResult result = DecimateOperation.ToTriangleCount(target).Apply(mesh);

        Assert.True(mesh.TriangleCount > target, "fixture should be unable to reach the target");
        Assert.Contains("Short of the", result.Summary);
        Assert.Contains("1-triangle target", result.Summary);
        Assert.Contains("Repairing the mesh first", result.Summary);
    }

    [Fact]
    public void WhenItReachesTheTarget_TheSummaryDoesNotClaimItFellShort()
    {
        DMesh3 mesh = BuildGridBox(edgeVertices: 17);

        OperationResult result = DecimateOperation.ToTriangleCount(200).Apply(mesh);

        Assert.DoesNotContain("Short of the", result.Summary);
    }

    [Fact]
    public void ToPercentage_ComputesCorrectAbsoluteTargetCount()
    {
        DMesh3 mesh = BuildGridBox(edgeVertices: 9); // 2*8*8*6 = 768 triangles
        int before = mesh.TriangleCount;

        var operation = DecimateOperation.ToPercentage(0.25);

        int expectedTarget = (int)Math.Round(before * 0.25, MidpointRounding.AwayFromZero);
        Assert.Equal(expectedTarget, operation.TargetTriangleCount(before));

        OperationResult result = operation.Apply(mesh);

        Assert.True(result.Changed);
        Assert.InRange(mesh.TriangleCount, expectedTarget - 2, expectedTarget);
        AssertValidManifoldMesh(mesh);
    }

    [Theory]
    [InlineData(1000, 0.25, 250)]
    [InlineData(1000, 1.0, 1000)]
    [InlineData(10, 0.01, 1)] // rounds down to 0, clamped up to the minimum of 1
    [InlineData(0, 0.5, 0)]
    public void TargetTriangleCount_IsPureArithmetic_NoMeshRequired(int currentCount, double fraction, int expected)
    {
        var operation = DecimateOperation.ToPercentage(fraction);
        Assert.Equal(expected, operation.TargetTriangleCount(currentCount));
    }

    [Fact]
    public void TargetTriangleCount_TriangleCountMode_ClampsToCurrentCount()
    {
        var operation = DecimateOperation.ToTriangleCount(500);

        // Requesting more triangles than currently exist clamps down to "no reduction possible".
        Assert.Equal(100, operation.TargetTriangleCount(100));
        // Below current count, the configured absolute target is used as-is.
        Assert.Equal(500, operation.TargetTriangleCount(10_000));
    }

    [Fact]
    public void Apply_TargetAtOrAboveCurrentCount_IsNoOp()
    {
        DMesh3 mesh = BuildGridBox(edgeVertices: 5); // 2*4*4*6 = 192 triangles
        int before = mesh.TriangleCount;

        var operation = DecimateOperation.ToTriangleCount(before + 100);
        OperationResult result = operation.Apply(mesh);

        Assert.False(result.Changed);
        Assert.Equal(before, mesh.TriangleCount);
    }

    [Fact]
    public void Preview_DoesNotMutateCallersMesh()
    {
        DMesh3 mesh = BuildGridBox(edgeVertices: 9);
        int before = mesh.TriangleCount;

        var operation = DecimateOperation.ToTriangleCount(100);
        OperationResult previewResult = operation.Preview(mesh);

        Assert.True(previewResult.Changed);
        Assert.Equal(before, mesh.TriangleCount);
    }

    [Fact]
    public void ToTriangleCount_RejectsNonPositiveTarget()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DecimateOperation.ToTriangleCount(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DecimateOperation.ToTriangleCount(-5));
    }

    [Fact]
    public void ToPercentage_RejectsNonPositiveFraction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DecimateOperation.ToPercentage(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DecimateOperation.ToPercentage(-0.1));
    }
}
