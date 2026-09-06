using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Drain hole drilling (§5.1 "Edit — Drain holes").
///
/// <para>
/// These tests assert invariants measured before and after the operation, never existence
/// (SPECIFICATION.md §11, 2026-09-05). The version of this feature they replace deleted every
/// triangle within the requested radius and added nothing: on the coarse cube below a Ø0.5mm
/// request removed a whole 2 × 2mm face — 16× the requested area — left the vertex count unchanged
/// at 8, and reported success. The old tests passed throughout, because they asserted that
/// triangles had been removed and that <c>DiameterAchieved</c> equalled the requested diameter,
/// which was true by construction.
/// </para>
///
/// <para>
/// The assertions that catch that bug, and are therefore in nearly every test here: the surface
/// area removed must be close to the requested circle's area (πr²); the vertex count must
/// <i>increase</i>, since a hole that constructs nothing constructs no vertices; the mesh must gain
/// exactly one boundary loop per hole; that loop's own measured diameter must match the request;
/// and the bounding box must not move, because a drain hole must never resize the model.
/// </para>
/// </summary>
public class DrainHoleTests
{
    private const double Pi = Math.PI;

    // ------------------------------------------------------------------ fixtures

    /// <summary>
    /// The 12-triangle, 8-vertex 2 × 2 × 2mm cube that exposed the original defect: surface area 24,
    /// volume 8, zero issues. Coarse meshes are where a naive "delete nearby triangles" drill fails
    /// most spectacularly, so it is the primary fixture here.
    /// </summary>
    private static DMesh3 CreateCoarseCube(double size = 2.0)
    {
        var mesh = new DMesh3();
        double s = size;

        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(s, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(s, s, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, s, 0));
        int v4 = mesh.AppendVertex(new Vector3d(0, 0, s));
        int v5 = mesh.AppendVertex(new Vector3d(s, 0, s));
        int v6 = mesh.AppendVertex(new Vector3d(s, s, s));
        int v7 = mesh.AppendVertex(new Vector3d(0, s, s));

        // Outward-facing windings.
        mesh.AppendTriangle(v0, v2, v1);
        mesh.AppendTriangle(v0, v3, v2);
        mesh.AppendTriangle(v4, v5, v6);
        mesh.AppendTriangle(v4, v6, v7);
        mesh.AppendTriangle(v0, v1, v5);
        mesh.AppendTriangle(v0, v5, v4);
        mesh.AppendTriangle(v2, v3, v7);
        mesh.AppendTriangle(v2, v7, v6);
        mesh.AppendTriangle(v0, v4, v7);
        mesh.AppendTriangle(v0, v7, v3);
        mesh.AppendTriangle(v1, v2, v6);
        mesh.AppendTriangle(v1, v6, v5);

        return mesh;
    }

    /// <summary>
    /// The same cube tessellated <paramref name="n"/> × <paramref name="n"/> per face: same geometry,
    /// 12n² triangles. The dense counterpart to <see cref="CreateCoarseCube"/>, so every invariant is
    /// checked both where the hole is far smaller than a triangle and where it spans many.
    /// </summary>
    private static DMesh3 CreateDenseCube(double size = 2.0, int n = 10)
    {
        var mesh = new DMesh3();
        var index = new Dictionary<(int, int, int), int>();

        int Vertex(double x, double y, double z)
        {
            var key = ((int)Math.Round(x * 1e6), (int)Math.Round(y * 1e6), (int)Math.Round(z * 1e6));
            if (index.TryGetValue(key, out int existing))
            {
                return existing;
            }

            int vid = mesh.AppendVertex(new Vector3d(x, y, z));
            index[key] = vid;
            return vid;
        }

        double step = size / n;

        // Each face as a grid, wound outward.
        for (int axis = 0; axis < 3; axis++)
        {
            for (int side = 0; side < 2; side++)
            {
                double fixedValue = side == 0 ? 0.0 : size;
                for (int i = 0; i < n; i++)
                {
                    for (int j = 0; j < n; j++)
                    {
                        double a0 = i * step, a1 = (i + 1) * step;
                        double b0 = j * step, b1 = (j + 1) * step;

                        Vector3d P(double a, double b) => axis switch
                        {
                            0 => new Vector3d(fixedValue, a, b),
                            1 => new Vector3d(b, fixedValue, a),
                            _ => new Vector3d(a, b, fixedValue),
                        };

                        Vector3d p00 = P(a0, b0), p10 = P(a1, b0), p11 = P(a1, b1), p01 = P(a0, b1);
                        int q00 = Vertex(p00.x, p00.y, p00.z);
                        int q10 = Vertex(p10.x, p10.y, p10.z);
                        int q11 = Vertex(p11.x, p11.y, p11.z);
                        int q01 = Vertex(p01.x, p01.y, p01.z);

                        // Orient outward: flip on the low side of each axis.
                        if (side == 0)
                        {
                            mesh.AppendTriangle(q00, q11, q10);
                            mesh.AppendTriangle(q00, q01, q11);
                        }
                        else
                        {
                            mesh.AppendTriangle(q00, q10, q11);
                            mesh.AppendTriangle(q00, q11, q01);
                        }
                    }
                }
            }
        }

        return mesh;
    }

    /// <summary>A closed UV sphere: a dense, curved surface where the hole rim has to follow curvature.</summary>
    private static DMesh3 CreateSphere(double radius, Vector3d center, int rings = 24, int segments = 32)
    {
        var mesh = new DMesh3();
        var grid = new int[rings + 1, segments];

        // Single vertices at the poles, shared by every segment, so the sphere is genuinely closed.
        int north = mesh.AppendVertex(center + new Vector3d(0, 0, radius));
        int south = mesh.AppendVertex(center + new Vector3d(0, 0, -radius));

        for (int r = 0; r <= rings; r++)
        {
            double phi = Math.PI * r / rings;
            for (int s = 0; s < segments; s++)
            {
                if (r == 0)
                {
                    grid[r, s] = north;
                    continue;
                }

                if (r == rings)
                {
                    grid[r, s] = south;
                    continue;
                }

                double theta = 2.0 * Math.PI * s / segments;
                grid[r, s] = mesh.AppendVertex(center + new Vector3d(
                    radius * Math.Sin(phi) * Math.Cos(theta),
                    radius * Math.Sin(phi) * Math.Sin(theta),
                    radius * Math.Cos(phi)));
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                int s2 = (s + 1) % segments;
                int a = grid[r, s], b = grid[r, s2], c = grid[r + 1, s2], d = grid[r + 1, s];
                if (r > 0)
                {
                    mesh.AppendTriangle(a, b, c);
                }

                if (r + 1 < rings)
                {
                    mesh.AppendTriangle(a, c, d);
                }
            }
        }

        return mesh;
    }

    /// <summary>A point on the surface and its outward normal, taken from an actual triangle.</summary>
    private static (Vector3d Point, Vector3d Normal) SurfacePointFacing(DMesh3 mesh, Vector3d direction)
    {
        direction = direction.Normalized;
        int best = -1;
        double bestDot = double.MinValue;
        foreach (int tid in mesh.TriangleIndices())
        {
            double dot = mesh.GetTriNormal(tid).Dot(direction);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = tid;
            }
        }

        Index3i tri = mesh.GetTriangle(best);
        Vector3d centroid = (mesh.GetVertex(tri.a) + mesh.GetVertex(tri.b) + mesh.GetVertex(tri.c)) / 3.0;
        return (centroid, mesh.GetTriNormal(best));
    }

    private static int BoundaryLoopCount(DMesh3 mesh) => new MeshBoundaryLoops(mesh).Loops.Count;

    /// <summary>
    /// The diameter of a boundary loop as the test measures it independently of the operation:
    /// twice the mean distance of the loop's vertices from the hole axis.
    /// </summary>
    private static double MeasureLoopDiameter(DMesh3 mesh, EdgeLoop loop, Vector3d center, Vector3d axis)
    {
        axis = axis.Normalized;
        double sum = 0.0;
        foreach (int vid in loop.Vertices)
        {
            Vector3d d = mesh.GetVertex(vid) - center;
            sum += (d - axis * d.Dot(axis)).Length;
        }

        return 2.0 * sum / loop.Vertices.Length;
    }

    private static void AssertBoundsUnchanged(AxisAlignedBox3d before, AxisAlignedBox3d after, string what)
    {
        Assert.True(before.Min.Distance(after.Min) < 1e-9 && before.Max.Distance(after.Max) < 1e-9,
            $"{what}: a drain hole must not resize the model. Before {before.Min}..{before.Max}, after {after.Min}..{after.Max}.");
    }

    // ------------------------------------------------------------------ the invariants

    /// <summary>
    /// The whole audit finding in one test, on the mesh that exposed it. The old implementation
    /// removed a full 2 × 2mm face (area 4.0) for a Ø0.5mm request (area 0.196) and constructed
    /// nothing at all.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void OnACoarseCube_RemovesTheRequestedCircleAndNothingMore(double diameter)
    {
        DMesh3 mesh = CreateCoarseCube();
        MeshStatistics before = MeshStatistics.Compute(mesh);
        Assert.Equal(12, before.TriangleCount);
        Assert.Equal(8, before.VertexCount);

        var point = new Vector3d(1, 1, 0);
        var normal = new Vector3d(0, 0, -1);

        DrainHoleResult result = DrainHole.PlaceDrainHole(mesh, point, normal, diameter);
        MeshStatistics after = MeshStatistics.Compute(mesh);

        Assert.True(result.HolePlaced, result.Message);

        // 1. The area removed is the requested circle's area, not whatever triangles were nearby.
        double expectedArea = Pi * diameter * diameter / 4.0;
        double removed = before.SurfaceArea - after.SurfaceArea;
        Assert.True(Math.Abs(removed - expectedArea) < expectedArea * 0.02,
            $"Ø{diameter}mm should cost {expectedArea:0.####}mm² of surface, but {removed:0.####}mm² went missing.");
        Assert.True(Math.Abs(result.SurfaceAreaRemoved - removed) < 1e-9,
            "The reported area removed must be the area actually removed.");

        // 2. Geometry was constructed. The old drill left the vertex count at 8.
        Assert.True(after.VertexCount > before.VertexCount,
            $"Drilling must add vertices; the count stayed at {after.VertexCount}, which means nothing was built.");
        Assert.True(result.VerticesAdded > 0);
        Assert.True(result.TrianglesAdded > 0);

        // 3. Exactly one opening, and it is the requested circle.
        var loops = new MeshBoundaryLoops(mesh);
        EdgeLoop loop = Assert.Single(loops.Loops);
        double measured = MeasureLoopDiameter(mesh, loop, point, normal);
        Assert.True(Math.Abs(measured - diameter) < diameter * 0.01,
            $"The opening measures Ø{measured:0.####}mm for a Ø{diameter}mm request.");

        // 4. The operation's own reported diameter is that same measurement, not the request.
        Assert.True(Math.Abs(result.DiameterAchieved - measured) < 1e-9,
            $"DiameterAchieved ({result.DiameterAchieved}) must be measured from the mesh ({measured}).");

        // 5. The model was not resized, and the rest of it is untouched.
        AssertBoundsUnchanged(before.BoundingBox, after.BoundingBox, $"Ø{diameter}mm on the coarse cube");
        Assert.Equal(1, after.ShellCount);
    }

    /// <summary>
    /// Same invariants on a 1200-triangle mesh, where the hole spans many triangles rather than
    /// sitting inside one.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.2)]
    public void OnADenseCube_RemovesTheRequestedCircleAndNothingMore(double diameter)
    {
        DMesh3 mesh = CreateDenseCube();
        MeshStatistics before = MeshStatistics.Compute(mesh);
        Assert.True(before.TriangleCount >= 1000, $"Fixture should be dense, has {before.TriangleCount} triangles.");

        var point = new Vector3d(1, 1, 0);
        var normal = new Vector3d(0, 0, -1);

        DrainHoleResult result = DrainHole.PlaceDrainHole(mesh, point, normal, diameter);
        MeshStatistics after = MeshStatistics.Compute(mesh);

        Assert.True(result.HolePlaced, result.Message);

        double expectedArea = Pi * diameter * diameter / 4.0;
        double removed = before.SurfaceArea - after.SurfaceArea;
        Assert.True(Math.Abs(removed - expectedArea) < expectedArea * 0.02,
            $"Ø{diameter}mm should cost {expectedArea:0.####}mm², but {removed:0.####}mm² went missing.");

        // On a dense mesh the hole swallows more existing vertices than the rim adds, so the net
        // count may fall — the proof that geometry was constructed here is the rim itself: a ring of
        // vertices sitting exactly on the requested circle, where the original mesh had none.
        Assert.NotEqual(before.VertexCount, after.VertexCount);

        EdgeLoop loop = Assert.Single(new MeshBoundaryLoops(mesh).Loops);
        Assert.True(loop.Vertices.Length >= 16,
            $"The opening is a {loop.Vertices.Length}-sided polygon; a circle needs a real rim.");
        foreach (int vid in loop.Vertices)
        {
            Vector3d d = mesh.GetVertex(vid) - point;
            double radial = (d - normal.Normalized * d.Dot(normal.Normalized)).Length;
            Assert.True(Math.Abs(radial - diameter / 2.0) < diameter * 0.005,
                $"A rim vertex sits {radial:0.#####} from the axis, not {diameter / 2.0}.");
        }

        double measured = MeasureLoopDiameter(mesh, loop, point, normal);
        Assert.True(Math.Abs(measured - diameter) < diameter * 0.01,
            $"The opening measures Ø{measured:0.####}mm for a Ø{diameter}mm request.");

        AssertBoundsUnchanged(before.BoundingBox, after.BoundingBox, $"Ø{diameter}mm on the dense cube");
        Assert.Equal(1, after.ShellCount);
    }

    /// <summary>A curved surface: the rim has to follow the sphere, not float in the tangent plane.</summary>
    [Fact]
    public void OnACurvedSurface_CutsTheRequestedCircle()
    {
        DMesh3 mesh = CreateSphere(radius: 5.0, center: Vector3d.Zero);
        MeshStatistics before = MeshStatistics.Compute(mesh);

        // Off the bounding box's extremes, so the box is untouched by removing surface here.
        (Vector3d point, Vector3d normal) = SurfacePointFacing(mesh, new Vector3d(1, 1, 1));
        const double diameter = 1.5;

        DrainHoleResult result = DrainHole.PlaceDrainHole(mesh, point, normal, diameter);
        MeshStatistics after = MeshStatistics.Compute(mesh);

        Assert.True(result.HolePlaced, result.Message);

        double expectedArea = Pi * diameter * diameter / 4.0;
        double removed = before.SurfaceArea - after.SurfaceArea;
        Assert.True(Math.Abs(removed - expectedArea) < expectedArea * 0.05,
            $"Ø{diameter}mm should cost about {expectedArea:0.####}mm² on a curved surface, but {removed:0.####}mm² went missing.");

        Assert.True(after.VertexCount > before.VertexCount);

        EdgeLoop loop = Assert.Single(new MeshBoundaryLoops(mesh).Loops);
        double measured = MeasureLoopDiameter(mesh, loop, point, normal);
        Assert.True(Math.Abs(measured - diameter) < diameter * 0.02,
            $"The opening measures Ø{measured:0.####}mm for a Ø{diameter}mm request.");

        AssertBoundsUnchanged(before.BoundingBox, after.BoundingBox, "a hole on a sphere");

        // The rim follows the sphere: every new boundary vertex is on (or just inside) the surface.
        foreach (int vid in loop.Vertices)
        {
            double r = mesh.GetVertex(vid).Length;
            Assert.True(Math.Abs(r - 5.0) < 0.05,
                $"A rim vertex sits {r:0.####} from the centre; the sphere's surface is at 5.");
        }
    }

    /// <summary>
    /// The requested diameter must actually drive the result. Two different requests on identical
    /// meshes must produce measurably different openings, with the areas in the ratio of the squares.
    /// </summary>
    [Fact]
    public void DifferentDiameters_ProduceProportionallyDifferentOpenings()
    {
        DMesh3 small = CreateCoarseCube();
        DMesh3 large = CreateCoarseCube();
        double areaBefore = MeshStatistics.Compute(small).SurfaceArea;

        var point = new Vector3d(1, 1, 0);
        var normal = new Vector3d(0, 0, -1);

        DrainHoleResult smallResult = DrainHole.PlaceDrainHole(small, point, normal, 0.5);
        DrainHoleResult largeResult = DrainHole.PlaceDrainHole(large, point, normal, 1.5);

        Assert.True(smallResult.HolePlaced && largeResult.HolePlaced);

        double smallRemoved = areaBefore - MeshStatistics.Compute(small).SurfaceArea;
        double largeRemoved = areaBefore - MeshStatistics.Compute(large).SurfaceArea;

        Assert.True(largeRemoved > smallRemoved * 8.0 && largeRemoved < smallRemoved * 10.0,
            $"Tripling the diameter must cost about nine times the area: {smallRemoved:0.####}mm² vs {largeRemoved:0.####}mm².");

        Assert.True(largeResult.DiameterAchieved > smallResult.DiameterAchieved * 2.9,
            $"The measured openings must differ with the request: {smallResult.DiameterAchieved} vs {largeResult.DiameterAchieved}.");
    }

    /// <summary>
    /// Three holes, three openings. A drill that merges holes together, or leaves the mesh closed,
    /// fails here.
    /// </summary>
    [Fact]
    public void MultipleHoles_LeaveOneBoundaryLoopEach()
    {
        DMesh3 mesh = CreateDenseCube();
        MeshStatistics before = MeshStatistics.Compute(mesh);
        const double diameter = 0.6;

        var placements = new[]
        {
            (Point: new Vector3d(1, 1, 0), Normal: new Vector3d(0, 0, -1)),
            (Point: new Vector3d(1, 1, 2), Normal: new Vector3d(0, 0, 1)),
            (Point: new Vector3d(0, 1, 1), Normal: new Vector3d(-1, 0, 0)),
        };

        foreach ((Vector3d p, Vector3d n) in placements)
        {
            DrainHoleResult r = DrainHole.PlaceDrainHole(mesh, p, n, diameter);
            Assert.True(r.HolePlaced, r.Message);
        }

        MeshStatistics after = MeshStatistics.Compute(mesh);

        Assert.Equal(placements.Length, BoundaryLoopCount(mesh));

        double expected = placements.Length * Pi * diameter * diameter / 4.0;
        double removed = before.SurfaceArea - after.SurfaceArea;
        Assert.True(Math.Abs(removed - expected) < expected * 0.02,
            $"Three Ø{diameter}mm holes should cost {expected:0.####}mm², but {removed:0.####}mm² went missing.");

        AssertBoundsUnchanged(before.BoundingBox, after.BoundingBox, "three holes");
        Assert.Equal(1, after.ShellCount);
    }

    /// <summary>
    /// The drill must not introduce the defects Inspect exists to report. Decimation shipped a
    /// summary denying the 67 self-intersections it had just created (§11, 2026-09-06); the lesson is
    /// that a local construction has to be checked against whole-mesh invariants afterwards. After
    /// drilling, the only issue in the model may be the drain hole itself.
    /// </summary>
    [Theory]
    [InlineData(0.5, 0.0)]
    [InlineData(1.2, 0.0)]
    [InlineData(0.6, 0.3)]
    public void TheDrilledMesh_HasNoDefectsBeyondTheHoleItself(double diameter, double countersink)
    {
        foreach (DMesh3 mesh in new[] { CreateCoarseCube(), CreateDenseCube() })
        {
            var point = new Vector3d(1, 1, 0);
            var normal = new Vector3d(0, 0, -1);

            var before = MeshDiagnosticsRunner.Run(mesh, Detectors());
            Assert.Empty(before.Issues);

            DrainHoleResult result = DrainHole.PlaceDrainHole(mesh, point, normal, diameter, countersink);
            Assert.True(result.HolePlaced, result.Message);

            var after = MeshDiagnosticsRunner.Run(mesh, Detectors());
            var unexpected = after.Issues.Where(i => i.Category != "BoundaryHole").ToList();
            Assert.True(unexpected.Count == 0,
                $"Drilling Ø{diameter}mm introduced {after.Summary}");
            Assert.True(after.Issues.Count == 1, $"expected just the drain hole; got '{after.Summary}' loops={new MeshBoundaryLoops(mesh).Loops.Count} tris={mesh.TriangleCount} verts={mesh.VertexCount}");
        }
    }

    private static IMeshDetector[] Detectors() =>
    [
        new NonManifoldDetector(),
        new BoundaryHoleDetector(),
        new SelfIntersectionDetector(),
        new InvertedNormalDetector(),
        new DegenerateTriangleDetector(),
        new DuplicateVertexDetector(),
        new DisconnectedShellDetector(),
    ];

    // ------------------------------------------------------------------ countersink

    /// <summary>
    /// A countersink must produce a measurably different mesh from the same placement without one —
    /// the "Smooth" hole-fill lesson (§11, 2026-09-05): a control that names a distinct behaviour
    /// must produce a distinct result. It must also leave the opening at the requested diameter: the
    /// chamfer widens the surface, not the hole.
    /// </summary>
    [Fact]
    public void Countersink_ChangesTheMeshAndKeepsTheOpeningAtTheRequestedDiameter()
    {
        DMesh3 plain = CreateCoarseCube();
        DMesh3 sunk = CreateCoarseCube();
        MeshStatistics before = MeshStatistics.Compute(plain);

        var point = new Vector3d(1, 1, 0);
        var normal = new Vector3d(0, 0, -1);
        const double diameter = 0.5;
        const double countersink = 0.4;

        DrainHoleResult plainResult = DrainHole.PlaceDrainHole(plain, point, normal, diameter);
        DrainHoleResult sunkResult = DrainHole.PlaceDrainHole(sunk, point, normal, diameter, countersink);

        Assert.True(plainResult.HolePlaced && sunkResult.HolePlaced, sunkResult.Message);

        MeshStatistics plainStats = MeshStatistics.Compute(plain);
        MeshStatistics sunkStats = MeshStatistics.Compute(sunk);

        // Measurably different geometry, not just a different message.
        Assert.True(sunkStats.VertexCount > plainStats.VertexCount,
            $"A countersink must add geometry: {sunkStats.VertexCount} vertices vs {plainStats.VertexCount} without one.");
        // The chamfer's own geometry, pinned by measurement rather than by implementation: a 45°
        // cone from radius r+c on the surface down to radius r at depth c removes an extra annulus of
        // πc(2r+c) and puts back a cone wall √2 times that, so the mesh must gain exactly
        // πc(2r+c)(√2-1) of surface over the plain hole. A countersink that did nothing, or that only
        // moved vertices about, cannot land on that number.
        double expectedGain = Pi * countersink * (2 * (diameter / 2.0) + countersink) * (Math.Sqrt(2.0) - 1.0);
        double actualGain = sunkStats.SurfaceArea - plainStats.SurfaceArea;
        Assert.True(Math.Abs(actualGain - expectedGain) < expectedGain * 0.05,
            $"The chamfer should add {expectedGain:0.#####}mm² over a plain hole, but the difference is {actualGain:0.#####}mm².");

        // The chamfer is real and measured, not echoed from the request.
        Assert.True(Math.Abs(sunkResult.CountersinkAchieved - countersink) < countersink * 0.05,
            $"The chamfer measures {sunkResult.CountersinkAchieved:0.####}mm for a {countersink}mm request.");
        Assert.Equal(0.0, plainResult.CountersinkAchieved);

        // And the opening itself is still the hole the user asked for.
        EdgeLoop loop = Assert.Single(new MeshBoundaryLoops(sunk).Loops);
        double measured = MeasureLoopDiameter(sunk, loop, point, normal);
        Assert.True(Math.Abs(measured - diameter) < diameter * 0.01,
            $"The countersunk opening measures Ø{measured:0.####}mm for a Ø{diameter}mm request.");

        AssertBoundsUnchanged(before.BoundingBox, sunkStats.BoundingBox, "a countersunk hole");
    }

    /// <summary>Two different countersink depths must produce two different meshes.</summary>
    [Fact]
    public void DeeperCountersink_RemovesMoreSurface()
    {
        DMesh3 shallow = CreateDenseCube();
        DMesh3 deep = CreateDenseCube();
        var point = new Vector3d(1, 1, 0);
        var normal = new Vector3d(0, 0, -1);

        DrainHoleResult shallowResult = DrainHole.PlaceDrainHole(shallow, point, normal, 0.5, 0.2);
        DrainHoleResult deepResult = DrainHole.PlaceDrainHole(deep, point, normal, 0.5, 0.6);

        Assert.True(shallowResult.HolePlaced && deepResult.HolePlaced);
        Assert.True(deepResult.CountersinkAchieved > shallowResult.CountersinkAchieved * 2.5,
            $"Depths must differ: {shallowResult.CountersinkAchieved} vs {deepResult.CountersinkAchieved}.");
        // A deeper 45° chamfer cuts a wider circle in the surface and hangs more cone wall under it,
        // so the mesh keeps more surface, i.e. the net area removed falls.
        Assert.True(deepResult.SurfaceAreaRemoved < shallowResult.SurfaceAreaRemoved,
            $"A deeper countersink must change the surface more: {deepResult.SurfaceAreaRemoved:0.#####} vs {shallowResult.SurfaceAreaRemoved:0.#####}.");
        Assert.True(Math.Abs(deepResult.DiameterAchieved - 0.5) < 0.005 && Math.Abs(shallowResult.DiameterAchieved - 0.5) < 0.005,
            "Neither chamfer may change the opening the user asked for.");
    }

    /// <summary>
    /// A countersink deeper than the material under the pick is clamped and said so, rather than
    /// pushing a cone out through the far side of the model (§4, "Honest diagnostics").
    /// </summary>
    [Fact]
    public void CountersinkDeeperThanTheMaterial_IsClampedAndReported()
    {
        DMesh3 mesh = CreateCoarseCube();
        AxisAlignedBox3d before = mesh.GetBounds();

        DrainHoleResult result = DrainHole.PlaceDrainHole(
            mesh, new Vector3d(1, 1, 0), new Vector3d(0, 0, -1), diameter: 0.5, countersinkDepth: 5.0);

        Assert.True(result.HolePlaced, result.Message);
        Assert.True(result.CountersinkAchieved < 2.0,
            $"The cube is 2mm thick; a {result.CountersinkAchieved}mm chamfer would leave the far side.");
        Assert.Contains("reduced", result.Message, StringComparison.OrdinalIgnoreCase);
        AssertBoundsUnchanged(before, mesh.GetBounds(), "an over-deep countersink");
    }

    // ------------------------------------------------------------------ honesty and refusal

    /// <summary>
    /// A hole that does not fit must be refused with the mesh left exactly as it was, naming the
    /// largest diameter that does fit — the shape of honesty §11 (2026-09-05) established for
    /// decimation ("names the target it missed and why").
    /// </summary>
    [Fact]
    public void AHoleTooBigForTheSurface_IsRefusedAndTheMeshIsUntouched()
    {
        DMesh3 mesh = CreateCoarseCube();
        MeshStatistics before = MeshStatistics.Compute(mesh);

        DrainHoleResult result = DrainHole.PlaceDrainHole(
            mesh, new Vector3d(1, 1, 0), new Vector3d(0, 0, -1), diameter: 6.0);

        MeshStatistics after = MeshStatistics.Compute(mesh);

        Assert.False(result.HolePlaced);
        Assert.Equal(before.TriangleCount, after.TriangleCount);
        Assert.Equal(before.VertexCount, after.VertexCount);
        Assert.Equal(before.SurfaceArea, after.SurfaceArea, 12);
        Assert.Equal(0, BoundaryLoopCount(mesh));
        Assert.Equal(0.0, result.DiameterAchieved);
        Assert.Contains("Ø", result.Message);
        Assert.Contains("unchanged", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>DiameterAchieved</c> must be a measurement, so it must be capable of disagreeing with the
    /// request. This checks the two halves of that: it equals an independent measurement of the
    /// resulting boundary loop, and it is zero — not the request — when nothing was cut.
    /// </summary>
    [Fact]
    public void DiameterAchieved_IsMeasuredFromTheMeshAndNeverEchoesTheRequest()
    {
        DMesh3 mesh = CreateDenseCube();
        var point = new Vector3d(1, 1, 0);
        var normal = new Vector3d(0, 0, -1);

        DrainHoleResult placed = DrainHole.PlaceDrainHole(mesh, point, normal, 0.9);
        EdgeLoop loop = Assert.Single(new MeshBoundaryLoops(mesh).Loops);
        Assert.Equal(MeasureLoopDiameter(mesh, loop, point, normal), placed.DiameterAchieved, 9);

        DrainHoleResult refused = DrainHole.PlaceDrainHole(
            CreateCoarseCube(), new Vector3d(1, 1, 0), new Vector3d(0, 0, -1), diameter: 9.0);
        Assert.False(refused.HolePlaced);
        Assert.NotEqual(9.0, refused.DiameterAchieved);
        Assert.Equal(9.0, refused.DiameterRequested);
    }

    /// <summary>A hole must never be cut into an existing opening, silently enlarging it.</summary>
    [Fact]
    public void AHoleOverAnExistingBoundary_IsRefused()
    {
        DMesh3 mesh = CreateCoarseCube();
        // Open the bottom face by removing one of its two triangles.
        int victim = mesh.TriangleIndices().First(tid => mesh.GetTriNormal(tid).Dot(new Vector3d(0, 0, -1)) > 0.9);
        mesh.RemoveTriangle(victim);

        MeshStatistics before = MeshStatistics.Compute(mesh);
        DrainHoleResult result = DrainHole.PlaceDrainHole(
            mesh, new Vector3d(1.4, 0.6, 0), new Vector3d(0, 0, -1), diameter: 0.4);
        MeshStatistics after = MeshStatistics.Compute(mesh);

        Assert.False(result.HolePlaced, "A drain hole must not be cut into an existing boundary.");
        Assert.Equal(before.TriangleCount, after.TriangleCount);
        Assert.Equal(before.SurfaceArea, after.SurfaceArea, 12);
    }

    [Fact]
    public void InvalidDiameter_Throws()
    {
        DMesh3 mesh = CreateCoarseCube();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DrainHole.PlaceDrainHole(mesh, new Vector3d(1, 1, 0), new Vector3d(0, 0, -1), diameter: 0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DrainHole.PlaceDrainHole(mesh, new Vector3d(1, 1, 0), new Vector3d(0, 0, -1), diameter: -1.0));
    }

    [Fact]
    public void NegativeCountersink_Throws()
    {
        DMesh3 mesh = CreateCoarseCube();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DrainHole.PlaceDrainHole(mesh, new Vector3d(1, 1, 0), new Vector3d(0, 0, -1), 0.5, countersinkDepth: -0.5));
    }

    [Fact]
    public void EmptyMesh_ReportsNoHole()
    {
        var mesh = new DMesh3();
        DrainHoleResult result = DrainHole.PlaceDrainHole(mesh, Vector3d.Zero, new Vector3d(0, 0, 1), 1.0);

        Assert.False(result.HolePlaced);
        Assert.Equal(0, result.TrianglesRemoved);
        Assert.Equal(0, result.TrianglesAdded);
    }

    // ------------------------------------------------------------------ the operation wrapper

    /// <summary>
    /// The operation's summary is what the user reads, so it must carry the measured opening, not the
    /// request restated (§11, 2026-09-06).
    /// </summary>
    [Fact]
    public void Operation_ReportsTheMeasuredResult()
    {
        DMesh3 mesh = CreateDenseCube();
        MeshStatistics before = MeshStatistics.Compute(mesh);

        var operation = new PlaceDrainHoleOperation(
            surfacePoint: new Vector3d(1, 1, 0),
            surfaceNormal: new Vector3d(0, 0, -1),
            diameter: 0.8,
            countersinkDepth: 0.3);

        // Preview must not touch the mesh.
        operation.Preview(mesh);
        Assert.Equal(before.TriangleCount, mesh.TriangleCount);
        Assert.Equal(before.VertexCount, mesh.VertexCount);

        OperationResult result = operation.Apply(mesh);
        MeshStatistics after = MeshStatistics.Compute(mesh);

        Assert.True(result.Changed);
        Assert.Contains("measured", result.Summary);
        Assert.Contains("countersink", result.Summary);
        Assert.True(after.VertexCount > before.VertexCount);
        Assert.Equal(1, BoundaryLoopCount(mesh));
        AssertBoundsUnchanged(before.BoundingBox, after.BoundingBox, "the operation wrapper");
    }

    /// <summary>A refusal must reach the user as a refusal, not as a silent no-op.</summary>
    [Fact]
    public void Operation_ReportsARefusalWithoutChangingTheMesh()
    {
        DMesh3 mesh = CreateCoarseCube();
        MeshStatistics before = MeshStatistics.Compute(mesh);

        var operation = new PlaceDrainHoleOperation(
            new Vector3d(1, 1, 0), new Vector3d(0, 0, -1), diameter: 8.0);

        OperationResult result = operation.Apply(mesh);

        Assert.False(result.Changed);
        Assert.Contains("Could not", result.Summary);
        Assert.Equal(before.TriangleCount, mesh.TriangleCount);
        Assert.Equal(before.VertexCount, mesh.VertexCount);
    }
}
