using System;
using System.Numerics;

namespace Meshwright.Rendering.Camera;

/// <summary>
/// Unprojects a viewport pixel into a world-space <see cref="ViewportRay"/>, given the camera's
/// view/projection matrices and the viewport's pixel size. Pure math, independent of
/// <see cref="OrbitCamera"/> specifically, so it works with any view/projection pair (including
/// the ones handed to <c>MeshRenderer.Render</c> and <c>Gizmos.IViewportGizmo.Render</c>).
/// </summary>
public static class ViewportRaycaster
{
    /// <summary>
    /// Computes the world-space ray passing through <paramref name="pixelPosition"/> (in device
    /// pixels, origin top-left, y-down — matching Avalonia's pointer coordinates after scaling by
    /// <c>RenderScaling</c>) for a viewport of size <paramref name="viewportPixelSize"/>.
    /// </summary>
    /// <param name="pixelPosition">Pixel coordinates, origin at the top-left, y increasing downward.</param>
    /// <param name="viewportPixelSize">Viewport size in the same pixel units as <paramref name="pixelPosition"/>.</param>
    /// <param name="view">Camera view matrix (e.g. <see cref="OrbitCamera.GetViewMatrix"/>).</param>
    /// <param name="projection">Camera projection matrix (e.g. <see cref="OrbitCamera.GetProjectionMatrix"/>).</param>
    public static ViewportRay Unproject(Vector2 pixelPosition, Vector2 viewportPixelSize, Matrix4x4 view, Matrix4x4 projection)
    {
        if (viewportPixelSize.X <= 0f || viewportPixelSize.Y <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(viewportPixelSize), "Viewport pixel size must be positive.");
        }

        // Device pixels (y-down) -> normalized device coordinates (y-up), both axes in [-1, 1].
        float ndcX = (2f * pixelPosition.X / viewportPixelSize.X) - 1f;
        float ndcY = 1f - (2f * pixelPosition.Y / viewportPixelSize.Y);

        // Matches the convention already used by MeshRenderer/OrbitCamera: a world-space point is
        // carried to clip space via Vector4.Transform(worldPoint, view) then transformed again by
        // projection, i.e. clip = world * view * projection under System.Numerics' row-vector
        // convention. Combine once so both the near- and far-plane unprojections reuse one inverse.
        Matrix4x4 viewProjection = view * projection;
        if (!Matrix4x4.Invert(viewProjection, out Matrix4x4 inverseViewProjection))
        {
            throw new InvalidOperationException("Combined view/projection matrix is not invertible.");
        }

        Vector3 nearPoint = UnprojectNdc(new Vector3(ndcX, ndcY, 0f), inverseViewProjection);
        Vector3 farPoint = UnprojectNdc(new Vector3(ndcX, ndcY, 1f), inverseViewProjection);

        Vector3 direction = Vector3.Normalize(farPoint - nearPoint);
        return new ViewportRay(nearPoint, direction);
    }

    private static Vector3 UnprojectNdc(Vector3 ndc, Matrix4x4 inverseViewProjection)
    {
        var clip = new Vector4(ndc, 1f);
        Vector4 world = Vector4.Transform(clip, inverseViewProjection);

        // world.W can't legitimately be ~0 for a real camera ray (it would mean the near/far plane
        // point is at infinity), but guard against division blow-up on degenerate matrices anyway.
        float w = MathF.Abs(world.W) < 1e-8f ? 1e-8f : world.W;
        return new Vector3(world.X / w, world.Y / w, world.Z / w);
    }
}
