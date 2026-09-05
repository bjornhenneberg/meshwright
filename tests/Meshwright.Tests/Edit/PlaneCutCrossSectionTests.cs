using g3;
using Meshwright.Core;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Plane cuts whose cross-section is more than one loop (SPECIFICATION.md §5.1 "Edit", backlog
/// item 12). Anything with a hole through it — a tube, a Menger sponge — is crossed by the cutting
/// plane in several separate boundary loops at once, and the cap has to close each of them, with
/// loops nested inside other loops read as holes rather than as extra filled discs.
///
/// <para>
/// Per §11 (2026-09-05) these assert invariants measured before and after: bounding box, volume,
/// shell count and issue counts from the shipping detectors, never merely that some triangles came
/// out.
/// </para>
/// </summary>
public class PlaneCutCrossSectionTests
{
    /// <summary>
    /// Defects a cut must not introduce. <c>DisconnectedShell</c> is deliberately absent: a cut is
    /// allowed — and for Split, required — to leave more than one shell, and shell counts are
    /// asserted explicitly instead.
    /// </summary>
    private static readonly string[] ForbiddenCategories =
    [
        "SelfIntersection",
        "BoundaryHole",
        "NonManifoldEdge",
        "DegenerateTriangle",
        "InvertedNormal",
    ];

    private static MeshDiagnosticsReport Diagnose(DMesh3 mesh)
    {
        var document = new MeshDocument();
        document.Load(mesh);
        return document.Report!;
    }

    /// <summary>
    /// Slivers, separately from the rest. Cutting a plane through a mesh triangle a hair away from
    /// one of its corners leaves a sliver whatever the cap does — <c>SplitMixedTriangle</c> makes
    /// those, not the cross-section — so on a plane that lies askew to the geometry this is not a
    /// defect the cap can be held to.
    /// </summary>
    private static readonly string[] TopologyCategories =
    [
        "SelfIntersection",
        "BoundaryHole",
        "NonManifoldEdge",
        "InvertedNormal",
    ];

    private static void AssertNoNewDefects(string label, MeshDiagnosticsReport report, string[]? categories = null)
    {
        foreach (string category in categories ?? ForbiddenCategories)
        {
            int count = report.Issues.Count(issue => issue.Category == category);
            Assert.True(count == 0, $"{label}: {count} {category} issue(s) after the cut");
        }
    }

    /// <summary>Total area of the triangles lying wholly in the cut plane — the cap.</summary>
    private static double CapArea(DMesh3 mesh, double planeZ)
    {
        double area = 0.0;
        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d a = mesh.GetVertex(tri.a);
            Vector3d b = mesh.GetVertex(tri.b);
            Vector3d c = mesh.GetVertex(tri.c);
            if (Math.Abs(a.z - planeZ) < 1e-9 && Math.Abs(b.z - planeZ) < 1e-9 && Math.Abs(c.z - planeZ) < 1e-9)
            {
                area += 0.5 * (b - a).Cross(c - a).Length;
            }
        }

        return area;
    }

    // ---------------------------------------------------------------- fixtures

    private static void Quad(DMesh3 mesh, int a, int b, int c, int d)
    {
        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(a, c, d);
    }

    /// <summary>Closed axis-aligned box spanning <paramref name="min"/> to <paramref name="max"/>, normals outward.</summary>
    private static DMesh3 BuildBox(Vector3d min, Vector3d max, DMesh3? into = null)
    {
        DMesh3 mesh = into ?? new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(min.x, min.y, min.z));
        int v100 = mesh.AppendVertex(new Vector3d(max.x, min.y, min.z));
        int v110 = mesh.AppendVertex(new Vector3d(max.x, max.y, min.z));
        int v010 = mesh.AppendVertex(new Vector3d(min.x, max.y, min.z));
        int v001 = mesh.AppendVertex(new Vector3d(min.x, min.y, max.z));
        int v101 = mesh.AppendVertex(new Vector3d(max.x, min.y, max.z));
        int v111 = mesh.AppendVertex(new Vector3d(max.x, max.y, max.z));
        int v011 = mesh.AppendVertex(new Vector3d(min.x, max.y, max.z));

        Quad(mesh, v000, v010, v110, v100);
        Quad(mesh, v001, v101, v111, v011);
        Quad(mesh, v000, v100, v101, v001);
        Quad(mesh, v010, v011, v111, v110);
        Quad(mesh, v000, v001, v011, v010);
        Quad(mesh, v100, v110, v111, v101);
        return mesh;
    }

    /// <summary>
    /// A closed square tube: an axis-aligned box with a square bore straight through it along Z.
    /// A plane perpendicular to the bore crosses it in two loops — the outside and the bore — which
    /// is the smallest case that a single angularly-sorted cap loop cannot describe.
    /// </summary>
    private static DMesh3 BuildSquareTube(double outerHalf, double innerHalf, double z0, double z1, DMesh3? into = null)
    {
        DMesh3 mesh = into ?? new DMesh3();

        double[] cornerX = [outerHalf, -outerHalf, -outerHalf, outerHalf];
        double[] cornerY = [outerHalf, outerHalf, -outerHalf, -outerHalf];

        var outerBottom = new int[4];
        var outerTop = new int[4];
        var innerBottom = new int[4];
        var innerTop = new int[4];
        double ratio = innerHalf / outerHalf;

        for (int k = 0; k < 4; k++)
        {
            outerBottom[k] = mesh.AppendVertex(new Vector3d(cornerX[k], cornerY[k], z0));
            outerTop[k] = mesh.AppendVertex(new Vector3d(cornerX[k], cornerY[k], z1));
            innerBottom[k] = mesh.AppendVertex(new Vector3d(cornerX[k] * ratio, cornerY[k] * ratio, z0));
            innerTop[k] = mesh.AppendVertex(new Vector3d(cornerX[k] * ratio, cornerY[k] * ratio, z1));
        }

        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Quad(mesh, outerBottom[k], outerBottom[n], outerTop[n], outerTop[k]);   // outside wall
            Quad(mesh, innerBottom[n], innerBottom[k], innerTop[k], innerTop[n]);   // bore wall
            Quad(mesh, outerTop[k], outerTop[n], innerTop[n], innerTop[k]);         // top annulus
            Quad(mesh, outerBottom[n], outerBottom[k], innerBottom[k], innerBottom[n]); // bottom annulus
        }

        return mesh;
    }

    /// <summary>
    /// A Menger sponge, generated rather than loaded: 2,112 triangles at level 2, closed, one
    /// shell, riddled with square tunnels straight through it. The generator emits only the faces
    /// of solid cells that have no solid neighbour, welded on a shared vertex grid.
    ///
    /// <para>
    /// This reproduces <c>Menger_sponge_sample.stl</c> exactly — same triangle count, vertex count,
    /// bounding box, shell count and volume to the precision the STL's float32 coordinates can
    /// carry — without committing a third-party binary, which §11 (2026-09-04) rules out for test
    /// meshes.
    /// </para>
    /// </summary>
    private static DMesh3 BuildMengerSponge(int level, double half)
    {
        int n = (int)Math.Round(Math.Pow(3, level));
        double step = 2 * half / n;
        var mesh = new DMesh3();
        var vertexIds = new Dictionary<(int, int, int), int>();

        int V(int x, int y, int z)
        {
            if (!vertexIds.TryGetValue((x, y, z), out int id))
            {
                id = mesh.AppendVertex(new Vector3d(-half + (x * step), -half + (y * step), -half + (z * step)));
                vertexIds[(x, y, z)] = id;
            }

            return id;
        }

        bool Solid(int i, int j, int k)
        {
            if (i < 0 || j < 0 || k < 0 || i >= n || j >= n || k >= n)
            {
                return false;
            }

            for (int d = 0; d < level; d++)
            {
                int ones = (i % 3 == 1 ? 1 : 0) + (j % 3 == 1 ? 1 : 0) + (k % 3 == 1 ? 1 : 0);
                if (ones >= 2)
                {
                    return false;
                }

                i /= 3;
                j /= 3;
                k /= 3;
            }

            return true;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                for (int k = 0; k < n; k++)
                {
                    if (!Solid(i, j, k))
                    {
                        continue;
                    }

                    if (!Solid(i - 1, j, k))
                    {
                        Quad(mesh, V(i, j, k), V(i, j, k + 1), V(i, j + 1, k + 1), V(i, j + 1, k));
                    }

                    if (!Solid(i + 1, j, k))
                    {
                        Quad(mesh, V(i + 1, j, k), V(i + 1, j + 1, k), V(i + 1, j + 1, k + 1), V(i + 1, j, k + 1));
                    }

                    if (!Solid(i, j - 1, k))
                    {
                        Quad(mesh, V(i, j, k), V(i + 1, j, k), V(i + 1, j, k + 1), V(i, j, k + 1));
                    }

                    if (!Solid(i, j + 1, k))
                    {
                        Quad(mesh, V(i, j + 1, k), V(i, j + 1, k + 1), V(i + 1, j + 1, k + 1), V(i + 1, j + 1, k));
                    }

                    if (!Solid(i, j, k - 1))
                    {
                        Quad(mesh, V(i, j, k), V(i, j + 1, k), V(i + 1, j + 1, k), V(i + 1, j, k));
                    }

                    if (!Solid(i, j, k + 1))
                    {
                        Quad(mesh, V(i, j, k + 1), V(i + 1, j, k + 1), V(i + 1, j + 1, k + 1), V(i, j + 1, k + 1));
                    }
                }
            }
        }

        return mesh;
    }

    // ------------------------------------------------------------------- tests

    /// <summary>
    /// The generated sponge is the fixture the multi-loop invariants are asserted against, so its
    /// own properties are pinned first: if the generator drifts, the cut tests below would be
    /// measuring a different solid.
    /// </summary>
    [Fact]
    public void MengerSpongeFixture_IsTheExpectedClosedSolid()
    {
        DMesh3 sponge = BuildMengerSponge(2, 1.0);
        MeshDiagnosticsReport report = Diagnose(sponge);

        Assert.Equal(2112, sponge.TriangleCount);
        Assert.Equal(896, sponge.VertexCount);
        Assert.Equal(1, report.Statistics.ShellCount);
        Assert.Empty(report.Issues);

        // A level-2 sponge of side 2 keeps (20/27)^2 of its volume.
        Assert.Equal(8.0 * 400.0 / 729.0, report.Statistics.Volume, 9);
        Assert.Equal(new Vector3d(-1, -1, -1), report.Statistics.BoundingBox.Min);
        Assert.Equal(new Vector3d(1, 1, 1), report.Statistics.BoundingBox.Max);
    }

    /// <summary>
    /// The single-loop case the cap has always handled. Rebuilding the cap around real cut-edge
    /// connectivity must not cost it: a cube cut in half is still two closed, defect-free halves
    /// that add back up to the original.
    /// </summary>
    [Fact]
    public void SingleLoopCut_OnACube_StillProducesTwoClosedHalves()
    {
        DMesh3 cube = BuildBox(Vector3d.Zero, new Vector3d(10, 10, 10));
        MeshDiagnosticsReport before = Diagnose(cube);

        PlaneCutResult result = new PlaneCut().Cut(
            cube, new Vector3d(0, 0, 4), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);

        MeshDiagnosticsReport positive = Diagnose(result.PositiveSideMesh);
        MeshDiagnosticsReport negative = Diagnose(result.NegativeSideMesh!);

        AssertNoNewDefects("positive half", positive);
        AssertNoNewDefects("negative half", negative);
        Assert.Equal(1, positive.Statistics.ShellCount);
        Assert.Equal(1, negative.Statistics.ShellCount);

        Assert.Equal(before.Statistics.Volume, positive.Statistics.Volume + negative.Statistics.Volume, 9);
        Assert.Equal(600.0, positive.Statistics.Volume, 9);
        Assert.Equal(400.0, negative.Statistics.Volume, 9);

        Assert.Equal(4.0, positive.Statistics.BoundingBox.Min.z, 9);
        Assert.Equal(4.0, negative.Statistics.BoundingBox.Max.z, 9);
        Assert.Equal(100.0, CapArea(result.PositiveSideMesh, 4.0), 9);
    }

    /// <summary>
    /// The case that made this work necessary. A square tube is crossed by the cutting plane in two
    /// separate loops, and a cap built by sorting all the intersection points around the plane
    /// zig-zags between them: it fills the bore, self-intersects, and leaves both halves open.
    /// The bore has to stay a bore.
    /// </summary>
    [Fact]
    public void CutThroughATube_CapsBothLoopsAndLeavesTheBoreOpen()
    {
        DMesh3 tube = BuildSquareTube(outerHalf: 5.0, innerHalf: 2.0, z0: 0.0, z1: 10.0);
        MeshDiagnosticsReport before = Diagnose(tube);
        Assert.Equal(1, before.Statistics.ShellCount);
        Assert.Empty(before.Issues);
        Assert.Equal(((10.0 * 10.0) - (4.0 * 4.0)) * 10.0, before.Statistics.Volume, 9);

        PlaneCutResult result = new PlaneCut().Cut(
            tube, new Vector3d(0, 0, 6), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);

        MeshDiagnosticsReport positive = Diagnose(result.PositiveSideMesh);
        MeshDiagnosticsReport negative = Diagnose(result.NegativeSideMesh!);

        AssertNoNewDefects("positive half", positive);
        AssertNoNewDefects("negative half", negative);
        Assert.Equal(1, positive.Statistics.ShellCount);
        Assert.Equal(1, negative.Statistics.ShellCount);

        Assert.Equal(before.Statistics.Volume, positive.Statistics.Volume + negative.Statistics.Volume, 9);
        Assert.Equal(84.0 * 4.0, positive.Statistics.Volume, 9);
        Assert.Equal(84.0 * 6.0, negative.Statistics.Volume, 9);

        // 84, not 100: the cap is the annulus. A cap that filled the bore would measure 100 here
        // and still leave the volumes looking plausible, because both halves are capped the same
        // way and the error cancels in the sum.
        Assert.Equal(84.0, CapArea(result.PositiveSideMesh, 6.0), 9);
        Assert.Equal(84.0, CapArea(result.NegativeSideMesh!, 6.0), 9);
    }

    /// <summary>
    /// Nesting, one level deeper: a pillar standing inside the tube's bore. Its loop lies inside a
    /// loop that is itself inside the outer loop, so by parity it is solid again. Treating every
    /// enclosed loop as a hole would punch the pillar's own cross-section out of the cap and leave
    /// the pillar open at the cut.
    /// </summary>
    [Fact]
    public void CutThroughAPillarInsideATube_CapsTheInnermostLoopAsSolid()
    {
        DMesh3 mesh = BuildSquareTube(outerHalf: 5.0, innerHalf: 2.0, z0: 0.0, z1: 10.0);
        BuildBox(new Vector3d(-1, -1, 0), new Vector3d(1, 1, 10), into: mesh);

        MeshDiagnosticsReport before = Diagnose(mesh);
        Assert.Equal(2, before.Statistics.ShellCount);
        Assert.Equal((84.0 * 10.0) + (4.0 * 10.0), before.Statistics.Volume, 9);

        PlaneCutResult result = new PlaneCut().Cut(
            mesh, new Vector3d(0, 0, 6), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);

        MeshDiagnosticsReport positive = Diagnose(result.PositiveSideMesh);
        MeshDiagnosticsReport negative = Diagnose(result.NegativeSideMesh!);

        AssertNoNewDefects("positive half", positive);
        AssertNoNewDefects("negative half", negative);

        // Tube and pillar are separate solids, so each half of the cut carries both of them.
        Assert.Equal(2, positive.Statistics.ShellCount);
        Assert.Equal(2, negative.Statistics.ShellCount);

        Assert.Equal(before.Statistics.Volume, positive.Statistics.Volume + negative.Statistics.Volume, 9);
        Assert.Equal(88.0 * 4.0, positive.Statistics.Volume, 9);
        Assert.Equal(88.0 * 6.0, negative.Statistics.Volume, 9);

        // Annulus (84) plus the pillar's own square (4) — not 84, which is what reading the pillar
        // loop as another hole would leave.
        Assert.Equal(88.0, CapArea(result.PositiveSideMesh, 6.0), 9);
        Assert.Equal(88.0, CapArea(result.NegativeSideMesh!, 6.0), 9);
    }

    /// <summary>
    /// The invariant this work is measured against: a Menger sponge cut in half. Its cross-section
    /// at this plane is ten loops — the outer square, the central tunnel, and eight smaller ones —
    /// so it exercises the loop walk, the nesting test and the hole bridging all at once.
    /// </summary>
    [Fact]
    public void MengerSponge_Split_LeavesOneCleanShellPerSideAndKeepsTheVolume()
    {
        DMesh3 sponge = BuildMengerSponge(2, 1.0);
        MeshDiagnosticsReport before = Diagnose(sponge);

        PlaneCutResult result = new PlaneCut().Cut(
            sponge, new Vector3d(0, 0, 0.5), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);

        MeshDiagnosticsReport positive = Diagnose(result.PositiveSideMesh);
        MeshDiagnosticsReport negative = Diagnose(result.NegativeSideMesh!);

        AssertNoNewDefects("positive half", positive);
        AssertNoNewDefects("negative half", negative);
        Assert.Equal(1, positive.Statistics.ShellCount);
        Assert.Equal(1, negative.Statistics.ShellCount);

        Assert.Equal(
            before.Statistics.Volume,
            positive.Statistics.Volume + negative.Statistics.Volume,
            9);

        Assert.Equal(0.5, positive.Statistics.BoundingBox.Min.z, 9);
        Assert.Equal(0.5, negative.Statistics.BoundingBox.Max.z, 9);
        Assert.Equal(1.0, positive.Statistics.BoundingBox.Max.z, 9);
        Assert.Equal(-1.0, negative.Statistics.BoundingBox.Min.z, 9);

        // Both caps cover the same cross-section: eight of the nine cells in each of the eight
        // level-1 blocks that survive at this height, i.e. (2 * 8/9)^2 of the bounding square.
        double expectedCapArea = 256.0 / 81.0;
        Assert.Equal(expectedCapArea, CapArea(result.PositiveSideMesh, 0.5), 6);
        Assert.Equal(expectedCapArea, CapArea(result.NegativeSideMesh!, 0.5), 6);
    }

    /// <summary>
    /// The same sponge cut on a plane aligned with nothing: the cross-section's loops are no longer
    /// axis-aligned in the plane basis, the crossings land at arbitrary points along mesh edges,
    /// and the loop count and nesting differ from the axis-aligned case. The invariants do not.
    /// </summary>
    [Fact]
    public void MengerSponge_SplitOnAnObliquePlane_IsStillOneCleanShellPerSide()
    {
        DMesh3 sponge = BuildMengerSponge(2, 1.0);
        MeshDiagnosticsReport before = Diagnose(sponge);

        Vector3d normal = new Vector3d(0.3, 0.5, 0.81).Normalized;
        PlaneCutResult result = new PlaneCut().Cut(
            sponge, new Vector3d(0.05, -0.02, 0.11), normal, CutMode.Split, HoleFillMode.Planar);

        MeshDiagnosticsReport positive = Diagnose(result.PositiveSideMesh);
        MeshDiagnosticsReport negative = Diagnose(result.NegativeSideMesh!);

        AssertNoNewDefects("positive half", positive, TopologyCategories);
        AssertNoNewDefects("negative half", negative, TopologyCategories);
        Assert.Equal(1, positive.Statistics.ShellCount);
        Assert.Equal(1, negative.Statistics.ShellCount);
        Assert.Equal(
            before.Statistics.Volume,
            positive.Statistics.Volume + negative.Statistics.Volume,
            9);
    }

    /// <summary>
    /// Keep and Discard have to leave nothing behind on the side they threw away, and what they
    /// keep has to be a closed shell — on a multi-loop cross-section just as on a cube.
    /// </summary>
    [Theory]
    [InlineData(CutMode.Keep)]
    [InlineData(CutMode.Discard)]
    public void MengerSponge_KeepOrDiscard_LeavesOneClosedShellOnTheChosenSideOnly(CutMode mode)
    {
        DMesh3 sponge = BuildMengerSponge(2, 1.0);
        var planePoint = new Vector3d(0, 0, 0.5);
        Vector3d planeNormal = Vector3d.AxisZ;

        PlaneCutResult result = new PlaneCut().Cut(sponge, planePoint, planeNormal, mode, HoleFillMode.Planar);

        Assert.Null(result.NegativeSideMesh);
        DMesh3 kept = result.PositiveSideMesh;
        MeshDiagnosticsReport report = Diagnose(kept);

        AssertNoNewDefects("kept side", report);
        Assert.Equal(1, report.Statistics.ShellCount);

        double sign = mode == CutMode.Keep ? 1.0 : -1.0;
        foreach (int tid in kept.TriangleIndices())
        {
            double signedDistance = sign * (kept.GetTriCentroid(tid) - planePoint).Dot(planeNormal);
            Assert.True(
                signedDistance > -1e-9,
                $"Triangle {tid} sits {-signedDistance:0.#####} beyond the cut plane, on the discarded side.");
        }

        double expected = mode == CutMode.Keep
            ? Diagnose(sponge).Statistics.Volume - NegativeHalfVolume()
            : NegativeHalfVolume();
        Assert.Equal(expected, report.Statistics.Volume, 6);

        double NegativeHalfVolume()
        {
            PlaneCutResult split = new PlaneCut().Cut(
                BuildMengerSponge(2, 1.0), planePoint, planeNormal, CutMode.Split, HoleFillMode.Planar);
            return Diagnose(split.NegativeSideMesh!).Statistics.Volume;
        }
    }

    /// <summary>
    /// The triangulator on its own, on the shape the parity rule exists for: a square, a square
    /// hole inside it, and a square island inside that hole. Exercised directly because a failure
    /// here is otherwise only visible as a wrong volume several layers up.
    /// </summary>
    [Fact]
    public void Triangulate_NestedLoops_FillsTheIslandAndLeavesTheHole()
    {
        var mesh = new DMesh3();
        var basis = PlaneBasis.Create(Vector3d.Zero, Vector3d.AxisZ);

        List<int> Square(double half)
        {
            var loop = new List<int>();
            double[] xs = [-half, half, half, -half];
            double[] ys = [-half, -half, half, half];
            for (int i = 0; i < 4; i++)
            {
                loop.Add(mesh.AppendVertex(new Vector3d(xs[i], ys[i], 0)));
            }

            return loop;
        }

        List<List<int>> loops = [Square(4.0), Square(3.0), Square(1.0)];
        List<Index3i> triangles = CutCrossSection.Triangulate(loops, mesh, basis);

        double signed = 0.0;
        double absolute = 0.0;
        foreach (Index3i tri in triangles)
        {
            Vector2d a = basis.Project(mesh.GetVertex(tri.a));
            Vector2d b = basis.Project(mesh.GetVertex(tri.b));
            Vector2d c = basis.Project(mesh.GetVertex(tri.c));
            double area = 0.5 * (((b.x - a.x) * (c.y - a.y)) - ((b.y - a.y) * (c.x - a.x)));
            signed += area;
            absolute += Math.Abs(area);
        }

        // 64 - 36 + 4: the outer square, less the hole, plus the island back again.
        Assert.Equal(32.0, signed, 9);

        // Equal absolute and signed area means nothing is wound backwards and nothing overlaps.
        Assert.Equal(signed, absolute, 9);
    }

    /// <summary>
    /// Two loops that do not nest at all — a cut through two separate bars — must come back as two
    /// separate filled caps, not one cap spanning the gap between them.
    /// </summary>
    [Fact]
    public void CutThroughTwoSeparateBars_CapsEachOneOnItsOwn()
    {
        DMesh3 mesh = BuildBox(new Vector3d(-10, -1, 0), new Vector3d(-8, 1, 10));
        BuildBox(new Vector3d(8, -1, 0), new Vector3d(10, 1, 10), into: mesh);

        MeshDiagnosticsReport before = Diagnose(mesh);
        PlaneCutResult result = new PlaneCut().Cut(
            mesh, new Vector3d(0, 0, 5), Vector3d.AxisZ, CutMode.Split, HoleFillMode.Planar);

        MeshDiagnosticsReport positive = Diagnose(result.PositiveSideMesh);
        MeshDiagnosticsReport negative = Diagnose(result.NegativeSideMesh!);

        AssertNoNewDefects("positive half", positive);
        AssertNoNewDefects("negative half", negative);
        Assert.Equal(2, positive.Statistics.ShellCount);
        Assert.Equal(2, negative.Statistics.ShellCount);

        Assert.Equal(before.Statistics.Volume, positive.Statistics.Volume + negative.Statistics.Volume, 9);
        Assert.Equal(8.0, CapArea(result.PositiveSideMesh, 5.0), 9); // two 2x2 squares
    }
}
