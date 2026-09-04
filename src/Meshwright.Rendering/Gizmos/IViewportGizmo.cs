using System.Numerics;
using GlApi = Silk.NET.OpenGL.GL;

namespace Meshwright.Rendering.Gizmos;

/// <summary>
/// An interactive 3D overlay hosted by <c>MeshViewportControl</c> — a drag handle, marker, or
/// other on-screen affordance rendered on top of the mesh and driven by mouse input. This is the
/// contract M3's plane-cut, transform, and drain-hole-placement features build their gizmos
/// against; implementing this interface should be the *only* thing those features need to do —
/// <c>MeshViewportControl</c> itself should not need further changes.
///
/// <para>
/// Only one gizmo is active at a time, set via <c>MeshViewportControl.Gizmo</c>. The control gives
/// the active gizmo first refusal on every pointer event (mirroring the existing shift-modifier
/// pan/orbit branch in <c>MeshViewportControl</c>): <see cref="OnPointerPressed"/> is asked first,
/// and if it returns <c>true</c>, <c>MeshViewportControl</c> suppresses camera orbit/pan for the
/// rest of that drag and routes every subsequent move/release to the gizmo instead, until
/// <see cref="OnPointerReleased"/> is called. If a press is not consumed, camera orbit/pan behaves
/// exactly as before M3. <see cref="OnPointerMoved"/> is still called on every move even when no
/// button is pressed (so a gizmo can render hover feedback before a drag starts), but its return
/// value only matters while a drag it started is in progress.
/// </para>
///
/// <para>
/// GL resources: a gizmo owns and lazily creates whatever GL objects it needs inside
/// <see cref="Render"/> (the same "create-on-first-use" pattern is fine here since, unlike
/// <c>MeshRenderer</c>, gizmos are optional and may never render). If a gizmo holds GL resources,
/// implement <see cref="System.IDisposable"/> too — <c>MeshViewportControl</c> disposes the active
/// gizmo (if disposable) from <c>OnOpenGlDeinit</c>, where a GL context is guaranteed current.
/// </para>
/// </summary>
public interface IViewportGizmo
{
    /// <summary>
    /// Draws the gizmo. Called once per frame after the mesh itself, with a current GL context
    /// (same guarantee <c>MeshRenderer.Render</c> relies on). <paramref name="view"/>/<paramref name="projection"/>
    /// are the same matrices used to render the mesh that frame; the mesh's model matrix is
    /// currently always identity (see <c>MeshViewportControl.OnOpenGlRender</c>), so it is not
    /// threaded through here.
    /// </summary>
    void Render(GlApi gl, Matrix4x4 view, Matrix4x4 projection);

    /// <summary>
    /// A pointer button was pressed. Return <c>true</c> to claim the drag (camera orbit/pan is
    /// then suppressed until <see cref="OnPointerReleased"/>); return <c>false</c> to decline and
    /// let the event fall through to normal camera handling.
    /// </summary>
    bool OnPointerPressed(GizmoPointerEvent e);

    /// <summary>
    /// The pointer moved. Called on every move regardless of whether a gizmo drag is active, so a
    /// gizmo can update hover state; the return value is only meaningful (and only consulted by
    /// <c>MeshViewportControl</c>) while this gizmo is the one that claimed the current drag.
    /// </summary>
    bool OnPointerMoved(GizmoPointerEvent e);

    /// <summary>
    /// A pointer button was released. Only called when this gizmo claimed the drag via
    /// <see cref="OnPointerPressed"/>; use it to end the drag and reset any transient state.
    /// </summary>
    bool OnPointerReleased(GizmoPointerEvent e);
}
