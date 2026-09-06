using System;
using System.Numerics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;
using Xunit;

namespace Meshwright.Tests.Camera;

/// <summary>
/// What "orthographic" has to mean, measured rather than asserted as a flag.
///
/// <para>
/// The defining property is the absence of the perspective divide: two objects of equal size at
/// different depths must project to the same screen size. A test that only checked
/// <c>ProjectionMode == Orthographic</c> would pass on a camera that still built a perspective
/// matrix, which is precisely the shape of defect §11 keeps recording.
/// </para>
/// </summary>
public class ProjectionModeTests
{
    private const float Aspect = 16f / 9f;

    private static OrbitCamera FramedCamera(ProjectionMode mode)
    {
        var camera = new OrbitCamera();
        camera.Frame(new Vector3(2f, -1f, 3f), 10f);
        camera.ProjectionMode = mode;
        return camera;
    }

    /// <summary>Projects a world point to normalized device coordinates, w-divide included.</summary>
    private static Vector2 ToNdc(OrbitCamera camera, Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), camera.GetViewMatrix() * camera.GetProjectionMatrix(Aspect));
        return new Vector2(clip.X / clip.W, clip.Y / clip.W);
    }

    /// <summary>Unit vector pointing right on screen, in world space.</summary>
    private static Vector3 ScreenRight(OrbitCamera camera)
    {
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Matrix4x4 view = camera.GetViewMatrix();

        // Row-vector convention: the view matrix's first column is the camera's right axis.
        var right = new Vector3(view.M11, view.M21, view.M31);
        Assert.True(MathF.Abs(Vector3.Dot(right, forward)) < 1e-4f, "Screen-right must be perpendicular to the view direction.");
        return Vector3.Normalize(right);
    }

    /// <summary>
    /// On-screen length of a segment of world length <paramref name="worldLength"/>, centred at
    /// <paramref name="center"/> and laid perpendicular to the view so depth is the only variable.
    /// </summary>
    private static float ScreenLength(OrbitCamera camera, Vector3 center, float worldLength)
    {
        Vector3 right = ScreenRight(camera);
        Vector2 a = ToNdc(camera, center - right * (worldLength / 2f));
        Vector2 b = ToNdc(camera, center + right * (worldLength / 2f));
        return Vector2.Distance(a, b);
    }

    [Fact]
    public void Orthographic_EqualSizedObjectsAtDifferentDepths_ProjectToEqualScreenSize()
    {
        var camera = FramedCamera(ProjectionMode.Orthographic);
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);

        // Two identical 1 mm segments, one a third of the camera distance nearer than the target
        // and one the same amount further away.
        float offset = camera.Distance / 3f;
        float near = ScreenLength(camera, camera.Target - forward * offset, 1f);
        float far = ScreenLength(camera, camera.Target + forward * offset, 1f);

        Assert.True(near > 0f, "The near segment must have a measurable on-screen size.");
        Assert.Equal(near, far, 5);
    }

    [Fact]
    public void Perspective_EqualSizedObjectsAtDifferentDepths_ProjectToDifferentScreenSizes()
    {
        // The control for the test above: without it, a camera that had quietly lost its
        // perspective path would pass the orthographic assertion for the wrong reason.
        var camera = FramedCamera(ProjectionMode.Perspective);
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);

        float offset = camera.Distance / 3f;
        float near = ScreenLength(camera, camera.Target - forward * offset, 1f);
        float far = ScreenLength(camera, camera.Target + forward * offset, 1f);

        Assert.True(near > far * 1.5f, $"A nearer object must project larger in perspective (near={near}, far={far}).");
    }

    [Fact]
    public void Orthographic_ProjectionMatrix_HasNoPerspectiveDivideTerm()
    {
        Matrix4x4 orthographic = FramedCamera(ProjectionMode.Orthographic).GetProjectionMatrix(Aspect);
        Matrix4x4 perspective = FramedCamera(ProjectionMode.Perspective).GetProjectionMatrix(Aspect);

        Assert.Equal(0f, orthographic.M34);
        Assert.Equal(-1f, perspective.M34);
        Assert.True(Matrix4x4.Invert(orthographic, out _));
    }

    [Fact]
    public void SwitchingToOrthographic_KeepsGeometryAtTheTargetTheSameSizeOnScreen()
    {
        // Switching projection is a display choice, not a reframing: whatever the user has centred
        // must not jump in size or position when the mode changes.
        var perspective = FramedCamera(ProjectionMode.Perspective);
        var orthographic = FramedCamera(ProjectionMode.Orthographic);

        float sizePerspective = ScreenLength(perspective, perspective.Target, 1f);
        float sizeOrthographic = ScreenLength(orthographic, orthographic.Target, 1f);

        Assert.Equal(sizePerspective, sizeOrthographic, 4);
        Assert.Equal(Vector2.Zero.X, ToNdc(orthographic, orthographic.Target).X, 4);
        Assert.Equal(Vector2.Zero.Y, ToNdc(orthographic, orthographic.Target).Y, 4);
    }

    [Fact]
    public void Orthographic_ZoomingIn_StillMagnifies()
    {
        // An orthographic camera has no eye distance to shrink things with, so if the frustum
        // height did not track Distance the mouse wheel would do nothing at all.
        var camera = FramedCamera(ProjectionMode.Orthographic);
        float before = ScreenLength(camera, camera.Target, 1f);

        camera.Zoom(-camera.Distance / 2f);
        float after = ScreenLength(camera, camera.Target, 1f);

        Assert.True(after > before * 1.5f, $"Zooming in must magnify in orthographic too (before={before}, after={after}).");
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(50f)]
    [InlineData(500f)]
    public void Orthographic_WholeFramedModelStaysInsideTheViewVolume(float radius)
    {
        // The framing margin has to survive the mode switch, and the orthographic near plane sits
        // behind the eye precisely so that no part of the model is clipped away.
        var camera = new OrbitCamera();
        var center = new Vector3(1f, 2f, -3f);
        camera.Frame(center, radius);
        camera.ProjectionMode = ProjectionMode.Orthographic;

        Matrix4x4 viewProjection = camera.GetViewMatrix() * camera.GetProjectionMatrix(Aspect);

        foreach (Vector3 corner in BoundingBoxCorners(center, radius / MathF.Sqrt(3f)))
        {
            Vector4 clip = Vector4.Transform(new Vector4(corner, 1f), viewProjection);
            Assert.True(MathF.Abs(clip.X / clip.W) <= 1f, $"Corner {corner} is clipped horizontally.");
            Assert.True(MathF.Abs(clip.Y / clip.W) <= 1f, $"Corner {corner} is clipped vertically.");
            Assert.InRange(clip.Z / clip.W, 0f, 1f);
        }
    }

    private static Vector3[] BoundingBoxCorners(Vector3 center, float halfExtent)
    {
        var corners = new Vector3[8];
        int i = 0;
        foreach (float x in new[] { -halfExtent, halfExtent })
        {
            foreach (float y in new[] { -halfExtent, halfExtent })
            {
                foreach (float z in new[] { -halfExtent, halfExtent })
                {
                    corners[i++] = center + new Vector3(x, y, z);
                }
            }
        }

        return corners;
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(5f)]
    [InlineData(500f)]
    public void GizmoScale_InOrthographic_IsIndependentOfDepthAndMatchesTheViewHeight(float radius)
    {
        // GizmoScale.ForFractionOfHeight resolves against the projection matrix, so switching to
        // orthographic changes what every gizmo's draw and pick paths measure against. In
        // orthographic the frustum height does not vary with depth, and a tenth of the viewport
        // must still be a tenth of the viewport.
        var camera = FramedCamera(ProjectionMode.Orthographic);
        camera.Frame(Vector3.Zero, radius);
        camera.ProjectionMode = ProjectionMode.Orthographic;

        Matrix4x4 view = camera.GetViewMatrix();
        Matrix4x4 projection = camera.GetProjectionMatrix(Aspect);
        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);

        float atTarget = GizmoScale.ForFractionOfHeight(camera.Target, view, projection, 0.1f);
        float nearer = GizmoScale.ForFractionOfHeight(camera.Target - forward * radius, view, projection, 0.1f);
        float further = GizmoScale.ForFractionOfHeight(camera.Target + forward * radius, view, projection, 0.1f);

        Assert.Equal(atTarget, nearer, 4);
        Assert.Equal(atTarget, further, 4);
        Assert.Equal(camera.OrthographicHeight * 0.1f, atTarget, 4);
    }
}
