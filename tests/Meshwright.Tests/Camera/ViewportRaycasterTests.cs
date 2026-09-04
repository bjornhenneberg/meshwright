using System.Numerics;
using Meshwright.Rendering.Camera;
using Xunit;

namespace Meshwright.Tests.Camera;

/// <summary>
/// Unit tests for screen-to-world ray unprojection. These are deterministic geometric assertions,
/// independent of any gizmo or mesh picking; they verify that <see cref="ViewportRaycaster.Unproject"/>
/// correctly inverts the view/projection matrices.
/// </summary>
public class ViewportRaycasterTests
{
    private static readonly Vector2 ViewportSize = new(800, 600);

    [Fact]
    public void Unproject_CenterPixel_ProducesRayAlongCameraForward()
    {
        var camera = new OrbitCamera();
        Vector2 centerPixel = ViewportSize / 2f;

        ViewportRay ray = ViewportRaycaster.Unproject(centerPixel, ViewportSize, camera.GetViewMatrix(), camera.GetProjectionMatrix(ViewportSize.X / ViewportSize.Y));

        // Center pixel should produce a ray roughly along the camera's forward direction (toward target).
        Vector3 cameraForward = Vector3.Normalize(camera.Target - camera.Position);
        float dotProduct = Vector3.Dot(ray.Direction, cameraForward);
        Assert.True(dotProduct > 0.99f, $"Center pixel ray should be nearly parallel to camera forward; dot={dotProduct}");
    }

    [Fact]
    public void Unproject_RayDirection_IsNormalized()
    {
        var camera = new OrbitCamera();
        ViewportRay ray = ViewportRaycaster.Unproject(ViewportSize / 2f, ViewportSize, camera.GetViewMatrix(), camera.GetProjectionMatrix(1f));

        float length = ray.Direction.Length();
        Assert.True(MathF.Abs(length - 1f) < 1e-5f, $"Ray direction should be normalized; length={length}");
    }

    [Fact]
    public void Unproject_LeftRightPixels_ProducesRaysDivergingLeftRight()
    {
        var camera = new OrbitCamera();
        float aspect = ViewportSize.X / ViewportSize.Y;
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix(aspect);

        Vector2 leftPixel = new(10, ViewportSize.Y / 2f);
        Vector2 rightPixel = new(ViewportSize.X - 10, ViewportSize.Y / 2f);

        ViewportRay leftRay = ViewportRaycaster.Unproject(leftPixel, ViewportSize, view, proj);
        ViewportRay rightRay = ViewportRaycaster.Unproject(rightPixel, ViewportSize, view, proj);

        // Left and right rays should have different x-components.
        Assert.True(leftRay.Direction.X < rightRay.Direction.X, "Left pixel ray should have smaller x-component than right pixel ray");
    }

    [Fact]
    public void Unproject_TopBottomPixels_ProducesRaysDivergingUpDown()
    {
        var camera = new OrbitCamera();
        float aspect = ViewportSize.X / ViewportSize.Y;
        var view = camera.GetViewMatrix();
        var proj = camera.GetProjectionMatrix(aspect);

        Vector2 topPixel = new(ViewportSize.X / 2f, 10);
        Vector2 bottomPixel = new(ViewportSize.X / 2f, ViewportSize.Y - 10);

        ViewportRay topRay = ViewportRaycaster.Unproject(topPixel, ViewportSize, view, proj);
        ViewportRay bottomRay = ViewportRaycaster.Unproject(bottomPixel, ViewportSize, view, proj);

        // Top and bottom rays should have different y-components (note: y is up in world space, down in screen space).
        Assert.NotEqual(topRay.Direction.Y, bottomRay.Direction.Y);
    }

    [Fact]
    public void PointAt_ProducesPointAlongRay()
    {
        var camera = new OrbitCamera();
        ViewportRay ray = ViewportRaycaster.Unproject(ViewportSize / 2f, ViewportSize, camera.GetViewMatrix(), camera.GetProjectionMatrix(1f));

        Vector3 point = ray.PointAt(1f);

        Vector3 expectedPoint = ray.Origin + ray.Direction;
        Assert.Equal(expectedPoint, point);
    }

    [Fact]
    public void Unproject_InvalidViewportSize_Throws()
    {
        var camera = new OrbitCamera();
        var invalidSize = new Vector2(0, 600);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ViewportRaycaster.Unproject(Vector2.Zero, invalidSize, camera.GetViewMatrix(), camera.GetProjectionMatrix(1f))
        );
    }
}
