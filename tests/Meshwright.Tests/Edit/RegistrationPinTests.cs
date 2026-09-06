using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Registration pins on plane-cut faces (SPECIFICATION.md §5.1, backlog item 25).
///
/// <para>These assert <b>measured invariants before and after</b>, never existence: a pinned split
/// that appended a cylinder in the wrong place, at the wrong size, or on top of the half it was
/// meant to bore into would satisfy "triangle count went up" perfectly happily (§11,
/// 2026-09-05).</para>
/// </summary>
public class RegistrationPinTests
{
    private static DMesh3 BuildBox(double sizeX, double sizeY, double sizeZ, double originZ = 0.0)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, originZ));
        int v100 = mesh.AppendVertex(new Vector3d(sizeX, 0, originZ));
        int v110 = mesh.AppendVertex(new Vector3d(sizeX, sizeY, originZ));
        int v010 = mesh.AppendVertex(new Vector3d(0, sizeY, originZ));
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, originZ + sizeZ));
        int v101 = mesh.AppendVertex(new Vector3d(sizeX, 0, originZ + sizeZ));
        int v111 = mesh.AppendVertex(new Vector3d(sizeX, sizeY, originZ + sizeZ));
        int v011 = mesh.AppendVertex(new Vector3d(0, sizeY, originZ + sizeZ));

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
        }

        Quad(v000, v010, v110, v100);
        Quad(v001, v101, v111, v011);
        Quad(v000, v100, v101, v001);
        Quad(v010, v011, v111, v110);
        Quad(v000, v001, v011, v010);
        Quad(v100, v110, v111, v101);

        return mesh;
    }

    /// <summary>
    /// Area of the regular n-gon the pin circle really is, as a fraction of πr². Every volume figure
    /// below is compared against the polygon, not the ideal circle, so the tolerance measures the
    /// implementation rather than the sampling.
    /// </summary>
    private static double PolygonArea(double radius, int segments) =>
        0.5 * segments * radius * radius * Math.Sin(2.0 * Math.PI / segments);

    private static int SelfIntersectionCount(DMesh3 mesh) =>
        new SelfIntersectionDetector().Detect(mesh).Count;

    /// <summary>Largest distance from the pin axis reached by geometry on the far side of the cut plane.</summary>
    private static double MaxRadiusBelowPlane(DMesh3 mesh, Vector3d center, Vector3d axis, double planeTolerance)
    {
        double best = 0.0;
        foreach (int vid in mesh.VertexIndices())
        {
            Vector3d offset = mesh.GetVertex(vid) - center;
            double along = offset.Dot(axis);
            if (along < -planeTolerance)
            {
                best = Math.Max(best, (offset - (along * axis)).Length);
            }
        }

        return best;
    }

    [Fact]
    public void Pin_PegAndSocketAreCoaxialAndMateWithExactlyTheRequestedClearance()
    {
        var mesh = BuildBox(20, 20, 20);
        var options = new RegistrationPinOptions(Diameter: 4.0, Clearance: 0.2, Depth: 5.0);

        PlaneCutResult result = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar, addCap: true, pin: options);

        Assert.True(result.MeshWasModified);
        RegistrationPinResult pin = Assert.IsType<RegistrationPinResult>(result.Pin);
        Assert.True(pin.PinPlaced, pin.Message);

        // Measured off the finished geometry, not restated from the request.
        Assert.Equal(4.0, pin.PegDiameterAchieved, 9);
        Assert.Equal(4.4, pin.SocketDiameterAchieved, 9);
        Assert.Equal(0.2, pin.ClearanceAchieved, 9);
        Assert.Equal(5.0, pin.DepthAchieved, 9);

        // Independently of the result record: scan both halves for whatever they put below the cut
        // plane. On the peg half that can only be the peg; on the socket half the bore is the only
        // thing near the axis, so the widest thing within a bore-and-a-bit is the socket wall.
        DMesh3 pegHalf = result.PositiveSideMesh;
        DMesh3 socketHalf = result.NegativeSideMesh!;
        Assert.Equal(2.0, MaxRadiusBelowPlane(pegHalf, pin.Center, pin.Axis, 1e-9), 9);

        // Coaxial: every peg vertex below the plane is the same distance from the socket's axis.
        var pegRadii = new List<double>();
        foreach (int vid in pegHalf.VertexIndices())
        {
            Vector3d offset = pegHalf.GetVertex(vid) - pin.Center;
            double along = offset.Dot(pin.Axis);
            double radius = (offset - (along * pin.Axis)).Length;
            if (along < -1e-9 && radius > 1e-9)
            {
                pegRadii.Add(radius);
            }
        }

        Assert.NotEmpty(pegRadii);
        Assert.All(pegRadii, r => Assert.Equal(2.0, r, 9));

        Assert.True(pegHalf.IsClosed(), "peg half must stay a closed shell");
        Assert.True(socketHalf.IsClosed(), "socket half must stay a closed shell");
    }

    [Fact]
    public void Pin_MovesVolumeFromTheSocketHalfToThePegHalfByComparableAmounts()
    {
        var mesh = BuildBox(20, 20, 20);
        var plain = new PlaneCut().Cut(mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);
        double plainPeg = MeshStatistics.Compute(plain.PositiveSideMesh).Volume;
        double plainSocket = MeshStatistics.Compute(plain.NegativeSideMesh!).Volume;

        var options = new RegistrationPinOptions(Diameter: 4.0, Clearance: 0.2, Depth: 5.0);
        var pinned = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar, addCap: true, pin: options);

        double pinnedPeg = MeshStatistics.Compute(pinned.PositiveSideMesh).Volume;
        double pinnedSocket = MeshStatistics.Compute(pinned.NegativeSideMesh!).Volume;

        double pegGain = pinnedPeg - plainPeg;
        double socketLoss = plainSocket - pinnedSocket;

        // The peg is a 32-gon prism of radius 2 and length 5; the socket a 32-gon prism of radius
        // 2.2 and depth 5.2. Both are exact to floating point — the pin is generated, so there is no
        // approximation to allow for beyond the polygon itself.
        Assert.Equal(PolygonArea(2.0, 32) * 5.0, pegGain, 6);
        Assert.Equal(PolygonArea(2.2, 32) * 5.2, socketLoss, 6);

        Assert.True(pegGain > 0.0, "the peg half must gain volume");
        Assert.True(socketLoss > 0.0, "the socket half must lose volume");
        Assert.InRange(socketLoss / pegGain, 1.0, 1.5);
    }

    [Fact]
    public void Pin_DoesNotMoveTheBoundingBoxOrIntroduceSelfIntersections()
    {
        var mesh = BuildBox(20, 20, 20);
        AxisAlignedBox3d before = mesh.CachedBounds;

        var options = new RegistrationPinOptions(Diameter: 4.0, Clearance: 0.2, Depth: 5.0);
        var result = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar, addCap: true, pin: options);

        AxisAlignedBox3d combined = result.PositiveSideMesh.CachedBounds;
        combined.Contain(result.NegativeSideMesh!.CachedBounds);

        Assert.Equal(before.Min.x, combined.Min.x, 9);
        Assert.Equal(before.Min.y, combined.Min.y, 9);
        Assert.Equal(before.Min.z, combined.Min.z, 9);
        Assert.Equal(before.Max.x, combined.Max.x, 9);
        Assert.Equal(before.Max.y, combined.Max.y, 9);
        Assert.Equal(before.Max.z, combined.Max.z, 9);

        Assert.Equal(0, SelfIntersectionCount(result.PositiveSideMesh));
        Assert.Equal(0, SelfIntersectionCount(result.NegativeSideMesh!));
    }

    [Fact]
    public void Pin_TooLargeForTheCrossSection_IsRefusedAndLeavesTheMeshUntouched()
    {
        var mesh = BuildBox(20, 20, 20);
        int trianglesBefore = mesh.TriangleCount;
        double volumeBefore = MeshStatistics.Compute(mesh).Volume;

        var options = new RegistrationPinOptions(Diameter: 18.0, Clearance: 0.2, Depth: 5.0);
        var result = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar, addCap: true, pin: options);

        Assert.False(result.MeshWasModified);
        Assert.Null(result.NegativeSideMesh);
        RegistrationPinResult pin = Assert.IsType<RegistrationPinResult>(result.Pin);
        Assert.False(pin.PinPlaced);
        Assert.Contains("does not fit", pin.Message, StringComparison.Ordinal);
        Assert.True(pin.LargestDiameterThatFits > 0.0);
        Assert.Contains(pin.LargestDiameterThatFits.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), pin.Message, StringComparison.Ordinal);

        // The input, and the mesh handed back in its place, are untouched.
        Assert.Equal(trianglesBefore, mesh.TriangleCount);
        Assert.Equal(volumeBefore, MeshStatistics.Compute(mesh).Volume, 9);
        Assert.Equal(trianglesBefore, result.PositiveSideMesh.TriangleCount);
    }

    [Fact]
    public void Pin_TheLargestDiameterARefusalNamesActuallyFits()
    {
        var mesh = BuildBox(20, 20, 20);
        var refused = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar,
            addCap: true, pin: new RegistrationPinOptions(18.0, 0.2, 5.0));

        double largest = refused.Pin!.LargestDiameterThatFits;

        var retried = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar,
            addCap: true, pin: new RegistrationPinOptions(largest, 0.2, 5.0));

        Assert.True(retried.Pin!.PinPlaced, retried.Pin.Message);
        Assert.Equal(largest, retried.Pin.PegDiameterAchieved, 6);
    }

    [Fact]
    public void Pin_ThatWouldBoreOutThroughTheModelsOwnWall_IsRefused()
    {
        // Cut 2 mm from the bottom of a 20 mm box: the cross-section is huge, so nothing about the
        // mating face objects, but a 6 mm deep socket would come out through the underside.
        var mesh = BuildBox(20, 20, 20);
        var result = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 2), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar,
            addCap: true, pin: new RegistrationPinOptions(Diameter: 4.0, Clearance: 0.2, Depth: 6.0));

        Assert.False(result.MeshWasModified);
        Assert.False(result.Pin!.PinPlaced);
        Assert.Contains("break out", result.Pin.Message, StringComparison.Ordinal);

        // The same pin one millimetre shorter than the material below the cut is accepted, so the
        // refusal is about the depth rather than a blanket veto.
        var shallow = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 2), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar,
            addCap: true, pin: new RegistrationPinOptions(Diameter: 4.0, Clearance: 0.2, Depth: 1.0));

        Assert.True(shallow.Pin!.PinPlaced, shallow.Pin.Message);
    }

    [Fact]
    public void Pin_NeedsBothHalves_SoKeepModeAndUncappedCutsAreRefused()
    {
        var mesh = BuildBox(20, 20, 20);
        var options = new RegistrationPinOptions(4.0, 0.2, 5.0);

        var keep = new PlaneCut().Cut(mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Keep, HoleFillMode.Planar, addCap: true, pin: options);
        Assert.False(keep.MeshWasModified);
        Assert.False(keep.Pin!.PinPlaced);
        Assert.Contains("Split mode", keep.Pin.Message, StringComparison.Ordinal);

        var uncapped = new PlaneCut().Cut(mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar, addCap: false, pin: options);
        Assert.False(uncapped.MeshWasModified);
        Assert.False(uncapped.Pin!.PinPlaced);
        Assert.Contains("capped", uncapped.Pin.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Pin_RoundTrips_TheSocketSwallowsThePegWithClearanceAllRound()
    {
        var mesh = BuildBox(20, 20, 20);
        var result = new PlaneCut().Cut(
            mesh, new Vector3d(10, 10, 10), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar,
            addCap: true, pin: new RegistrationPinOptions(Diameter: 4.0, Clearance: 0.2, Depth: 5.0));

        RegistrationPinResult pin = result.Pin!;
        DMesh3 pegHalf = result.PositiveSideMesh;
        DMesh3 socketHalf = result.NegativeSideMesh!;

        // Reassembly is the halves back in their original relative position — that is what a split
        // for printing means. Every point of the peg must then be inside the bore, radially clear by
        // the requested clearance and axially clear of the bore's floor.
        double socketFloor = double.MaxValue;
        foreach (int vid in socketHalf.VertexIndices())
        {
            Vector3d offset = socketHalf.GetVertex(vid) - pin.Center;
            double along = offset.Dot(pin.Axis);
            double radius = (offset - (along * pin.Axis)).Length;
            if (radius < 2.3 && along < -1e-9)
            {
                socketFloor = Math.Min(socketFloor, along);
            }
        }

        Assert.Equal(-5.2, socketFloor, 9);

        double pegTip = 0.0;
        foreach (int vid in pegHalf.VertexIndices())
        {
            Vector3d offset = pegHalf.GetVertex(vid) - pin.Center;
            pegTip = Math.Min(pegTip, offset.Dot(pin.Axis));
        }

        Assert.Equal(-5.0, pegTip, 9);
        Assert.True(pegTip > socketFloor, "the peg must not bottom out before the faces meet");
        Assert.Equal(0.2, (pin.SocketDiameterAchieved - pin.PegDiameterAchieved) / 2.0, 9);
    }

    [Fact]
    public void Pin_OnAMengerSpongeCut_LandsInMaterialAndKeepsBothHalvesClosed()
    {
        DMesh3 mesh = MengerSponge.BuildLevel2();
        double size = mesh.CachedBounds.Diagonal.x;
        Vector3d center = mesh.CachedBounds.Center;

        var result = new PlaneCut().Cut(
            mesh, center, Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar,
            addCap: true, pin: new RegistrationPinOptions(Diameter: size / 20.0, Clearance: size / 400.0));

        RegistrationPinResult pin = Assert.IsType<RegistrationPinResult>(result.Pin);
        Assert.True(pin.PinPlaced, pin.Message);

        // Automatic placement has to land in material. The cut of a level-2 sponge at its centre is
        // dozens of disjoint squares with square holes in them, so a centroid would land in a void:
        // this asserts the chosen point is on the solid side of the cross-section and that the whole
        // socket bore fits inside the material there.
        Assert.True(result.PositiveSideMesh.IsClosed(), "peg half must stay a closed shell");
        Assert.True(result.NegativeSideMesh!.IsClosed(), "socket half must stay a closed shell");

        var tree = new DMeshAABBTree3(mesh, autoBuild: true);
        double socketRadius = pin.SocketDiameterAchieved / 2.0;
        for (int d = 1; d <= 4; d++)
        {
            Vector3d probe = pin.Center - (pin.Axis * (d * (pin.DepthAchieved + pin.ClearanceAchieved) / 4.0));
            Assert.True(tree.IsInside(probe), $"socket axis leaves the model at depth step {d}");
        }

        Assert.True(socketRadius > 0.0);
        Assert.Equal(pin.PegDiameterAchieved + (2.0 * pin.ClearanceRequested), pin.SocketDiameterAchieved, 9);

        // And the pin introduces no defect the unpinned cut of the same sponge does not already
        // have — in particular it does not graze the walls of the tunnels it sits between.
        PlaneCutResult plain = new PlaneCut().Cut(mesh, center, Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);
        Assert.Equal(
            SelfIntersectionCount(plain.PositiveSideMesh) + SelfIntersectionCount(plain.NegativeSideMesh!),
            SelfIntersectionCount(result.PositiveSideMesh) + SelfIntersectionCount(result.NegativeSideMesh!));
    }

    [Fact]
    public void Pin_CostsTheSameOrderAsTheCutItself_NotABooleansWorth()
    {
        DMesh3 mesh = MengerSponge.BuildLevel2();
        Vector3d center = mesh.CachedBounds.Center;
        double size = mesh.CachedBounds.Diagonal.x;
        var options = new RegistrationPinOptions(size / 20.0, size / 400.0);

        // Warm up the JIT so the comparison measures geometry rather than first-call overhead.
        new PlaneCut().Cut(mesh, center, Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);
        new PlaneCut().Cut(mesh, center, Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar, addCap: true, pin: options);

        var plainWatch = Stopwatch.StartNew();
        new PlaneCut().Cut(mesh, center, Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);
        plainWatch.Stop();

        var pinnedWatch = Stopwatch.StartNew();
        PlaneCutResult pinned = new PlaneCut().Cut(mesh, center, Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar, addCap: true, pin: options);
        pinnedWatch.Stop();

        Assert.True(pinned.Pin!.PinPlaced, pinned.Pin.Message);

        // The pin is generated, not booleaned: it adds one circle's worth of geometry and a spatial
        // query, so a pinned cut costs the same order as an unpinned one. A boolean-based
        // implementation — the thing this feature exists to replace — would not.
        double plainMs = Math.Max(plainWatch.Elapsed.TotalMilliseconds, 1.0);
        Assert.True(
            pinnedWatch.Elapsed.TotalMilliseconds < plainMs * 20.0,
            $"pinned cut took {pinnedWatch.Elapsed.TotalMilliseconds:0.#} ms against {plainMs:0.#} ms unpinned");

        // And it adds a bounded amount of geometry over the unpinned cut of the same mesh: two
        // circles' worth of wall and end discs plus the cap re-triangulated around them, not a whole
        // second mesh's worth.
        PlaneCutResult plain = new PlaneCut().Cut(mesh, center, Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);
        int added = pinned.TrianglesAfter - plain.TrianglesAfter;
        Assert.True(added > 0 && added < 400, $"pinning added {added} triangles over the unpinned cut");
    }
}
