using System.Numerics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;
using Xunit;

namespace Meshwright.Tests.Gizmos;

/// <summary>
/// Tests for <see cref="GizmoScale"/>, the shared world-size/screen-size conversion the render and
/// pick paths both depend on. Verified against the harness's own projection so the claim being
/// checked is the one that matters: a size from <see cref="GizmoScale.ForFractionOfHeight"/> really
/// does span that fraction of the viewport on screen.
/// </summary>
public class GizmoScaleTests
{
    [Theory]
    [InlineData(0.5f)]
    [InlineData(5f)]
    [InlineData(50f)]
    [InlineData(500f)]
    public void ForFractionOfHeight_SpansThatFractionOfTheViewportOnScreen(float radius)
    {
        var center = new Vector3(1f, 2f, -3f);
        var harness = ViewportHarness.Framed(center, radius);

        const float fraction = 0.2f;
        float worldSize = GizmoScale.ForFractionOfHeight(center, harness.View, harness.Projection, fraction);

        // Offset by that world size along the camera's own up axis, then measure the on-screen gap.
        Vector3 up = Vector3.Normalize(Vector3.Cross(
            Vector3.Normalize(Vector3.Cross(center - harness.Camera.Position, Vector3.UnitY)),
            center - harness.Camera.Position));

        Vector2 at = harness.RequireProjectToPixel(center);
        Vector2 offset = harness.RequireProjectToPixel(center + up * worldSize);

        float pixelGap = MathF.Abs(offset.Y - at.Y);
        float expected = harness.LogicalSize.Y * fraction;

        Assert.True(MathF.Abs(pixelGap - expected) < expected * 0.02f,
            $"A {fraction:P0}-of-height size spanned {pixelGap}px, expected ~{expected}px (radius={radius}).");
    }

    [Fact]
    public void ForFractionOfHeight_GivesTheSameScreenSizeAtEveryModelScale()
    {
        var center = Vector3.Zero;
        const float fraction = 0.15f;

        float? firstGap = null;
        foreach (float radius in new[] { 0.5f, 5f, 50f, 500f })
        {
            var harness = ViewportHarness.Framed(center, radius);
            float worldSize = GizmoScale.ForFractionOfHeight(center, harness.View, harness.Projection, fraction);

            // The whole point: world size grows with the model, screen size does not.
            Assert.True(worldSize > 0f);

            Vector2 at = harness.RequireProjectToPixel(center);
            Vector2 offset = harness.RequireProjectToPixel(center + Vector3.UnitY * worldSize);
            float gap = MathF.Abs(offset.Y - at.Y);

            firstGap ??= gap;
            Assert.True(MathF.Abs(gap - firstGap.Value) < 1f,
                $"Screen size drifted with model scale: {gap}px at radius {radius} vs {firstGap}px at the first scale.");
        }
    }

    [Fact]
    public void WorldPerViewportHeight_GrowsWithCameraDistance()
    {
        var center = Vector3.Zero;
        var camera = new OrbitCamera();
        camera.Frame(center, 10f);
        var harness = new ViewportHarness(camera);

        float near = GizmoScale.WorldPerViewportHeight(center, harness.View, harness.Projection);

        camera.Zoom(camera.Distance);
        float far = GizmoScale.WorldPerViewportHeight(center, harness.View, harness.Projection);

        Assert.True(far > near * 1.5f, $"Zooming out should widen the world span per screen height; near={near}, far={far}");
    }
}
