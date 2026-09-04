using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;
using Meshwright.Rendering.GL;
using Silk.NET.OpenGL;

namespace Meshwright.App.Views;

/// <summary>
/// Avalonia control that hosts an OpenGL viewport rendering a <see cref="g3.DMesh3"/> via
/// <see cref="MeshRenderer"/>, with mouse-driven orbit/pan/zoom through an <see cref="OrbitCamera"/>.
/// </summary>
public sealed class MeshViewportControl : OpenGlControlBase
{
    private const double OrbitSensitivity = 0.008;
    private const double PanSensitivity = 0.0025;
    private const float ZoomSensitivity = 0.001f;

    private readonly OrbitCamera _camera = new();

    private Silk.NET.OpenGL.GL? _gl;
    private MeshRenderer? _renderer;
    private g3.DMesh3? _pendingMesh;
    private g3.DMesh3? _mesh;
    private MeshDiagnosticsReport? _report;
    private bool _highlightsDirty;

    private bool _isOrbiting;
    private bool _isPanning;
    private bool _gizmoClaimingDrag;
    private Point _lastPointerPosition;
    private IViewportGizmo? _gizmo;

    public g3.DMesh3? Mesh
    {
        get => _mesh;
        set
        {
            _mesh = value;
            _pendingMesh = value;

            if (value is not null)
            {
                g3.AxisAlignedBox3d bounds = value.CachedBounds;
                g3.Vector3d center = bounds.Center;
                double radius = bounds.DiagonalLength / 2.0;
                _camera.Frame(new System.Numerics.Vector3((float)center.x, (float)center.y, (float)center.z), (float)radius);
            }

            RequestNextFrameRendering();
        }
    }

    /// <summary>
    /// Diagnostics report for the current <see cref="Mesh"/>; its flagged triangles/edges are
    /// highlighted in the viewport on the next render. Set to null (or a report with no issues)
    /// to fall back to plain shaded rendering.
    /// </summary>
    public MeshDiagnosticsReport? Report
    {
        get => _report;
        set
        {
            _report = value;
            _highlightsDirty = true;
            RequestNextFrameRendering();
        }
    }

    /// <summary>
    /// An interactive overlay gizmo, if any (e.g. a plane-cut handle, transform axes, or drain-hole
    /// placement marker). Only one gizmo is active at a time. Set to null to deactivate.
    /// The gizmo is rendered after the mesh on every frame and is given first refusal on pointer
    /// events (see <see cref="IViewportGizmo"/> for the contract).
    /// </summary>
    public IViewportGizmo? Gizmo
    {
        get => _gizmo;
        set
        {
            _gizmo = value;
            _gizmoClaimingDrag = false;
            RequestNextFrameRendering();
        }
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _gl = Silk.NET.OpenGL.GL.GetApi(gl.GetProcAddress);
        _renderer = new MeshRenderer(_gl);
        _renderer.Initialize();
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is null || _renderer is null)
        {
            return;
        }

        // Uploads must happen here (not in the Mesh/Report setters) because only Avalonia's GL
        // callbacks guarantee a current GL context.
        if (_pendingMesh is not null)
        {
            UploadCurrentMesh(_pendingMesh);
            _pendingMesh = null;
            _highlightsDirty = false;
        }
        else if (_highlightsDirty && _mesh is not null)
        {
            UploadCurrentMesh(_mesh);
            _highlightsDirty = false;
        }

        // OpenGlControlBase does not bind the target framebuffer for us; it hands back the
        // FBO id we must render into (per Avalonia.OpenGL.xml: "fb: The framebuffer ID to
        // render into"), matching Avalonia's own OpenGL sample controls.
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)fb);

        var size = Bounds.Size;
        double scaling = VisualRoot?.RenderScaling ?? 1.0;
        int pixelWidth = Math.Max(1, (int)(size.Width * scaling));
        int pixelHeight = Math.Max(1, (int)(size.Height * scaling));
        _gl.Viewport(0, 0, (uint)pixelWidth, (uint)pixelHeight);

        _gl.Enable(EnableCap.DepthTest);
        _gl.ClearColor(0.15f, 0.15f, 0.18f, 1f);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        float aspect = pixelHeight == 0 ? 1f : (float)pixelWidth / pixelHeight;
        var view = _camera.GetViewMatrix();
        var projection = _camera.GetProjectionMatrix(aspect);
        _renderer.Render(view, projection, System.Numerics.Matrix4x4.Identity);

        // Render active gizmo (if any) on top of the mesh.
        if (_gizmo is not null)
        {
            _gizmo.Render(_gl, view, projection);
        }
    }

    private void UploadCurrentMesh(g3.DMesh3 mesh)
    {
        var flaggedTriangleIds = new HashSet<int>();
        var flaggedEdges = new List<g3.Index2i>();
        foreach (MeshIssue issue in _report?.Issues ?? Array.Empty<MeshIssue>())
        {
            foreach (int triangleId in issue.TriangleIds)
            {
                flaggedTriangleIds.Add(triangleId);
            }

            flaggedEdges.AddRange(issue.EdgeIds);
        }

        _renderer!.UploadMesh(mesh, flaggedTriangleIds, flaggedEdges);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        // Dispose gizmo while GL context is current (if it holds GL resources).
        (_gizmo as IDisposable)?.Dispose();
        _gizmo = null;

        _renderer?.Dispose();
        _renderer = null;
        _gl = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        HandlePointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        HandlePointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        HandlePointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        HandlePointerWheelChanged(e);
    }

    /// <summary>
    /// Entry point for pointer input arriving via the transparent overlay control that sits on
    /// top of this control in <c>MainWindow.axaml</c> (workaround for GL surfaces not always
    /// participating in Avalonia's normal input routing on Linux). <paramref name="e"/> is
    /// targeted at the overlay, but since the overlay occupies the same layout cell and is
    /// stretched identically, <c>e.GetPosition(this)</c> yields the same coordinates as if the
    /// event had targeted this control directly.
    /// </summary>
    public void HandleExternalPointerPressed(PointerPressedEventArgs e) => HandlePointerPressed(e);

    public void HandleExternalPointerMoved(PointerEventArgs e) => HandlePointerMoved(e);

    public void HandleExternalPointerReleased(PointerReleasedEventArgs e) => HandlePointerReleased(e);

    public void HandleExternalPointerWheelChanged(PointerWheelEventArgs e) => HandlePointerWheelChanged(e);

    private void HandlePointerPressed(PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var pointerPos = e.GetPosition(this);
        _lastPointerPosition = pointerPos;

        // Compute unprojected ray for gizmo (and camera) to consume.
        ViewportRay ray = ComputeViewportRay(pointerPos);
        GizmoPointerButton button = GetGizmoButton(point.Properties);
        GizmoModifierKeys modifiers = GetGizmoModifiers(e.KeyModifiers);

        var gizmoEvent = new GizmoPointerEvent(ray, new System.Numerics.Vector2((float)pointerPos.X, (float)pointerPos.Y),
            GetViewportPixelSize(), button, modifiers, _mesh);

        // Gizmo first refusal: if it claims the event, suppress camera manipulation.
        _gizmoClaimingDrag = _gizmo?.OnPointerPressed(gizmoEvent) ?? false;
        if (!_gizmoClaimingDrag)
        {
            // Camera orbit/pan as before, only if gizmo didn't claim the press.
            if (point.Properties.IsLeftButtonPressed)
            {
                bool panModifier = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _isOrbiting = !panModifier;
                _isPanning = panModifier;
            }
            else if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
            {
                _isPanning = true;
            }
        }

        e.Pointer.Capture(this);
        RequestNextFrameRendering();
    }

    private void HandlePointerMoved(PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        ViewportRay ray = ComputeViewportRay(position);

        // If gizmo claimed the current drag, route the move to it (even if no button is pressed).
        if (_gizmoClaimingDrag && _gizmo is not null)
        {
            var gizmoEvent = new GizmoPointerEvent(ray, new System.Numerics.Vector2((float)position.X, (float)position.Y),
                GetViewportPixelSize(), GizmoPointerButton.None, GetGizmoModifiers(e.KeyModifiers), _mesh);
            _gizmo.OnPointerMoved(gizmoEvent);
        }
        else if (_isOrbiting || _isPanning)
        {
            // Normal camera orbit/pan path (only active if a button is down and gizmo didn't claim it).
            double dx = position.X - _lastPointerPosition.X;
            double dy = position.Y - _lastPointerPosition.Y;

            if (_isOrbiting)
            {
                _camera.Orbit((float)(dx * OrbitSensitivity), (float)(-dy * OrbitSensitivity));
            }
            else if (_isPanning)
            {
                _camera.Pan((float)(dx * PanSensitivity), (float)(dy * PanSensitivity));
            }
        }
        else if (_gizmo is not null)
        {
            // Gizmo hover feedback: even with no drag active, send moves to gizmo for hover highlighting.
            var gizmoEvent = new GizmoPointerEvent(ray, new System.Numerics.Vector2((float)position.X, (float)position.Y),
                GetViewportPixelSize(), GizmoPointerButton.None, GetGizmoModifiers(e.KeyModifiers), _mesh);
            _gizmo.OnPointerMoved(gizmoEvent);
        }

        _lastPointerPosition = position;
        RequestNextFrameRendering();
    }

    private void HandlePointerReleased(PointerReleasedEventArgs e)
    {
        if (_gizmoClaimingDrag && _gizmo is not null)
        {
            var point = e.GetCurrentPoint(this);
            var position = e.GetPosition(this);
            ViewportRay ray = ComputeViewportRay(position);
            GizmoPointerButton button = GetGizmoButton(point.Properties);

            var gizmoEvent = new GizmoPointerEvent(ray, new System.Numerics.Vector2((float)position.X, (float)position.Y),
                GetViewportPixelSize(), button, GetGizmoModifiers(e.KeyModifiers), _mesh);
            _gizmo.OnPointerReleased(gizmoEvent);
        }

        _gizmoClaimingDrag = false;
        _isOrbiting = false;
        _isPanning = false;
        e.Pointer.Capture(null);
        RequestNextFrameRendering();
    }

    private void HandlePointerWheelChanged(PointerWheelEventArgs e)
    {
        // Scale the zoom step by current distance so it feels consistent whether zoomed in or out.
        _camera.Zoom((float)(-e.Delta.Y) * _camera.Distance * ZoomSensitivity * 100f);
        RequestNextFrameRendering();
    }

    private ViewportRay ComputeViewportRay(Point pixelPosition)
    {
        var size = Bounds.Size;
        double scaling = VisualRoot?.RenderScaling ?? 1.0;
        int pixelWidth = Math.Max(1, (int)(size.Width * scaling));
        int pixelHeight = Math.Max(1, (int)(size.Height * scaling));

        var scaledPixelPos = new System.Numerics.Vector2((float)(pixelPosition.X * scaling), (float)(pixelPosition.Y * scaling));
        var viewportPixelSize = new System.Numerics.Vector2(pixelWidth, pixelHeight);
        float aspect = pixelHeight == 0 ? 1f : (float)pixelWidth / pixelHeight;

        return ViewportRaycaster.Unproject(scaledPixelPos, viewportPixelSize, _camera.GetViewMatrix(), _camera.GetProjectionMatrix(aspect));
    }

    private System.Numerics.Vector2 GetViewportPixelSize()
    {
        var size = Bounds.Size;
        double scaling = VisualRoot?.RenderScaling ?? 1.0;
        int pixelWidth = Math.Max(1, (int)(size.Width * scaling));
        int pixelHeight = Math.Max(1, (int)(size.Height * scaling));
        return new System.Numerics.Vector2(pixelWidth, pixelHeight);
    }

    private static GizmoPointerButton GetGizmoButton(PointerPointProperties props)
    {
        if (props.IsLeftButtonPressed) return GizmoPointerButton.Primary;
        if (props.IsRightButtonPressed) return GizmoPointerButton.Secondary;
        if (props.IsMiddleButtonPressed) return GizmoPointerButton.Middle;
        return GizmoPointerButton.None;
    }

    private static GizmoModifierKeys GetGizmoModifiers(KeyModifiers modifiers)
    {
        GizmoModifierKeys result = GizmoModifierKeys.None;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= GizmoModifierKeys.Shift;
        if (modifiers.HasFlag(KeyModifiers.Control)) result |= GizmoModifierKeys.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= GizmoModifierKeys.Alt;
        return result;
    }
}
