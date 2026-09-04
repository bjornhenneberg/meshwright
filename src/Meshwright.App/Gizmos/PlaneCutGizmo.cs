using System;
using System.Numerics;
using Silk.NET.OpenGL;
using GlApi = Silk.NET.OpenGL.GL;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;

namespace Meshwright.App.Gizmos;

/// <summary>
/// Interactive plane gizmo for plane cut operation (M3 batch 3). Renders a square plane widget
/// with a normal arrow, allowing the user to:
/// - Click to reposition the plane at a point on the mesh
/// - Drag to rotate the plane's normal
/// - Shift+drag to translate the plane along its normal
/// </summary>
public sealed class PlaneCutGizmo : IViewportGizmo, IDisposable
{
    private Vector3 _planePosition;
    private Vector3 _planeNormal = Vector3.UnitZ;
    private float _planeSize = 2.0f;

    private bool _isDragging;
    private Vector3 _dragStartPosition;
    private Vector3 _dragStartNormal;
    private GizmoModifierKeys _dragModifiers;

    // GL resources
    private uint _planeVao;
    private uint _planeVbo;
    private uint _planeProgram;
    private int _planeVertexCount;

    private uint _arrowVao;
    private uint _arrowVbo;
    private uint _arrowProgram;
    private int _arrowVertexCount;

    private bool _disposed;

    public Vector3 PlanePosition => _planePosition;
    public Vector3 PlaneNormal => _planeNormal;

    /// <summary>True once the user has dragged the gizmo; the plane values should then take
    /// precedence over any manually-typed textbox values.</summary>
    public bool WasTouched { get; private set; }

    /// <summary>Raised whenever the plane position or normal changes as a result of dragging.</summary>
    public event EventHandler? Changed;

    public PlaneCutGizmo(Vector3 initialPosition)
    {
        _planePosition = initialPosition;
    }

    public void Render(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_planeProgram == 0)
        {
            BuildGeometry(gl);
        }

        // Render plane square
        RenderPlaneSquare(gl, view, projection);

        // Render normal arrow
        RenderNormalArrow(gl, view, projection);
    }

    public bool OnPointerPressed(GizmoPointerEvent e)
    {
        if (e.Button != GizmoPointerButton.Primary)
        {
            return false;
        }

        // Pick: intersect the ray with the gizmo's own plane, then check whether the hit
        // falls inside the rendered square (see RenderPlaneSquare for the same basis/extent).
        // The previous check (`Vector3.Distance(e.Ray.Origin, _planePosition) < _planeSize +
        // 2.0f`) measured how far the *camera* was from the plane, not where the click
        // landed - depending on zoom level that made every click anywhere in the viewport
        // hit the gizmo, or no click ever hit it.
        if (!IntersectPlane(_planePosition, _planeNormal, e.Ray, out Vector3 hit))
        {
            return false;
        }

        (Vector3 right, Vector3 up) = PlaneBasis(_planeNormal);
        Vector3 local = hit - _planePosition;
        float localRight = Vector3.Dot(local, right);
        float localUp = Vector3.Dot(local, up);
        if (Math.Abs(localRight) > _planeSize || Math.Abs(localUp) > _planeSize)
        {
            return false;
        }

        _isDragging = true;
        _dragStartPosition = _planePosition;
        _dragStartNormal = _planeNormal;
        _dragModifiers = e.Modifiers;
        return true;
    }

    /// <summary>Same right/up basis used to orient the rendered plane square and normal arrow.</summary>
    private static (Vector3 Right, Vector3 Up) PlaneBasis(Vector3 normal)
    {
        Vector3 right = Math.Abs(normal.X) < 0.9f ? Vector3.Cross(Vector3.UnitX, normal) : Vector3.Cross(Vector3.UnitY, normal);
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Cross(normal, right);
        return (right, up);
    }

    private static bool IntersectPlane(Vector3 planePoint, Vector3 planeNormal, ViewportRay ray, out Vector3 hit)
    {
        Vector3 rayDir = Vector3.Normalize(ray.Direction);
        float denominator = Vector3.Dot(planeNormal, rayDir);
        if (Math.Abs(denominator) < 1e-6f)
        {
            hit = planePoint;
            return false;
        }

        float t = Vector3.Dot(planePoint - ray.Origin, planeNormal) / denominator;
        if (t < 0f)
        {
            hit = planePoint;
            return false;
        }

        hit = ray.Origin + rayDir * t;
        return true;
    }

    public bool OnPointerMoved(GizmoPointerEvent e)
    {
        if (!_isDragging)
        {
            return false;
        }

        if (_dragModifiers.HasFlag(GizmoModifierKeys.Shift))
        {
            // Shift+drag: translate along normal
            Vector3 rayDir = e.Ray.Direction;
            Vector3 toPreviousPos = _dragStartPosition - e.Ray.Origin;
            float t = Vector3.Dot(toPreviousPos, rayDir) / rayDir.Length();
            float projectedDelta = Vector3.Dot(e.Ray.PointAt(t) - _dragStartPosition, _planeNormal);
            _planePosition = _dragStartPosition + _planeNormal * projectedDelta;

            WasTouched = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Regular drag: rotate normal
            Vector3 rayDir = e.Ray.Direction;
            float t = (float)((_dragStartPosition - e.Ray.Origin).Length() / rayDir.Length());
            Vector3 pointOnRay = e.Ray.PointAt(t);
            Vector3 delta = pointOnRay - _dragStartPosition;

            // Simple rotation: rotate around an axis perpendicular to both current normal and delta
            if (delta.LengthSquared() > 0.01f)
            {
                Vector3 rotationAxis = Vector3.Cross(_planeNormal, delta);
                if (rotationAxis.LengthSquared() > 0.01f)
                {
                    rotationAxis = Vector3.Normalize(rotationAxis);
                    float angle = Math.Min(delta.Length() * 0.1f, 0.5f);
                    Quaternion rotation = Quaternion.CreateFromAxisAngle(rotationAxis, angle);
                    _planeNormal = Vector3.Transform(_dragStartNormal, rotation);
                    _planeNormal = Vector3.Normalize(_planeNormal);

                    WasTouched = true;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        return true;
    }

    public bool OnPointerReleased(GizmoPointerEvent e)
    {
        _isDragging = false;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // GL cleanup would happen here if we had GL context access
        _disposed = true;
    }

    private void BuildGeometry(GlApi gl)
    {
        BuildPlaneSquare(gl);
        BuildNormalArrow(gl);
    }

    private void BuildPlaneSquare(GlApi gl)
    {
        // Build a square quad in the plane
        float size = _planeSize;
        var vertices = new float[]
        {
            -size, -size, 0, // 0: bottom-left
            size, -size, 0,  // 1: bottom-right
            size, size, 0,   // 2: top-right
            -size, size, 0,  // 3: top-left

            // Edges as line strips for better visualization
            -size, -size, 0,
            size, -size, 0,
            size, size, 0,
            -size, size, 0,
        };

        _planeVertexCount = 8;
        _planeVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _planeVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), vertices, BufferUsageARB.StaticDraw);

        _planeVao = gl.GenVertexArray();
        gl.BindVertexArray(_planeVao);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        gl.EnableVertexAttribArray(0);

        _planeProgram = CreateShaderProgram(gl, BasicVertexShader, BasicFragmentShader);
    }

    private void BuildNormalArrow(GlApi gl)
    {
        // Build an arrow pointing along +Z (will be rotated to match normal)
        var vertices = new float[]
        {
            0, 0, 0,        // Base
            0, 0, 2,        // Tip
            0.1f, 0, 1.8f,  // Arrow head 1
            -0.1f, 0, 1.8f, // Arrow head 2
            0, 0.1f, 1.8f,  // Arrow head 3
        };

        _arrowVertexCount = 5;
        _arrowVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _arrowVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)(vertices.Length * sizeof(float)), vertices, BufferUsageARB.StaticDraw);

        _arrowVao = gl.GenVertexArray();
        gl.BindVertexArray(_arrowVao);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
        gl.EnableVertexAttribArray(0);

        _arrowProgram = CreateShaderProgram(gl, BasicVertexShader, BasicFragmentShader);
    }

    private void RenderPlaneSquare(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        // Create rotation matrix from normal
        Vector3 right = Math.Abs(_planeNormal.X) < 0.9f ? Vector3.Cross(Vector3.UnitX, _planeNormal) : Vector3.Cross(Vector3.UnitY, _planeNormal);
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Cross(_planeNormal, right);

        Matrix4x4 model = Matrix4x4.CreateTranslation(_planePosition);
        // Build rotation matrix from basis vectors
        model.M11 = right.X; model.M12 = right.Y; model.M13 = right.Z;
        model.M21 = up.X; model.M22 = up.Y; model.M23 = up.Z;
        model.M31 = _planeNormal.X; model.M32 = _planeNormal.Y; model.M33 = _planeNormal.Z;

        gl.UseProgram(_planeProgram);
        SetMatrixUniform(gl, _planeProgram, "uModel", model);
        SetMatrixUniform(gl, _planeProgram, "uView", view);
        SetMatrixUniform(gl, _planeProgram, "uProjection", projection);

        int colorLoc = gl.GetUniformLocation(_planeProgram, "uColor");
        gl.Uniform3(colorLoc, 0.3f, 0.7f, 1.0f); // Light blue for plane

        gl.BindVertexArray(_planeVao);
        gl.DrawArrays(PrimitiveType.LineLoop, 4, 4);
    }

    private void RenderNormalArrow(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        // Create rotation matrix from normal (to +Z)
        Vector3 right = Math.Abs(_planeNormal.X) < 0.9f ? Vector3.Cross(Vector3.UnitX, _planeNormal) : Vector3.Cross(Vector3.UnitY, _planeNormal);
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Cross(_planeNormal, right);

        Matrix4x4 model = Matrix4x4.CreateTranslation(_planePosition);
        model.M11 = right.X; model.M12 = right.Y; model.M13 = right.Z;
        model.M21 = up.X; model.M22 = up.Y; model.M23 = up.Z;
        model.M31 = _planeNormal.X; model.M32 = _planeNormal.Y; model.M33 = _planeNormal.Z;

        gl.UseProgram(_arrowProgram);
        SetMatrixUniform(gl, _arrowProgram, "uModel", model);
        SetMatrixUniform(gl, _arrowProgram, "uView", view);
        SetMatrixUniform(gl, _arrowProgram, "uProjection", projection);

        int colorLoc = gl.GetUniformLocation(_arrowProgram, "uColor");
        gl.Uniform3(colorLoc, 1.0f, 1.0f, 1.0f); // White for arrow

        gl.BindVertexArray(_arrowVao);
        gl.DrawArrays(PrimitiveType.LineStrip, 0, 2);
        gl.DrawArrays(PrimitiveType.Triangles, 2, 3);
    }

    private static uint CreateShaderProgram(GlApi gl, string vertexSource, string fragmentSource)
    {
        uint vertex = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vertex, vertexSource);
        gl.CompileShader(vertex);

        string vertexLog = gl.GetShaderInfoLog(vertex);
        if (!string.IsNullOrEmpty(vertexLog))
        {
            System.Diagnostics.Debug.WriteLine($"Vertex shader compilation log: {vertexLog}");
        }

        uint fragment = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fragment, fragmentSource);
        gl.CompileShader(fragment);

        string fragmentLog = gl.GetShaderInfoLog(fragment);
        if (!string.IsNullOrEmpty(fragmentLog))
        {
            System.Diagnostics.Debug.WriteLine($"Fragment shader compilation log: {fragmentLog}");
        }

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vertex);
        gl.AttachShader(program, fragment);
        gl.LinkProgram(program);

        gl.DeleteShader(vertex);
        gl.DeleteShader(fragment);

        return program;
    }

    private static void SetMatrixUniform(GlApi gl, uint program, string name, Matrix4x4 matrix)
    {
        int location = gl.GetUniformLocation(program, name);
        unsafe
        {
            gl.UniformMatrix4(location, 1, false, (float*)&matrix);
        }
    }

    private const string BasicVertexShader = @"
#version 330 core
layout (location = 0) in vec3 aPosition;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;
void main()
{
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
}";

    private const string BasicFragmentShader = @"
#version 330 core
uniform vec3 uColor;
out vec4 FragColor;
void main()
{
    FragColor = vec4(uColor, 1.0);
}";
}
