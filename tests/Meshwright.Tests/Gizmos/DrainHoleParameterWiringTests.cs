using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Gizmos;

/// <summary>
/// The wiring between the Drain Holes panel's fields, the gizmo that places holes, and the geometry
/// that gets cut.
///
/// <para>
/// This is the gap a test suite cannot see unless something asserts the wiring itself (§11,
/// 2026-09-06): the drain-hole operation was always driven directly in tests, so nobody noticed that
/// <see cref="DrainHoleGizmo"/> placed every hole at a hard-coded Ø2mm regardless of the Diameter
/// field, that the viewport marker was drawn at 0.3× the hole's radius, or that Countersink Depth was
/// parsed and then never used. Every test here starts from a UI value and ends at a measurement.
/// </para>
/// </summary>
public class DrainHoleParameterWiringTests
{
    private const int Rings = 16;
    private const int Segments = 24;
    private static readonly Vector3 ModelCenter = new(0f, 0f, 0f);

    private static DMesh3 CreateSphere(float radius)
    {
        var mesh = new DMesh3();
        var center = new Vector3d(ModelCenter.X, ModelCenter.Y, ModelCenter.Z);
        var grid = new int[Rings + 1, Segments];

        int north = mesh.AppendVertex(center + new Vector3d(0, 0, radius));
        int south = mesh.AppendVertex(center + new Vector3d(0, 0, -radius));

        for (int ring = 0; ring <= Rings; ring++)
        {
            double phi = Math.PI * ring / Rings;
            for (int seg = 0; seg < Segments; seg++)
            {
                if (ring == 0)
                {
                    grid[ring, seg] = north;
                    continue;
                }

                if (ring == Rings)
                {
                    grid[ring, seg] = south;
                    continue;
                }

                double theta = 2.0 * Math.PI * seg / Segments;
                grid[ring, seg] = mesh.AppendVertex(center + new Vector3d(
                    radius * Math.Sin(phi) * Math.Cos(theta),
                    radius * Math.Sin(phi) * Math.Sin(theta),
                    radius * Math.Cos(phi)));
            }
        }

        for (int ring = 0; ring < Rings; ring++)
        {
            for (int seg = 0; seg < Segments; seg++)
            {
                int seg2 = (seg + 1) % Segments;
                int a = grid[ring, seg], b = grid[ring, seg2], c = grid[ring + 1, seg2], d = grid[ring + 1, seg];
                if (ring > 0)
                {
                    mesh.AppendTriangle(a, b, c);
                }

                if (ring + 1 < Rings)
                {
                    mesh.AppendTriangle(a, c, d);
                }
            }
        }

        return mesh;
    }

    // ---------------------------------------------------------------- the gizmo

    /// <summary>The gizmo places the hole at the diameter it was given, not at a constant.</summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(1.5)]
    [InlineData(4.0)]
    public void APlacedHole_CarriesTheRequestedDiameter(double diameter)
    {
        DMesh3 mesh = CreateSphere(10f);
        var harness = ViewportHarness.Framed(ModelCenter, 10f);
        var gizmo = new DrainHoleGizmo(mesh) { Diameter = diameter, CountersinkDepth = 0.25 };

        Assert.True(harness.PressAtPixel(gizmo, harness.CenterPixel, mesh: mesh));

        PlacedDrainHole hole = Assert.Single(gizmo.Holes);
        Assert.Equal(diameter, hole.Diameter);
        Assert.Equal(0.25, hole.CountersinkDepth);
    }

    /// <summary>
    /// The viewport marker is the hole's own radius. The old code drew it at
    /// <c>Diameter / 2 * 0.3</c> — a fudge factor that made every marker misdescribe its hole.
    /// </summary>
    [Theory]
    [InlineData(0.5)]
    [InlineData(2.0)]
    [InlineData(6.0)]
    public void TheViewportMarker_IsDrawnAtTheHolesRealRadius(double diameter)
    {
        var hole = new PlacedDrainHole(1, Vector3d.Zero, new Vector3d(0, 0, 1), diameter);
        Assert.Equal((float)(diameter / 2.0), DrainHoleGizmo.MarkerRadius(hole), 6);
    }

    // ---------------------------------------------------------------- the panel

    /// <summary>
    /// Typing a diameter and then placing a hole must produce a hole of that diameter, listed at that
    /// diameter. This is the exact defect the audit saw on screen: "Hole 1: Ø2mm" after setting 0.5.
    /// </summary>
    [AvaloniaFact]
    public void TypingADiameterThenPlacingAHole_ListsTheDiameterThatWasTyped()
    {
        DMesh3 mesh = CreateSphere(10f);
        var document = new MeshDocument();
        document.Load(mesh);

        var gizmo = new DrainHoleGizmo(mesh);
        var panel = new DrainHolePanel();
        panel.SetDocument(document);
        panel.SetGizmo(gizmo);

        panel.SetDiameterTextForTesting("0.5");
        panel.SetCountersinkTextForTesting("0.2");

        var harness = ViewportHarness.Framed(ModelCenter, 10f);
        Assert.True(harness.PressAtPixel(gizmo, harness.CenterPixel, mesh: mesh));

        PlacedDrainHole hole = Assert.Single(gizmo.Holes);
        Assert.Equal(0.5, hole.Diameter);
        Assert.Equal(0.2, hole.CountersinkDepth);

        string listed = Assert.Single(panel.PlacedHoleEntriesForTesting);
        Assert.Contains("Ø0.5mm", listed);
        Assert.Contains("0.2mm countersink", listed);
        Assert.DoesNotContain("Ø2mm", listed);
    }

    /// <summary>Changing the field after placing re-describes the hole rather than leaving a stale size.</summary>
    [AvaloniaFact]
    public void ChangingTheDiameterAfterPlacing_UpdatesTheHoleAndTheList()
    {
        DMesh3 mesh = CreateSphere(10f);
        var document = new MeshDocument();
        document.Load(mesh);

        var gizmo = new DrainHoleGizmo(mesh);
        var panel = new DrainHolePanel();
        panel.SetDocument(document);
        panel.SetGizmo(gizmo);

        var harness = ViewportHarness.Framed(ModelCenter, 10f);
        Assert.True(harness.PressAtPixel(gizmo, harness.CenterPixel, mesh: mesh));

        panel.SetDiameterTextForTesting("3.25");

        Assert.Equal(3.25, Assert.Single(gizmo.Holes).Diameter);
        Assert.Contains("Ø3.25mm", Assert.Single(panel.PlacedHoleEntriesForTesting));
    }

    /// <summary>
    /// The whole path, end to end: a diameter typed into the panel, a hole placed with the gizmo,
    /// Apply pressed, and the mesh measured. The opening in the model must be the number that was
    /// typed — and the panel's reported summary must be that measurement, not the request echoed back.
    /// </summary>
    [AvaloniaFact]
    public async Task ApplyingFromThePanel_CutsAHoleOfTheDiameterInTheField()
    {
        DMesh3 mesh = CreateSphere(10f);
        var document = new MeshDocument();
        document.Load(mesh);
        MeshStatistics before = MeshStatistics.Compute(document.Mesh!);

        var gizmo = new DrainHoleGizmo(mesh);
        var panel = new DrainHolePanel();
        panel.SetDocument(document);
        panel.SetGizmo(gizmo);
        panel.SetDiameterTextForTesting("1.5");

        var harness = ViewportHarness.Framed(ModelCenter, 10f);
        Assert.True(harness.PressAtPixel(gizmo, harness.CenterPixel, mesh: mesh));
        PlacedDrainHole hole = Assert.Single(gizmo.Holes);

        await panel.InvokeApplyAllForTesting();

        MeshStatistics after = MeshStatistics.Compute(document.Mesh!);

        // One opening, of the requested size, measured off the model itself.
        var loops = new MeshBoundaryLoops(document.Mesh!);
        EdgeLoop loop = Assert.Single(loops.Loops);
        Vector3d axis = hole.SurfaceNormal.Normalized;
        double sum = 0.0;
        foreach (int vid in loop.Vertices)
        {
            Vector3d d = document.Mesh!.GetVertex(vid) - hole.SurfacePoint;
            sum += (d - axis * d.Dot(axis)).Length;
        }

        double measured = 2.0 * sum / loop.Vertices.Length;
        Assert.True(Math.Abs(measured - 1.5) < 0.05,
            $"The panel cut a Ø{measured:0.###}mm hole for a Ø1.5mm request.");

        double areaRemoved = before.SurfaceArea - after.SurfaceArea;
        double expected = Math.PI * 1.5 * 1.5 / 4.0;
        Assert.True(Math.Abs(areaRemoved - expected) < expected * 0.1,
            $"A Ø1.5mm hole should cost about {expected:0.####}mm², but {areaRemoved:0.####}mm² went missing.");

        Assert.Contains("measured", panel.OperationResultMessage!);
        Assert.Contains("1.5", panel.OperationResultMessage!);
    }

    /// <summary>
    /// The countersink field has to reach the geometry. Two Apply runs from identical panels, one with
    /// a countersink and one without, must leave measurably different models — the "Smooth" hole-fill
    /// lesson applied to a control that did nothing at all (§11, 2026-09-05 and 2026-09-06).
    /// </summary>
    [AvaloniaFact]
    public async Task TheCountersinkField_ChangesTheGeometryItClaimsToChange()
    {
        async Task<MeshStatistics> Run(string countersink)
        {
            DMesh3 mesh = CreateSphere(10f);
            var document = new MeshDocument();
            document.Load(mesh);

            var gizmo = new DrainHoleGizmo(mesh);
            var panel = new DrainHolePanel();
            panel.SetDocument(document);
            panel.SetGizmo(gizmo);
            panel.SetDiameterTextForTesting("1.5");
            panel.SetCountersinkTextForTesting(countersink);

            var harness = ViewportHarness.Framed(ModelCenter, 10f);
            Assert.True(harness.PressAtPixel(gizmo, harness.CenterPixel, mesh: mesh));
            await panel.InvokeApplyAllForTesting();

            return MeshStatistics.Compute(document.Mesh!);
        }

        MeshStatistics plain = await Run("0.0");
        MeshStatistics sunk = await Run("0.8");

        Assert.NotEqual(plain.VertexCount, sunk.VertexCount);
        Assert.True(Math.Abs(plain.SurfaceArea - sunk.SurfaceArea) > 0.05,
            $"The countersink field left the surface area unchanged: {plain.SurfaceArea:0.####} vs {sunk.SurfaceArea:0.####}.");
        Assert.True(sunk.SurfaceArea > plain.SurfaceArea,
            "A 45° chamfer hangs more cone wall under the surface than the wider circle removes.");
    }
}
