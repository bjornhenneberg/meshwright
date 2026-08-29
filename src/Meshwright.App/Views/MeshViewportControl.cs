using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Meshwright.Geometry;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.GL;
using Silk.NET.OpenGL;

namespace Meshwright.App.Views;

/// <summary>
/// Avalonia control that hosts an OpenGL viewport rendering a <see cref="TriangleMesh"/> via
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
    private TriangleMesh? _pendingMesh;
    private TriangleMesh? _mesh;

    private bool _isOrbiting;
    private bool _isPanning;
    private Point _lastPointerPosition;

    public TriangleMesh? Mesh
    {
        get => _mesh;
        set
        {
            _mesh = value;
            if (_renderer is not null)
            {
                if (value is not null)
                {
                    _renderer.UploadMesh(value);
                }
            }
            else
            {
                _pendingMesh = value;
            }

            if (value is not null)
            {
                (System.Numerics.Vector3 center, float radius) = value.GetBounds();
                _camera.Frame(center, radius);
            }

            RequestNextFrameRendering();
        }
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        _gl = Silk.NET.OpenGL.GL.GetApi(gl.GetProcAddress);
        _renderer = new MeshRenderer(_gl);
        _renderer.Initialize();

        if (_pendingMesh is not null)
        {
            _renderer.UploadMesh(_pendingMesh);
            _pendingMesh = null;
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is null || _renderer is null)
        {
            return;
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
        _renderer.Render(_camera.GetViewMatrix(), _camera.GetProjectionMatrix(aspect), System.Numerics.Matrix4x4.Identity);
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
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

        _lastPointerPosition = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    private void HandlePointerMoved(PointerEventArgs e)
    {
        if (!_isOrbiting && !_isPanning)
        {
            return;
        }

        var position = e.GetPosition(this);
        double dx = position.X - _lastPointerPosition.X;
        double dy = position.Y - _lastPointerPosition.Y;
        _lastPointerPosition = position;

        if (_isOrbiting)
        {
            _camera.Orbit((float)(dx * OrbitSensitivity), (float)(-dy * OrbitSensitivity));
        }
        else if (_isPanning)
        {
            _camera.Pan((float)(dx * PanSensitivity), (float)(dy * PanSensitivity));
        }

        RequestNextFrameRendering();
    }

    private void HandlePointerReleased(PointerReleasedEventArgs e)
    {
        _isOrbiting = false;
        _isPanning = false;
        e.Pointer.Capture(null);
    }

    private void HandlePointerWheelChanged(PointerWheelEventArgs e)
    {
        // Scale the zoom step by current distance so it feels consistent whether zoomed in or out.
        _camera.Zoom((float)(-e.Delta.Y) * _camera.Distance * ZoomSensitivity * 100f);
        RequestNextFrameRendering();
    }
}
