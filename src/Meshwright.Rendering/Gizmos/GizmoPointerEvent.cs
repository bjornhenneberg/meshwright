using System.Numerics;
using Meshwright.Rendering.Camera;

namespace Meshwright.Rendering.Gizmos;

/// <summary>
/// Everything an <see cref="IViewportGizmo"/> needs to react to one pointer event, pre-computed by
/// <see cref="Meshwright.App.Views.MeshViewportControl"/> so implementers never touch Avalonia
/// input types or camera matrices directly.
/// </summary>
/// <param name="Ray">
/// The pointer position unprojected into a world-space ray through the current camera — the
/// primary input for drag math (see <c>Meshwright.Rendering.Gizmos.GizmoMath</c> for shared
/// ray/point/line/plane helpers).
/// </param>
/// <param name="PixelPosition">Raw pointer position in device pixels (origin top-left, y-down), for gizmos that need 2D screen-space hit-testing.</param>
/// <param name="ViewportPixelSize">Current viewport size in device pixels, matching <paramref name="PixelPosition"/>'s units.</param>
/// <param name="Button">Which button this event relates to; <see cref="GizmoPointerButton.None"/> for move events.</param>
/// <param name="Modifiers">Keyboard modifiers held during the event.</param>
/// <param name="Mesh">
/// The viewport's current mesh, or null if none is loaded. Included so a gizmo that needs to pick
/// a point on the mesh surface itself (e.g. a future drain-hole-placement gizmo) can call
/// <c>Meshwright.Geometry.Spatial.MeshRaycaster.Raycast(Mesh, Ray.ToRay3d())</c> directly, without
/// <c>MeshViewportControl</c> needing to know about mesh-picking at all.
/// </param>
public readonly record struct GizmoPointerEvent(
    ViewportRay Ray,
    Vector2 PixelPosition,
    Vector2 ViewportPixelSize,
    GizmoPointerButton Button,
    GizmoModifierKeys Modifiers,
    g3.DMesh3? Mesh);
