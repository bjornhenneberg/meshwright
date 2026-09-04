using System.Numerics;
using Meshwright.Rendering.Camera;
using Xunit;

namespace Meshwright.Tests.Gizmos;

/// <summary>
/// Self-tests for <see cref="ViewportHarness"/>. The harness is the instrument every other
/// interaction test reads its results from, so its own projection/unprojection round-trip is
/// pinned first — otherwise a harness bug would be indistinguishable from a gizmo bug.
/// </summary>
public class ViewportHarnessTests
{
    public static TheoryData<float, double> Scenarios()
    {
        var data = new TheoryData<float, double>();
        foreach (float radius in new[] { 0.5f, 5f, 50f, 500f })
        {
            foreach (double scaling in new[] { 1.0, 1.5, 2.0 })
            {
                data.Add(radius, scaling);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void ProjectToPixel_RoundTripsThroughRayThroughPixel(float radius, double scaling)
    {
        var center = new Vector3(3f, -2f, 1f);
        var harness = ViewportHarness.Framed(center, radius, renderScaling: scaling);

        // Points spread around the model, all comfortably on-screen.
        foreach (Vector3 offset in new[]
                 {
                     Vector3.Zero,
                     new Vector3(radius * 0.3f, 0, 0),
                     new Vector3(0, radius * 0.3f, 0),
                     new Vector3(-radius * 0.2f, radius * 0.1f, radius * 0.2f),
                 })
        {
            Vector3 world = center + offset;
            Vector2 pixel = harness.RequireProjectToPixel(world);
            var ray = harness.RayThroughPixel(pixel);

            // The ray through that pixel must pass through the world point it came from.
            Vector3 toPoint = world - ray.Origin;
            float along = Vector3.Dot(toPoint, ray.Direction);
            float perpendicular = (toPoint - ray.Direction * along).Length();

            Assert.True(along > 0f, $"Point should be in front of the ray origin; along={along}");
            Assert.True(perpendicular < radius * 1e-3f,
                $"Ray through the projected pixel missed its own world point by {perpendicular} (radius={radius}, scaling={scaling})");
        }
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void CameraTarget_ProjectsToTheCentreOfTheViewport(float radius, double scaling)
    {
        var center = new Vector3(-7f, 4f, 0.5f);
        var harness = ViewportHarness.Framed(center, radius, renderScaling: scaling);

        Vector2 pixel = harness.RequireProjectToPixel(center);

        Assert.Equal(harness.CenterPixel.X, pixel.X, 1);
        Assert.Equal(harness.CenterPixel.Y, pixel.Y, 1);
    }

    [Fact]
    public void RenderScaling_DoesNotChangeWhereAWorldPointAppearsInLogicalPixels()
    {
        var center = Vector3.Zero;
        var world = new Vector3(10f, 5f, 0f);

        // A logical pointer coordinate is scale-independent: the same world point must sit under
        // the same logical pixel on a 1x and a 2x display. (This is the invariant that a mismatch
        // between pointer coordinates and viewport pixel size would break.)
        Vector2 at1x = ViewportHarness.Framed(center, 50f, renderScaling: 1.0).RequireProjectToPixel(world);
        Vector2 at2x = ViewportHarness.Framed(center, 50f, renderScaling: 2.0).RequireProjectToPixel(world);

        Assert.Equal(at1x.X, at2x.X, 1);
        Assert.Equal(at1x.Y, at2x.Y, 1);
    }
}
