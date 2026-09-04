using System.Numerics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;

namespace Meshwright.Tests.Gizmos;

/// <summary>
/// Drives gizmos the way the running application does: through a real <see cref="OrbitCamera"/>,
/// a real viewport size, a real <c>RenderScaling</c>, and the production
/// <see cref="ViewportRaycaster.Unproject"/> — instead of hand-building a <see cref="ViewportRay"/>
/// at a convenient origin.
///
/// <para>
/// This exists because every interaction defect found in this codebase so far escaped a green test
/// suite for the same reason: the gizmo tests synthesise rays like
/// <c>new ViewportRay(new Vector3(0, 0, 3), -Vector3.UnitZ)</c>, which silently fixes the two
/// variables the bugs actually depended on — how far the camera is from the gizmo, and the display
/// scaling applied to pointer coordinates. A camera framed on a 50 mm model sits ~163 units away,
/// not 3, so a test at distance 3 can pass while every real click fails.
/// </para>
///
/// <para>
/// Tests should therefore express intent in the units the user acts in — "click the pixel where
/// the gizmo's centre appears", "click 40 pixels to the left" — and let this harness produce the
/// <see cref="GizmoPointerEvent"/>. <see cref="ProjectToPixel"/> is the inverse of the unprojection
/// under test, so a test can aim at a world point without knowing the matrices.
/// </para>
/// </summary>
public sealed class ViewportHarness
{
    /// <summary>Logical (device-independent) viewport size, as Avalonia reports <c>Bounds.Size</c>.</summary>
    public Vector2 LogicalSize { get; }

    /// <summary>Display scale factor, as Avalonia reports <c>VisualRoot.RenderScaling</c>.</summary>
    public double RenderScaling { get; }

    public OrbitCamera Camera { get; }

    public ViewportHarness(OrbitCamera camera, Vector2? logicalSize = null, double renderScaling = 1.0)
    {
        Camera = camera;
        LogicalSize = logicalSize ?? new Vector2(800, 600);
        RenderScaling = renderScaling;
    }

    /// <summary>A harness with the camera framed on a bounding sphere, exactly as loading a mesh does.</summary>
    public static ViewportHarness Framed(Vector3 center, float radius, Vector2? logicalSize = null, double renderScaling = 1.0)
    {
        var camera = new OrbitCamera();
        camera.Frame(center, radius);
        return new ViewportHarness(camera, logicalSize, renderScaling);
    }

    /// <summary>Viewport size in device pixels — mirrors the pixel-size arithmetic in <c>MeshViewportControl.MakeGizmoEvent</c>.</summary>
    public Vector2 PixelSize
    {
        get
        {
            int w = Math.Max(1, (int)(LogicalSize.X * RenderScaling));
            int h = Math.Max(1, (int)(LogicalSize.Y * RenderScaling));
            return new Vector2(w, h);
        }
    }

    public float Aspect => PixelSize.Y == 0 ? 1f : PixelSize.X / PixelSize.Y;

    public Matrix4x4 View => Camera.GetViewMatrix();

    public Matrix4x4 Projection => Camera.GetProjectionMatrix(Aspect);

    /// <summary>Centre of the viewport, in logical pixels.</summary>
    public Vector2 CenterPixel => LogicalSize / 2f;

    /// <summary>
    /// Unprojects a logical pointer position into a world ray. This mirrors
    /// <c>MeshViewportControl.MakeGizmoEvent</c>: scale the logical position by
    /// <see cref="RenderScaling"/>, then hand it to the production
    /// <see cref="ViewportRaycaster.Unproject"/>. Only the scaling arithmetic is restated here;
    /// the unprojection itself is the shipping code, so a regression in it fails these tests.
    /// </summary>
    public ViewportRay RayThroughPixel(Vector2 logicalPixel)
    {
        var devicePixel = new Vector2((float)(logicalPixel.X * RenderScaling), (float)(logicalPixel.Y * RenderScaling));
        return ViewportRaycaster.Unproject(devicePixel, PixelSize, View, Projection);
    }

    /// <summary>
    /// Projects a world point to the logical pixel it appears at — the inverse of
    /// <see cref="RayThroughPixel"/>. Lets a test aim at geometry ("the gizmo's centre") rather
    /// than at a magic screen coordinate. Returns null when the point is at or behind the camera's
    /// eye, where it has no on-screen position.
    /// </summary>
    public Vector2? ProjectToPixel(Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), View * Projection);
        if (clip.W <= 1e-6f)
        {
            return null;
        }

        float ndcX = clip.X / clip.W;
        float ndcY = clip.Y / clip.W;

        float deviceX = (ndcX + 1f) * 0.5f * PixelSize.X;
        float deviceY = (1f - ndcY) * 0.5f * PixelSize.Y;

        return new Vector2((float)(deviceX / RenderScaling), (float)(deviceY / RenderScaling));
    }

    /// <summary>Projects a world point to a pixel, failing the calling test if it is off-screen.</summary>
    public Vector2 RequireProjectToPixel(Vector3 world) =>
        ProjectToPixel(world) ?? throw new InvalidOperationException($"World point {world} does not project to a visible pixel.");

    public GizmoPointerEvent EventAt(
        Vector2 logicalPixel,
        GizmoPointerButton button = GizmoPointerButton.Primary,
        GizmoModifierKeys modifiers = GizmoModifierKeys.None,
        g3.DMesh3? mesh = null)
    {
        var devicePixel = new Vector2((float)(logicalPixel.X * RenderScaling), (float)(logicalPixel.Y * RenderScaling));
        return new GizmoPointerEvent(RayThroughPixel(logicalPixel), devicePixel, PixelSize, View, Projection, button, modifiers, mesh);
    }

    /// <summary>Presses at the pixel where <paramref name="world"/> appears; returns whether the gizmo claimed it.</summary>
    public bool PressAtWorld(
        IViewportGizmo gizmo,
        Vector3 world,
        GizmoModifierKeys modifiers = GizmoModifierKeys.None,
        g3.DMesh3? mesh = null) =>
        gizmo.OnPointerPressed(EventAt(RequireProjectToPixel(world), GizmoPointerButton.Primary, modifiers, mesh));

    /// <summary>Presses at a logical pixel; returns whether the gizmo claimed it.</summary>
    public bool PressAtPixel(
        IViewportGizmo gizmo,
        Vector2 logicalPixel,
        GizmoModifierKeys modifiers = GizmoModifierKeys.None,
        g3.DMesh3? mesh = null) =>
        gizmo.OnPointerPressed(EventAt(logicalPixel, GizmoPointerButton.Primary, modifiers, mesh));

    public bool MoveToPixel(
        IViewportGizmo gizmo,
        Vector2 logicalPixel,
        GizmoModifierKeys modifiers = GizmoModifierKeys.None,
        g3.DMesh3? mesh = null) =>
        gizmo.OnPointerMoved(EventAt(logicalPixel, GizmoPointerButton.None, modifiers, mesh));

    public bool ReleaseAtPixel(
        IViewportGizmo gizmo,
        Vector2 logicalPixel,
        GizmoModifierKeys modifiers = GizmoModifierKeys.None,
        g3.DMesh3? mesh = null) =>
        gizmo.OnPointerReleased(EventAt(logicalPixel, GizmoPointerButton.None, modifiers, mesh));

    /// <summary>
    /// The largest offset, in logical pixels, at which a press still lands on the gizmo — walking
    /// outward from the pixel where <paramref name="worldCenter"/> appears, in the given direction.
    /// This is the gizmo's on-screen grab radius, the quantity that decides whether a user can
    /// actually hit it; <paramref name="limit"/> caps the search.
    /// </summary>
    public int GrabRadiusPixels(
        Func<IViewportGizmo> freshGizmo,
        Vector3 worldCenter,
        Vector2 direction,
        int limit = 400,
        GizmoModifierKeys modifiers = GizmoModifierKeys.None,
        g3.DMesh3? mesh = null)
    {
        Vector2 center = RequireProjectToPixel(worldCenter);
        Vector2 step = Vector2.Normalize(direction);

        int reached = -1;
        for (int i = 0; i <= limit; i++)
        {
            if (!PressAtPixel(freshGizmo(), center + step * i, modifiers, mesh))
            {
                break;
            }

            reached = i;
        }

        return reached;
    }
}
