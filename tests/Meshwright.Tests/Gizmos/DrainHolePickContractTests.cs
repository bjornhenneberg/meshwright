using System.Numerics;
using g3;
using Meshwright.App.Gizmos;
using Xunit;

namespace Meshwright.Tests.Gizmos;

/// <summary>
/// <see cref="DrainHoleGizmo"/>'s half of the picking contract, driven through a real camera by
/// <see cref="ViewportHarness"/>.
///
/// <para>
/// This gizmo picks against the mesh surface rather than against widget geometry, so its contract
/// differs from <see cref="GizmoPickContractTests"/>: instead of "is the handle big enough", the
/// question is whether the hole lands under the cursor. That accuracy invariant is what the crash
/// this work started from was hiding — <c>ToRay3d</c> threw before any hole could be placed at all,
/// so nothing downstream of the raycast had ever been exercised through a real camera.
/// </para>
/// </summary>
public class DrainHolePickContractTests
{
    public static TheoryData<float, double> Scales()
    {
        var data = new TheoryData<float, double>();
        foreach (float radius in new[] { 0.5f, 5f, 50f, 500f })
        {
            foreach (double scaling in new[] { 1.0, 2.0 })
            {
                data.Add(radius, scaling);
            }
        }

        return data;
    }

    private static readonly Vector3 ModelCenter = new(2f, -1f, 3f);

    private const int Rings = 16;
    private const int Segments = 24;

    /// <summary>
    /// A UV sphere of the given radius centred on <see cref="ModelCenter"/>. Built directly rather
    /// than via marching cubes so the surface radius is exact at the vertices, which lets
    /// <see cref="ThePlacedHole_SitsOnTheSurfaceWithAnOutwardNormal"/> assert a tight tolerance.
    /// A sphere also guarantees the centre pixel is over the mesh from any camera angle.
    /// </summary>
    private static DMesh3 CreateSphere(float radius)
    {
        var mesh = new DMesh3();
        var center = new Vector3d(ModelCenter.X, ModelCenter.Y, ModelCenter.Z);

        var grid = new int[Rings + 1, Segments];
        for (int ring = 0; ring <= Rings; ring++)
        {
            double phi = Math.PI * ring / Rings;
            for (int seg = 0; seg < Segments; seg++)
            {
                double theta = 2.0 * Math.PI * seg / Segments;
                var offset = new Vector3d(
                    radius * Math.Sin(phi) * Math.Cos(theta),
                    radius * Math.Cos(phi),
                    radius * Math.Sin(phi) * Math.Sin(theta));
                grid[ring, seg] = mesh.AppendVertex(center + offset);
            }
        }

        for (int ring = 0; ring < Rings; ring++)
        {
            for (int seg = 0; seg < Segments; seg++)
            {
                int next = (seg + 1) % Segments;
                int a = grid[ring, seg];
                int b = grid[ring, next];
                int c = grid[ring + 1, next];
                int d = grid[ring + 1, seg];

                // Wound so face normals point out of the sphere (verified: the opposite order
                // gives 768/768 inward-facing normals, which silently inverts every surface normal
                // the gizmo reads back).
                mesh.AppendTriangle(a, c, d);
                mesh.AppendTriangle(a, b, c);
            }
        }

        return mesh;
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void ClickingTheMesh_PlacesAHole(float radius, double scaling)
    {
        DMesh3 mesh = CreateSphere(radius);
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new DrainHoleGizmo(mesh);

        // Straight at the middle of the model, which is over the sphere from any angle.
        Assert.True(harness.PressAtPixel(gizmo, harness.CenterPixel, mesh: mesh),
            $"Clicking the mesh must place a drain hole (radius={radius}, scaling={scaling}).");
        Assert.Single(gizmo.Holes);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void ClickingEmptySpace_PlacesNothingAndLeavesTheClickForTheCamera(float radius, double scaling)
    {
        DMesh3 mesh = CreateSphere(radius);
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new DrainHoleGizmo(mesh);

        // The viewport corner: the camera frames the sphere with margin, so this misses it.
        Assert.False(harness.PressAtPixel(gizmo, new Vector2(2f, 2f), mesh: mesh),
            $"Clicking past the mesh must not place a hole (radius={radius}, scaling={scaling}).");
        Assert.Empty(gizmo.Holes);
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void ThePlacedHole_LandsUnderTheCursor(float radius, double scaling)
    {
        DMesh3 mesh = CreateSphere(radius);
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new DrainHoleGizmo(mesh);

        // A little off-centre, so a bug that always returns the model's centre would show up.
        Vector2 clicked = harness.CenterPixel + new Vector2(30f, -20f);
        Assert.True(harness.PressAtPixel(gizmo, clicked, mesh: mesh));

        Vector3d point = Assert.Single(gizmo.Holes).SurfacePoint;
        Vector2 landed = harness.RequireProjectToPixel(new Vector3((float)point.x, (float)point.y, (float)point.z));

        float error = (landed - clicked).Length();
        Assert.True(error < 1.5f,
            $"The hole landed {error}px from the cursor (radius={radius}, scaling={scaling}); clicked {clicked}, landed {landed}.");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void ThePlacedHole_SitsOnTheSurfaceWithAnOutwardNormal(float radius, double scaling)
    {
        DMesh3 mesh = CreateSphere(radius);
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new DrainHoleGizmo(mesh);

        Assert.True(harness.PressAtPixel(gizmo, harness.CenterPixel + new Vector2(25f, 15f), mesh: mesh));
        PlacedDrainHole hole = Assert.Single(gizmo.Holes);

        var center = new Vector3d(ModelCenter.X, ModelCenter.Y, ModelCenter.Z);
        Vector3d outward = hole.SurfacePoint - center;

        // On the sphere's surface. A faceted sphere's triangles sag inside the true surface by up
        // to the sagitta of a ring chord, so allow that much.
        double sagitta = radius * (1.0 - Math.Cos(Math.PI / Rings));
        Assert.True(radius - outward.Length < sagitta * 1.5 && outward.Length - radius < 1e-6 * radius,
            $"Hole is {outward.Length} from the centre, expected ~{radius} (radius={radius}, scaling={scaling}).");

        // And facing out of the mesh, not into it — a drain hole drilled inward is useless.
        Assert.True(hole.SurfaceNormal.Dot(outward.Normalized) > 0,
            $"Hole normal points inward (radius={radius}, scaling={scaling}).");
    }
}
