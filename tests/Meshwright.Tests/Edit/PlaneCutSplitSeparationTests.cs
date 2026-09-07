using System;
using System.Collections.Generic;
using System.Linq;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Backlog item 26: a split used to leave both halves in one mesh with their caps in exactly the
/// same plane, over exactly the same area. Every cap triangle overlapped its opposite number, so
/// the clean Menger sponge came out of a centre split reporting <b>1,640</b> issues — 1,304
/// self-intersections, 208 non-manifold edges and 128 duplicate vertex locations — on a model whose
/// two halves, measured on their own, were each closed, single-shell and issue-free.
///
/// <para>
/// The assertions here are deliberately about <em>each half</em> rather than about the issue count
/// going down. A test that checked "fewer issues than before", or "TriangleCount > 0", would pass
/// just as happily for a fix that quietly dropped one half — which is the failure mode §11 records
/// for the plane cut twice already.
/// </para>
/// </summary>
public class PlaneCutSplitSeparationTests
{
    private static readonly Vector3d Normal = Vector3d.AxisZ;

    /// <summary>Defect categories a split invents when the halves are left coincident.</summary>
    private static readonly string[] CoincidenceDefects =
    {
        "SelfIntersection", "NonManifoldEdge", "DuplicateVertex",
    };

    [Fact]
    public void Split_LeavesTwoHalvesThatAreEachClosedSingleShellAndIssueFree()
    {
        var document = new MeshDocument();
        document.Load(MengerSponge.BuildLevel2());

        double volumeBefore = document.Report!.Statistics.Volume;
        Assert.Equal(0, document.Report.DefectCount);

        Assert.True(document.Apply(new PlaneCutSplitOperation(Vector3d.Zero, Normal)).Changed);

        // The merged mesh itself: two shells and not one defect between them.
        Assert.Equal(2, document.Report!.Statistics.ShellCount);
        Assert.Equal(0, document.Report.DefectCount);
        foreach (string category in CoincidenceDefects)
        {
            Assert.DoesNotContain(document.Report.Issues, issue => issue.Category == category);
        }

        IReadOnlyList<DMesh3> halves = MeshShells.Separate(document.Mesh!);
        Assert.Equal(2, halves.Count);

        double volumeSum = 0.0;
        foreach (DMesh3 half in halves)
        {
            var halfDocument = new MeshDocument();
            halfDocument.Load(half);
            MeshDiagnosticsReport report = halfDocument.Report!;

            Assert.Empty(new MeshBoundaryLoops(half).Loops);   // closed
            Assert.Equal(1, report.Statistics.ShellCount);     // one shell
            Assert.Equal(0, report.DefectCount);               // issue-free
            Assert.True(report.Statistics.TriangleCount > 0);

            volumeSum += report.Statistics.Volume;
        }

        // Neither half was dropped, and neither gained volume from the other.
        Assert.Equal(volumeBefore, volumeSum, 6);
        foreach (DMesh3 half in halves)
        {
            Assert.InRange(MeshStatistics.Compute(half).Volume, volumeBefore * 0.4, volumeBefore * 0.6);
        }
    }

    [Fact]
    public void Split_MovesTheHalvesFarEnoughApartForASlicerToSeeTwoObjects()
    {
        var document = new MeshDocument();
        document.Load(MengerSponge.BuildLevel2());     // 2 mm across, so the 1 mm floor is what bites
        document.Apply(new PlaneCutSplitOperation(Vector3d.Zero, Normal));

        (Interval1d positive, Interval1d negative) = HalfExtentsAlongNormal(document.Mesh!);

        double gap = positive.a - negative.b;
        Assert.True(gap >= 1.0, $"halves are only {gap:0.###} mm apart; a nozzle cannot pass between them");
        Assert.Equal(1.0, gap, 6);                     // max(1 mm, 5% of 2 mm) == 1 mm
    }

    /// <summary>
    /// The gap scales with the model, so it is neither invisible on a big part nor most of a small
    /// one: 5% of the extent along the cut normal once that beats the millimetre floor.
    /// </summary>
    [Fact]
    public void Split_ScalesTheGapWithTheModel()
    {
        var document = new MeshDocument();
        document.Load(MengerSponge.Build(2, 100.0));   // 200 mm across
        document.Apply(new PlaneCutSplitOperation(Vector3d.Zero, Normal));

        (Interval1d positive, Interval1d negative) = HalfExtentsAlongNormal(document.Mesh!);

        Assert.Equal(10.0, positive.a - negative.b, 6); // 5% of 200 mm
    }

    /// <summary>
    /// The separation has to be a <em>pure translation along the cut normal</em>, or the halves no
    /// longer line up and a registration pin is pointless. Undoing it by hand must put the model
    /// back exactly: same bounding box, same volume, and the two mating faces coplanar again.
    /// </summary>
    [Fact]
    public void Split_SeparatesByTranslationAlone_SoTheHalvesStillMate()
    {
        DMesh3 original = MengerSponge.BuildLevel2();
        AxisAlignedBox3d boundsBefore = original.GetBounds();

        var document = new MeshDocument();
        document.Load(original);
        document.Apply(new PlaneCutSplitOperation(Vector3d.Zero, Normal));

        IReadOnlyList<DMesh3> halves = MeshShells.Separate(document.Mesh!);
        (DMesh3 positive, DMesh3 negative) = OrderAlongNormal(halves);

        double gap = ExtentAlongNormal(positive).a - ExtentAlongNormal(negative).b;

        var remated = new DMesh3(positive);
        var map = new Dictionary<int, int>();
        foreach (int vid in negative.VertexIndices())
        {
            map[vid] = remated.AppendVertex(negative.GetVertex(vid) + Normal * gap);
        }

        foreach (int tid in negative.TriangleIndices())
        {
            Index3i tri = negative.GetTriangle(tid);
            remated.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]);
        }

        AxisAlignedBox3d boundsAfter = remated.GetBounds();
        Assert.Equal(boundsBefore.Min.x, boundsAfter.Min.x, 9);
        Assert.Equal(boundsBefore.Min.y, boundsAfter.Min.y, 9);
        Assert.Equal(boundsBefore.Min.z, boundsAfter.Min.z, 9);
        Assert.Equal(boundsBefore.Max.x, boundsAfter.Max.x, 9);
        Assert.Equal(boundsBefore.Max.y, boundsAfter.Max.y, 9);
        Assert.Equal(boundsBefore.Max.z, boundsAfter.Max.z, 9);

        // The mating faces are back in one plane — the cut plane the user aimed at.
        Assert.Equal(0.0, ExtentAlongNormal(positive).a, 9);
        Assert.Equal(0.0, ExtentAlongNormal(negative).b + gap, 9);
    }

    /// <summary>
    /// A registration pin stands proud of its mating face, so a gap chosen without it would leave
    /// the peg still buried in the socket and the two halves interpenetrating — the very defect
    /// this separation exists to remove, and one that only shows up when a pin is asked for.
    /// </summary>
    [Fact]
    public void Split_WithARegistrationPin_ClearsThePeg()
    {
        var pin = new RegistrationPinOptions(Diameter: 0.1, Clearance: 0.005);   // the 2 mm sponge, per RegistrationPinTests

        PlaneCutResult probe = new PlaneCut().Cut(
            MengerSponge.BuildLevel2(), Vector3d.Zero, Normal, CutMode.Split, HoleFillMode.Planar, addCap: true, pin: pin);
        Assert.True(probe.Pin!.PinPlaced);
        double pegProtrusion = probe.Pin.DepthAchieved;
        Assert.True(pegProtrusion > 0.0);

        var document = new MeshDocument();
        document.Load(MengerSponge.BuildLevel2());
        Assert.True(document.Apply(new PlaneCutSplitOperation(Vector3d.Zero, Normal, pin: pin)).Changed);

        Assert.Equal(0, document.Report!.DefectCount);
        Assert.Equal(2, document.Report.Statistics.ShellCount);

        (Interval1d positive, Interval1d negative) = HalfExtentsAlongNormal(document.Mesh!);
        double gap = positive.a - negative.b;
        Assert.True(
            gap > pegProtrusion,
            $"the peg protrudes {pegProtrusion:0.###} mm but the halves are only {gap:0.###} mm apart, so it is still inside the socket");

        foreach (DMesh3 half in MeshShells.Separate(document.Mesh!))
        {
            var halfDocument = new MeshDocument();
            halfDocument.Load(half);
            Assert.Empty(new MeshBoundaryLoops(half).Loops);
            Assert.Equal(1, halfDocument.Report!.Statistics.ShellCount);
            Assert.Equal(0, halfDocument.Report.DefectCount);
        }
    }

    /// <summary>
    /// Half a model is not debris. Inspect used to call the second half a "stray disconnected
    /// shell" at Warning severity — 50% of the model's volume described as something to clean up.
    /// </summary>
    [Fact]
    public void Split_ReportsTheSecondHalfAsAPartRatherThanAsStrayDebris()
    {
        var document = new MeshDocument();
        document.Load(MengerSponge.BuildLevel2());
        document.Apply(new PlaneCutSplitOperation(Vector3d.Zero, Normal));

        MeshDiagnosticsReport report = document.Report!;
        Assert.DoesNotContain(report.Issues, issue => issue.Category == "DisconnectedShell");

        MeshIssue part = Assert.Single(report.Issues, issue => issue.Category == "SeparatePart");
        Assert.Equal(MeshIssueSeverity.Info, part.Severity);
        Assert.Contains("Separate part", part.Message);

        Assert.Equal("No issues found. 1 separate part.", report.Summary);
    }

    /// <summary>Real debris is still debris — the threshold must not have made the detector blind.</summary>
    [Fact]
    public void ADisconnectedSpeckIsStillReportedAsStrayDebris()
    {
        DMesh3 mesh = MengerSponge.BuildLevel2();
        DMesh3 speck = MengerSponge.Build(0, 0.02);
        var map = new Dictionary<int, int>();
        foreach (int vid in speck.VertexIndices())
        {
            map[vid] = mesh.AppendVertex(speck.GetVertex(vid) + new Vector3d(100, 100, 100));
        }

        foreach (int tid in speck.TriangleIndices())
        {
            Index3i tri = speck.GetTriangle(tid);
            mesh.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]);
        }

        var document = new MeshDocument();
        document.Load(mesh);

        MeshIssue stray = Assert.Single(document.Report!.Issues, issue => issue.Category == "DisconnectedShell");
        Assert.Equal(MeshIssueSeverity.Warning, stray.Severity);
        Assert.Equal(1, document.Report.DefectCount);
    }

    [Fact]
    public void MeshShells_Separate_ReturnsOneMeshPerShell_LargestFirst()
    {
        DMesh3 mesh = MengerSponge.Build(0, 10.0);
        DMesh3 small = MengerSponge.Build(0, 1.0);
        var map = new Dictionary<int, int>();
        foreach (int vid in small.VertexIndices())
        {
            map[vid] = mesh.AppendVertex(small.GetVertex(vid) + new Vector3d(100, 0, 0));
        }

        foreach (int tid in small.TriangleIndices())
        {
            Index3i tri = small.GetTriangle(tid);
            mesh.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]);
        }

        IReadOnlyList<DMesh3> shells = MeshShells.Separate(mesh);

        Assert.Equal(2, shells.Count);
        Assert.True(MeshStatistics.Compute(shells[0]).Volume > MeshStatistics.Compute(shells[1]).Volume);
        Assert.Equal(mesh.TriangleCount, shells.Sum(shell => shell.TriangleCount));
    }

    [Fact]
    public void MeshShells_Separate_OnASingleShell_ReturnsACopyRatherThanTheInput()
    {
        DMesh3 mesh = MengerSponge.Build(0, 10.0);

        DMesh3 only = Assert.Single(MeshShells.Separate(mesh));

        Assert.NotSame(mesh, only);
        Assert.Equal(mesh.TriangleCount, only.TriangleCount);
    }

    private static (Interval1d Positive, Interval1d Negative) HalfExtentsAlongNormal(DMesh3 mesh)
    {
        (DMesh3 positive, DMesh3 negative) = OrderAlongNormal(MeshShells.Separate(mesh));
        return (ExtentAlongNormal(positive), ExtentAlongNormal(negative));
    }

    private static (DMesh3 Positive, DMesh3 Negative) OrderAlongNormal(IReadOnlyList<DMesh3> halves)
    {
        Assert.Equal(2, halves.Count);
        return ExtentAlongNormal(halves[0]).a > ExtentAlongNormal(halves[1]).a
            ? (halves[0], halves[1])
            : (halves[1], halves[0]);
    }

    private static Interval1d ExtentAlongNormal(DMesh3 mesh)
    {
        var extent = Interval1d.Empty;
        foreach (int vid in mesh.VertexIndices())
        {
            extent.Contain(mesh.GetVertex(vid).Dot(Normal));
        }

        return extent;
    }
}
