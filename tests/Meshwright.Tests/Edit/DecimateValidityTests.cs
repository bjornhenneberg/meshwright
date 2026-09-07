using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;
using Xunit;
using Xunit.Abstractions;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Item 22: decimation introduced the invalid geometry it said it had declined to create. Reducing
/// the clean Menger sponge sample stopped at 736 triangles and left 32 self-intersections in a mesh
/// that had none, while the panel reported that further collapses "would have created invalid
/// geometry" and the status bar counted the 32 in the same frame. Intermediate targets on the same
/// model also produced degenerate triangles, duplicate vertex locations and non-manifold edges.
///
/// <para>
/// The reducer's per-collapse validity test is local to the edge's one-ring, and none of those
/// defects are one-ring properties. <see cref="ValidatingReducer"/> adds the whole-mesh half.
/// </para>
///
/// <para>
/// These tests pin <em>both</em> halves deliberately. Refusing more collapses makes every
/// "no more defects than before" assertion pass trivially — a decimator that decimates nothing is
/// perfectly valid and perfectly useless — so the reach assertions below are as load-bearing as the
/// validity ones.
/// </para>
/// </summary>
public class DecimateValidityTests
{
    private readonly ITestOutputHelper _output;

    public DecimateValidityTests(ITestOutputHelper output) => _output = output;

    internal static IMeshDetector[] Detectors() =>
    [
        new NonManifoldDetector(),
        new BoundaryHoleDetector(),
        new SelfIntersectionDetector(),
        new InvertedNormalDetector(),
        new DegenerateTriangleDetector(),
        new DuplicateVertexDetector(),
        new DisconnectedShellDetector(),
    ];

    /// <summary>
    /// The Menger sponge's defining property, in a fixture that needs no downloaded file: two sheets
    /// of surface running parallel a short distance apart, meshed finely enough that a collapse can
    /// walk one through the other. A quadric-optimal collapse point on a nearly-flat neighbourhood is
    /// poorly conditioned and lands off the surface, which is how a wall ends up on the far side of
    /// its neighbour.
    /// </summary>
    private static DMesh3 ThinWalledSlab(int edgeVertices = 25, double thickness = 0.04)
    {
        var generator = new GridBox3Generator
        {
            Box = new Box3d(Vector3d.Zero, new Vector3d(1.0, 1.0, thickness)),
            EdgeVertices = edgeVertices,
        };
        generator.Generate();
        return generator.MakeDMesh();
    }

    private static DMesh3 GridBox(int edgeVertices)
    {
        var generator = new GridBox3Generator
        {
            Box = Box3d.UnitZeroCentered,
            EdgeVertices = edgeVertices,
        };
        generator.Generate();
        return generator.MakeDMesh();
    }

    private static MeshDiagnosticsReport Diagnose(DMesh3 mesh) =>
        MeshDiagnosticsRunner.Run(mesh, Detectors());

    // ------------------------------------------------------- the fixture still carries the defect

    /// <summary>
    /// Guards every assertion below from going vacuous. If the bare vendored reducer ever stops
    /// producing defects on this fixture — a different g3 revision, a different mesh generator — then
    /// "decimation adds no defects" would pass without <see cref="ValidatingReducer"/> doing anything
    /// at all, and the regression this slice fixed would be free to come back unnoticed.
    /// </summary>
    [Fact]
    public void TheFixtureStillReproducesTheBug_WithoutTheGuard()
    {
        DMesh3 mesh = ThinWalledSlab();
        Assert.Equal(0, Diagnose(mesh).DefectCount);

        var reducer = new Reducer(mesh);
        reducer.ReduceToTriangleCount(mesh.TriangleCount / 4);

        MeshDiagnosticsReport after = Diagnose(mesh);
        _output.WriteLine($"unguarded: {mesh.TriangleCount} triangles, {after.Summary}");
        Assert.True(after.DefectCount > 0,
            "the unguarded reducer no longer breaks this fixture, so the guarded tests below prove nothing");
    }

    // ------------------------------------------------------------------------- a valid mesh stays valid

    /// <summary>
    /// The invariant the bug broke, measured the way the app measures it: by the whole-mesh
    /// detectors, over a mesh that was clean before.
    /// </summary>
    [Theory]
    [InlineData(0.75)]
    [InlineData(0.5)]
    [InlineData(0.25)]
    [InlineData(0.05)]
    public void AMeshThatWasValid_IsStillValidAfterDecimating(double fraction)
    {
        DMesh3 mesh = ThinWalledSlab();
        Assert.Equal(0, Diagnose(mesh).DefectCount);

        OperationResult result = DecimateOperation.ToPercentage(fraction).Apply(mesh);

        MeshDiagnosticsReport after = Diagnose(mesh);
        _output.WriteLine($"{fraction:P0}: {mesh.TriangleCount} triangles, {after.Summary} — {result.Summary}");
        Assert.True(after.DefectCount == 0, $"decimating to {fraction:P0} introduced {after.Summary}");
        Assert.True(mesh.IsClosed(), "a closed mesh must stay closed");
    }

    // ---------------------------------------------------------------------------- and still reduces

    /// <summary>
    /// The other half. Refusing collapses is the cheap way to make the assertion above pass, so a
    /// target the mesh can honestly reach has to still be reached exactly — not approached, not
    /// "at least something was removed".
    /// </summary>
    [Theory]
    [InlineData(17, 200)] // 3072 triangles down to 200
    [InlineData(17, 1000)]
    [InlineData(9, 100)] // 768 triangles down to 100
    public void ATargetTheMeshCanReach_IsStillReachedExactly(int edgeVertices, int target)
    {
        DMesh3 mesh = GridBox(edgeVertices);

        OperationResult result = DecimateOperation.ToTriangleCount(target).Apply(mesh);

        Assert.InRange(mesh.TriangleCount, target - 2, target);
        Assert.DoesNotContain("Short of the", result.Summary);
        Assert.Equal(0, Diagnose(mesh).DefectCount);
    }

    /// <summary>
    /// A percentage target on a mesh with room to lose must still be met. This is the assertion that
    /// fails first if the guard is ever made stricter than "do not add a defect".
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(0.25)]
    [InlineData(0.1)]
    public void APercentageTargetOnAMeshWithRoomToLose_IsStillMet(double fraction)
    {
        DMesh3 mesh = GridBox(edgeVertices: 17);
        var operation = DecimateOperation.ToPercentage(fraction);
        int target = operation.TargetTriangleCount(mesh.TriangleCount);

        operation.Apply(mesh);

        Assert.InRange(mesh.TriangleCount, target - 2, target);
    }

    /// <summary>
    /// The guard must be doing its job through refusals rather than by accident: on the fixture that
    /// reproduces the bug it has to actually veto collapses the local tests approved.
    /// </summary>
    [Fact]
    public void TheGuard_RefusesCollapsesTheLocalTestsAccepted()
    {
        DMesh3 mesh = ThinWalledSlab();
        var reducer = new ValidatingReducer(mesh);

        reducer.ReduceToTriangleCount(mesh.TriangleCount / 4);

        _output.WriteLine($"refused {reducer.RefusedCollapses} collapses, {mesh.TriangleCount} triangles left");
        Assert.True(reducer.RefusedCollapses > 0);
    }

    /// <summary>
    /// A mesh that is already broken must not be held to a standard it never met: the guard refuses
    /// collapses that <em>add</em> defects, not collapses in regions that were already defective.
    /// Requiring validity here would leave a broken scan barely decimated at all.
    /// </summary>
    [Fact]
    public void AnAlreadyBrokenMesh_StillDecimates()
    {
        DMesh3 mesh = ThinWalledSlab();

        // Break it first, in the way the bug itself broke it, by reducing without the guard.
        new Reducer(mesh).ReduceToTriangleCount(mesh.TriangleCount / 2);
        MeshDiagnosticsReport before = Diagnose(mesh);
        Assert.True(before.DefectCount > 0, "fixture should be broken by here");

        int startedAt = mesh.TriangleCount;
        OperationResult result = DecimateOperation.ToTriangleCount(startedAt / 2).Apply(mesh);

        _output.WriteLine($"{startedAt} -> {mesh.TriangleCount}: {result.Summary}");
        Assert.True(result.Changed);
        Assert.True(mesh.TriangleCount <= startedAt * 0.6,
            $"a broken mesh still has to reduce; got {mesh.TriangleCount} from {startedAt}");
    }
}
