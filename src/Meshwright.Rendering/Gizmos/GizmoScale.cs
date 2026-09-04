using System.Numerics;

namespace Meshwright.Rendering.Gizmos;

/// <summary>
/// Converts between world size and on-screen size for gizmo geometry.
///
/// <para>
/// A gizmo sized in fixed world units is only usable at one zoom level: it fills the viewport on a
/// 1 mm part and collapses to a single pixel on a 500 mm one. Sizing it as a fraction of viewport
/// height instead makes it occupy the same screen area at every camera distance and on every model.
/// </para>
///
/// <para>
/// Both the render path and the pick path must agree on that size, or the gizmo will draw in one
/// place and respond in another — so both call through here rather than each deriving it. Working
/// in fractions of viewport <em>height</em> (not pixels) means this needs only the projection
/// matrix, which <c>IViewportGizmo.Render</c> and <see cref="GizmoPointerEvent"/> both already
/// carry; a fraction of height is a constant pixel count for any given viewport anyway.
/// </para>
/// </summary>
public static class GizmoScale
{
    /// <summary>
    /// World units spanned by the full height of the viewport at <paramref name="worldPoint"/>'s
    /// depth. Multiply by a fraction to get a world size that occupies that fraction of the screen.
    /// </summary>
    public static float WorldPerViewportHeight(Vector3 worldPoint, Matrix4x4 view, Matrix4x4 projection)
    {
        // M22 is the vertical scale term: 1/tan(fov/2) for perspective, 2/height for orthographic.
        float verticalScale = projection.M22;
        if (MathF.Abs(verticalScale) < 1e-9f)
        {
            return 1f;
        }

        // An orthographic projection's frustum height does not vary with depth. System.Numerics
        // marks perspective matrices with M34 = -1 (the row-vector w-divide term); orthographic
        // leaves it 0.
        bool isPerspective = MathF.Abs(projection.M34) > 1e-6f;
        if (!isPerspective)
        {
            return 2f / MathF.Abs(verticalScale);
        }

        // Depth along the camera's forward axis. CreateLookAt puts the camera at the origin looking
        // down -Z, so a point in front of the camera has negative view-space z.
        Vector3 viewSpace = Vector3.Transform(worldPoint, view);
        float depth = MathF.Abs(viewSpace.Z);

        return 2f * depth / MathF.Abs(verticalScale);
    }

    /// <summary>
    /// The world size that appears as <paramref name="fractionOfHeight"/> of the viewport's height
    /// at <paramref name="worldPoint"/> — the size a gizmo part should be drawn and picked at.
    /// </summary>
    public static float ForFractionOfHeight(Vector3 worldPoint, Matrix4x4 view, Matrix4x4 projection, float fractionOfHeight) =>
        WorldPerViewportHeight(worldPoint, view, projection) * fractionOfHeight;
}
