using System;
using System.Numerics;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.Geometry.Spatial;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;
using Meshwright.Tests.Gizmos;
using Xunit;

namespace Meshwright.Tests.Camera;

/// <summary>
/// Switching to orthographic replaces the projection matrix that <see cref="ViewportRaycaster"/>
/// and every gizmo's pick path resolve against. Picking that works in perspective and silently
/// misses in orthographic is the exact shape of defect this project keeps shipping, so the pick
/// contract is re-run here in the new mode rather than assumed to carry over.
/// </summary>
public class OrthographicPickingTests
{
    private static readonly Vector3 ModelCenter = new(2f, -1f, 3f);

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

    private static ViewportHarness FramedOrthographic(float radius, double scaling = 1.0)
    {
        ViewportHarness harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        harness.Camera.ProjectionMode = ProjectionMode.Orthographic;
        return harness;
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Orthographic_RayThroughAPixel_PassesThroughTheWorldPointThatProjectsThere(float radius, double scaling)
    {
        // The round trip that picking depends on: project a world point to the pixel it appears
        // at, then unproject that pixel and require the ray to pass through the point. Checked at
        // several depths, because it is depth that an orthographic unprojection gets wrong when it
        // is written for a perspective frustum.
        ViewportHarness harness = FramedOrthographic(radius, scaling);
        Vector3 forward = Vector3.Normalize(harness.Camera.Target - harness.Camera.Position);

        foreach (float depthOffset in new[] { -0.6f, 0f, 0.6f })
        {
            foreach (Vector3 lateral in new[] { Vector3.Zero, new Vector3(0.4f, 0.3f, -0.2f) })
            {
                Vector3 world = ModelCenter + forward * (depthOffset * radius) + lateral * radius;
                Vector2 pixel = harness.RequireProjectToPixel(world);
                ViewportRay ray = harness.RayThroughPixel(pixel);

                float along = Vector3.Dot(world - ray.Origin, ray.Direction);
                float missDistance = Vector3.Distance(ray.PointAt(along), world);

                // Measured in pixels, for the reason given in ViewportHarnessTests: picking cares
                // how far off the click lands on screen, and a bound tied to the model radius is
                // arbitrarily tighter on small models - which is how the perspective version of
                // this assertion came to fail on macOS float rounding alone.
                float worldPerPixel = GizmoScale.WorldPerViewportHeight(world, harness.View, harness.Projection) / harness.PixelSize.Y;
                Assert.True(missDistance < worldPerPixel * 0.5f,
                    $"Orthographic ray through pixel {pixel} missed {world} by {missDistance / worldPerPixel} px "
                    + $"(radius={radius}, scaling={scaling}).");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void CentrePixel_HitsTheSameTriangleInBothProjectionModes(float radius, double scaling)
    {
        // At the centre of the viewport the perspective and orthographic rays coincide (both run
        // from the eye through the target), so they must agree about what is under the crosshair.
        // Away from the centre they legitimately differ - parallel rays are not the same rays -
        // which is why the round-trip test above carries the off-centre cases.
        DMesh3 mesh = CreateSphere(radius);

        var perspective = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        ViewportHarness orthographic = FramedOrthographic(radius, scaling);

        // Both cameras orbit identically off the default pose first. Straight down the default
        // axis the centre ray lands on the sphere's UV grid seam, where which of two triangles
        // sharing that edge is reported is decided by float noise rather than by the projection -
        // a knife edge that would make this test flap for a reason it is not about.
        perspective.Camera.Orbit(0.13f, 0.07f);
        orthographic.Camera.Orbit(0.13f, 0.07f);

        MeshRayHit? perspectiveHit = MeshRaycaster.Raycast(mesh, perspective.RayThroughPixel(perspective.CenterPixel).ToRay3d());
        MeshRayHit? orthographicHit = MeshRaycaster.Raycast(mesh, orthographic.RayThroughPixel(orthographic.CenterPixel).ToRay3d());

        Assert.NotNull(perspectiveHit);
        Assert.NotNull(orthographicHit);
        Assert.Equal(perspectiveHit!.Value.TriangleId, orthographicHit!.Value.TriangleId);
        Assert.True(perspectiveHit.Value.Point.Distance(orthographicHit!.Value.Point) < radius * 1e-3,
            "The two modes' centre rays must strike the same point, not merely the same triangle.");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Orthographic_ClickingAGizmoCentre_ClaimsTheDrag(float radius, double scaling)
    {
        ViewportHarness harness = FramedOrthographic(radius, scaling);
        var gizmo = new PlaneCutGizmo(ModelCenter);

        Assert.True(harness.PressAtWorld(gizmo, ModelCenter),
            $"Clicking the gizmo's centre must claim the drag in orthographic too (radius={radius}, scaling={scaling}).");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Orthographic_ClickingFarFromAGizmo_LeavesTheClickForTheCamera(float radius, double scaling)
    {
        ViewportHarness harness = FramedOrthographic(radius, scaling);
        var gizmo = new PlaneCutGizmo(ModelCenter);

        Assert.False(harness.PressAtPixel(gizmo, new Vector2(4f, 4f)),
            $"A click in the corner must still orbit the camera in orthographic (radius={radius}, scaling={scaling}).");
    }

    /// <summary>A UV sphere centred on <see cref="ModelCenter"/>: the centre pixel is over the
    /// surface from any camera angle, in either projection.</summary>
    private static DMesh3 CreateSphere(float radius)
    {
        const int rings = 16;
        const int segments = 24;

        var mesh = new DMesh3();
        var center = new Vector3d(ModelCenter.X, ModelCenter.Y, ModelCenter.Z);
        var grid = new int[rings + 1, segments];

        for (int ring = 0; ring <= rings; ring++)
        {
            double phi = Math.PI * ring / rings;
            for (int seg = 0; seg < segments; seg++)
            {
                double theta = 2.0 * Math.PI * seg / segments;
                var offset = new Vector3d(
                    radius * Math.Sin(phi) * Math.Cos(theta),
                    radius * Math.Sin(phi) * Math.Sin(theta),
                    radius * Math.Cos(phi));
                grid[ring, seg] = mesh.AppendVertex(center + offset);
            }
        }

        for (int ring = 0; ring < rings; ring++)
        {
            for (int seg = 0; seg < segments; seg++)
            {
                int next = (seg + 1) % segments;
                mesh.AppendTriangle(grid[ring, seg], grid[ring + 1, seg], grid[ring + 1, next]);
                mesh.AppendTriangle(grid[ring, seg], grid[ring + 1, next], grid[ring, next]);
            }
        }

        return mesh;
    }
}
