using System;
using System.Numerics;
using Silk.NET.OpenGL;
using Meshwright.Rendering.Camera;
using GlApi = Silk.NET.OpenGL.GL;

namespace Meshwright.Rendering.Gizmos;

/// <summary>
/// Interactive transform gizmo for move, rotate, and scale operations.
/// Supports three mutually-exclusive modes:
/// - Move: three orthogonal colored arrows (X=red, Y=green, Z=blue) along axes from center
/// - Rotate: three rotation rings around the center
/// - Scale: a single handle at the center
/// </summary>
public sealed class TransformGizmo : IViewportGizmo, IDisposable
{
    public enum TransformMode
    {
        Move,
        Rotate,
        Scale,
    }

    // Gizmo dimensions, as fractions of the viewport's height rather than fixed world units. A
    // fixed world size only works at one zoom: 0.5 world units of axis arrow is most of the screen
    // on a 1mm part and a single pixel on a 500mm one, which left the centre handle with a 1px grab
    // radius on a 50mm model. Resolved against the live camera by the *For() helpers below, so the
    // drawn size and the picked size are always the same number.
    private const float AxisLengthFraction = 0.12f;
    private const float RingRadiusFraction = 0.10f;
    private const float CenterHandleRadiusFraction = 0.05f;
    private const float PickToleranceFraction = 0.025f;

    private Vector3 _center;
    private TransformMode _mode = TransformMode.Move;
    private Vector3 _currentTransform = Vector3.Zero;
    private bool _isDragging;
    private bool _hasTransform;
    private int? _activeAxis; // 0=X, 1=Y, 2=Z
    private Vector3 _dragStartAxisPoint;
    private Vector3 _dragStartRadial;
    private float _dragStartDistance;

    private uint _arrowVao;
    private uint _arrowVbo;
    private uint _arrowProgram;
    private int _arrowVertexCount;

    private uint _ringVao;
    private uint _ringVbo;
    private uint _ringProgram;
    private int _ringVertexCount;

    private bool _disposed;

    public Vector3 Center => _center;
    public TransformMode Mode => _mode;

    /// <summary>
    /// The transform accumulated by the current or most recent drag, interpreted per mode:
    /// Move = per-axis offset in world units; Rotate = signed angle in degrees in X, swept around
    /// <see cref="ActiveAxis"/>; Scale = uniform factor replicated in all three components.
    /// </summary>
    public Vector3 CurrentTransform => _currentTransform;

    /// <summary>Axis the last drag acted on (0=X, 1=Y, 2=Z), or null if none has been picked.</summary>
    public int? ActiveAxis => _activeAxis;

    /// <summary>True once a drag has produced a transform since the last <see cref="ResetTransform"/> or mode change.</summary>
    public bool HasTransform => _hasTransform;

    /// <summary>Raised whenever <see cref="CurrentTransform"/> changes during a drag.</summary>
    public event EventHandler? TransformChanged;

    public TransformGizmo(Vector3 initialCenter)
    {
        _center = initialCenter;
    }

    public void SetMode(TransformMode mode)
    {
        _mode = mode;
        _isDragging = false;
        _activeAxis = null;
        ResetTransform();
    }

    public void SetCenter(Vector3 center)
    {
        _center = center;
    }

    public void ResetTransform()
    {
        _currentTransform = _mode == TransformMode.Scale ? Vector3.One : Vector3.Zero;
        _hasTransform = false;
    }

    public void Render(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        switch (_mode)
        {
            case TransformMode.Move:
                RenderMoveGizmo(gl, view, projection);
                break;
            case TransformMode.Rotate:
                RenderRotateGizmo(gl, view, projection);
                break;
            case TransformMode.Scale:
                RenderScaleGizmo(gl, view, projection);
                break;
        }
    }

    private void RenderMoveGizmo(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_arrowProgram == 0)
        {
            BuildArrowGeometry(gl);
        }

        gl.UseProgram(_arrowProgram);

        // X-axis (red arrow)
        RenderArrow(gl, _arrowProgram, _center, Vector3.UnitX, new Vector3(1, 0, 0), view, projection);

        // Y-axis (green arrow)
        RenderArrow(gl, _arrowProgram, _center, Vector3.UnitY, new Vector3(0, 1, 0), view, projection);

        // Z-axis (blue arrow)
        RenderArrow(gl, _arrowProgram, _center, Vector3.UnitZ, new Vector3(0, 0, 1), view, projection);
    }

    private void RenderRotateGizmo(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_ringProgram == 0)
        {
            BuildRingGeometry(gl);
        }

        gl.UseProgram(_ringProgram);

        // X-axis ring (red)
        RenderRing(gl, _ringProgram, _center, Vector3.UnitX, new Vector3(1, 0, 0), view, projection);

        // Y-axis ring (green)
        RenderRing(gl, _ringProgram, _center, Vector3.UnitY, new Vector3(0, 1, 0), view, projection);

        // Z-axis ring (blue)
        RenderRing(gl, _ringProgram, _center, Vector3.UnitZ, new Vector3(0, 0, 1), view, projection);
    }

    private void RenderScaleGizmo(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_arrowProgram == 0)
        {
            BuildArrowGeometry(gl);
        }

        gl.UseProgram(_arrowProgram);

        // Single scale handle at center (white sphere)
        var model = Matrix4x4.CreateScale(CenterHandleRadiusFor(view, projection)) * Matrix4x4.CreateTranslation(_center);
        SetMatrixUniform(gl, _arrowProgram, "uModel", model);
        SetMatrixUniform(gl, _arrowProgram, "uView", view);
        SetMatrixUniform(gl, _arrowProgram, "uProjection", projection);

        int colorLocation = gl.GetUniformLocation(_arrowProgram, "uColor");
        gl.Uniform3(colorLocation, 1f, 1f, 1f);

        BuildAndRenderSphere(gl, _arrowProgram);
    }

    private void RenderArrow(GlApi gl, uint program, Vector3 base_, Vector3 direction, Vector3 color,
        Matrix4x4 view, Matrix4x4 projection)
    {
        var endpoint = base_ + direction * AxisLengthFor(view, projection);

        // Build arrow line and cone from base to endpoint; the vertices below are already in
        // world space, so the model matrix stays identity.
        SetMatrixUniform(gl, program, "uModel", Matrix4x4.Identity);
        SetMatrixUniform(gl, program, "uView", view);
        SetMatrixUniform(gl, program, "uProjection", projection);

        int colorLocation = gl.GetUniformLocation(program, "uColor");
        gl.Uniform3(colorLocation, color.X, color.Y, color.Z);

        // For simplicity, just render a line from base to endpoint
        // A full implementation would include a cone head
        var lineVerts = new float[]
        {
            base_.X, base_.Y, base_.Z,
            endpoint.X, endpoint.Y, endpoint.Z,
        };

        unsafe
        {
            fixed (float* ptr = lineVerts)
            {
                uint vao = gl.GenVertexArray();
                uint vbo = gl.GenBuffer();
                gl.BindVertexArray(vao);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(lineVerts.Length * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
                gl.EnableVertexAttribArray(0);
                gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
                gl.DrawArrays(PrimitiveType.Lines, 0, 2);
                gl.BindVertexArray(0);
                gl.DeleteBuffer(vbo);
                gl.DeleteVertexArray(vao);
            }
        }
    }

    private void RenderRing(GlApi gl, uint program, Vector3 center, Vector3 axis, Vector3 color,
        Matrix4x4 view, Matrix4x4 projection)
    {
        // Build a circle in the plane perpendicular to the axis
        const int segments = 32;
        float radius = RingRadiusFor(view, projection);
        var verts = new List<float>();

        for (int i = 0; i < segments; i++)
        {
            float angle1 = (float)(2 * Math.PI * i / segments);
            float angle2 = (float)(2 * Math.PI * (i + 1) / segments);

            Vector3 p1 = GetPointOnRing(axis, radius, angle1);
            Vector3 p2 = GetPointOnRing(axis, radius, angle2);

            verts.AddRange(new[] { center.X + p1.X, center.Y + p1.Y, center.Z + p1.Z });
            verts.AddRange(new[] { center.X + p2.X, center.Y + p2.Y, center.Z + p2.Z });
        }

        SetMatrixUniform(gl, program, "uModel", Matrix4x4.Identity);
        SetMatrixUniform(gl, program, "uView", view);
        SetMatrixUniform(gl, program, "uProjection", projection);

        int colorLocation = gl.GetUniformLocation(program, "uColor");
        gl.Uniform3(colorLocation, color.X, color.Y, color.Z);

        unsafe
        {
            fixed (float* ptr = verts.ToArray())
            {
                uint vao = gl.GenVertexArray();
                uint vbo = gl.GenBuffer();
                gl.BindVertexArray(vao);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Count * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
                gl.EnableVertexAttribArray(0);
                gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
                gl.DrawArrays(PrimitiveType.Lines, 0, (uint)verts.Count / 3);
                gl.BindVertexArray(0);
                gl.DeleteBuffer(vbo);
                gl.DeleteVertexArray(vao);
            }
        }
    }

    private Vector3 GetPointOnRing(Vector3 axis, float radius, float angle)
    {
        // Create two perpendicular vectors to the axis
        Vector3 u, v;
        if (Math.Abs(axis.X) < 0.9f)
        {
            u = Vector3.Cross(axis, Vector3.UnitX);
        }
        else
        {
            u = Vector3.Cross(axis, Vector3.UnitY);
        }
        u = Vector3.Normalize(u);
        v = Vector3.Cross(axis, u);

        return u * (float)Math.Cos(angle) * radius + v * (float)Math.Sin(angle) * radius;
    }

    private void BuildAndRenderSphere(GlApi gl, uint program)
    {
        const int latSteps = 8;
        const int lonSteps = 8;
        var vertices = new List<float>();

        for (int lat = 0; lat < latSteps; lat++)
        {
            float lat0 = (float)(Math.PI * (lat / (float)latSteps - 0.5));
            float lat1 = (float)(Math.PI * ((lat + 1) / (float)latSteps - 0.5));

            for (int lon = 0; lon < lonSteps; lon++)
            {
                float lon0 = (float)(2f * Math.PI * (lon / (float)lonSteps));
                float lon1 = (float)(2f * Math.PI * ((lon + 1) / (float)lonSteps));

                var v0 = new Vector3(
                    (float)(Math.Cos(lon0) * Math.Cos(lat0)),
                    (float)Math.Sin(lat0),
                    (float)(Math.Sin(lon0) * Math.Cos(lat0)));

                var v1 = new Vector3(
                    (float)(Math.Cos(lon1) * Math.Cos(lat0)),
                    (float)Math.Sin(lat0),
                    (float)(Math.Sin(lon1) * Math.Cos(lat0)));

                var v2 = new Vector3(
                    (float)(Math.Cos(lon0) * Math.Cos(lat1)),
                    (float)Math.Sin(lat1),
                    (float)(Math.Sin(lon0) * Math.Cos(lat1)));

                var v3 = new Vector3(
                    (float)(Math.Cos(lon1) * Math.Cos(lat1)),
                    (float)Math.Sin(lat1),
                    (float)(Math.Sin(lon1) * Math.Cos(lat1)));

                vertices.AddRange(new[] { v0.X, v0.Y, v0.Z, v1.X, v1.Y, v1.Z, v2.X, v2.Y, v2.Z });
                vertices.AddRange(new[] { v1.X, v1.Y, v1.Z, v3.X, v3.Y, v3.Z, v2.X, v2.Y, v2.Z });
            }
        }

        unsafe
        {
            fixed (float* ptr = vertices.ToArray())
            {
                uint vao = gl.GenVertexArray();
                uint vbo = gl.GenBuffer();
                gl.BindVertexArray(vao);
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Count * sizeof(float)), ptr, BufferUsageARB.StaticDraw);
                gl.EnableVertexAttribArray(0);
                gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
                gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)vertices.Count / 3);
                gl.BindVertexArray(0);
                gl.DeleteBuffer(vbo);
                gl.DeleteVertexArray(vao);
            }
        }
    }

    private float AxisLengthFor(Matrix4x4 view, Matrix4x4 projection) =>
        GizmoScale.ForFractionOfHeight(_center, view, projection, AxisLengthFraction);

    private float RingRadiusFor(Matrix4x4 view, Matrix4x4 projection) =>
        GizmoScale.ForFractionOfHeight(_center, view, projection, RingRadiusFraction);

    private float CenterHandleRadiusFor(Matrix4x4 view, Matrix4x4 projection) =>
        GizmoScale.ForFractionOfHeight(_center, view, projection, CenterHandleRadiusFraction);

    private float PickToleranceFor(Matrix4x4 view, Matrix4x4 projection) =>
        GizmoScale.ForFractionOfHeight(_center, view, projection, PickToleranceFraction);

    public bool OnPointerPressed(GizmoPointerEvent e)
    {
        if (e.Button != GizmoPointerButton.Primary)
            return false;

        switch (_mode)
        {
            case TransformMode.Move:
            {
                int? axis = PickNearestAxis(e.Ray, e.View, e.Projection);
                if (axis is null)
                    return false;

                _activeAxis = axis;
                ClosestPointOnAxis(_center, AxisVector(axis.Value), e.Ray, out _dragStartAxisPoint);
                _currentTransform = Vector3.Zero;
                break;
            }

            case TransformMode.Rotate:
            {
                int? axis = PickNearestRing(e.Ray, e.View, e.Projection);
                if (axis is null)
                    return false;

                var normal = AxisVector(axis.Value);
                if (!IntersectPlane(_center, normal, e.Ray, out Vector3 hit))
                    return false;

                Vector3 radial = RejectOntoPlane(hit - _center, normal);
                if (radial.LengthSquared() < 1e-8f)
                    return false;

                _activeAxis = axis;
                _dragStartRadial = Vector3.Normalize(radial);
                _currentTransform = Vector3.Zero;
                break;
            }

            case TransformMode.Scale:
            {
                float distance = DistancePointToRay(_center, e.Ray);
                if (distance > CenterHandleRadiusFor(e.View, e.Projection))
                    return false;

                _dragStartDistance = Math.Max(distance, 1e-3f);
                _currentTransform = Vector3.One;
                break;
            }
        }

        _isDragging = true;
        TransformChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool OnPointerMoved(GizmoPointerEvent e)
    {
        if (!_isDragging)
            return false;

        switch (_mode)
        {
            case TransformMode.Move:
                UpdateMoveTransform(e.Ray);
                break;
            case TransformMode.Rotate:
                UpdateRotateTransform(e.Ray);
                break;
            case TransformMode.Scale:
                UpdateScaleTransform(e.Ray);
                break;
        }

        _hasTransform = true;
        TransformChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool OnPointerReleased(GizmoPointerEvent e)
    {
        bool wasActive = _isDragging;
        _isDragging = false;
        return wasActive;
    }

    private int? PickNearestAxis(ViewportRay ray, Matrix4x4 view, Matrix4x4 projection)
    {
        float minDist = float.MaxValue;
        int? nearest = null;
        float axisLength = AxisLengthFor(view, projection);
        float tolerance = PickToleranceFor(view, projection);

        for (int i = 0; i < 3; i++)
        {
            var axisEnd = _center + AxisVector(i) * axisLength;
            float dist = DistancePointToRay(axisEnd, ray);
            if (dist < minDist && dist < tolerance)
            {
                minDist = dist;
                nearest = i;
            }
        }

        return nearest;
    }

    private int? PickNearestRing(ViewportRay ray, Matrix4x4 view, Matrix4x4 projection)
    {
        float minError = float.MaxValue;
        int? nearest = null;
        float ringRadius = RingRadiusFor(view, projection);
        float tolerance = PickToleranceFor(view, projection);

        for (int i = 0; i < 3; i++)
        {
            if (!IntersectPlane(_center, AxisVector(i), ray, out Vector3 hit))
                continue;

            float error = Math.Abs((hit - _center).Length() - ringRadius);
            if (error < minError && error < tolerance)
            {
                minError = error;
                nearest = i;
            }
        }

        return nearest;
    }

    private static Vector3 AxisVector(int axis) => axis switch
    {
        0 => Vector3.UnitX,
        1 => Vector3.UnitY,
        2 => Vector3.UnitZ,
        _ => Vector3.Zero,
    };

    private static float DistancePointToRay(Vector3 point, ViewportRay ray)
    {
        var rayOrigin = ray.Origin;
        var rayDir = Vector3.Normalize(ray.Direction);
        var toPoint = point - rayOrigin;
        var projected = Vector3.Dot(toPoint, rayDir);
        var closestPoint = rayOrigin + rayDir * Math.Max(0, projected);
        return Vector3.Distance(point, closestPoint);
    }

    /// <summary>Closest point on the infinite line through <paramref name="origin"/> along
    /// <paramref name="axis"/> to the given ray; false if the two are (near-)parallel.</summary>
    private static bool ClosestPointOnAxis(Vector3 origin, Vector3 axis, ViewportRay ray, out Vector3 point)
    {
        var rayDir = Vector3.Normalize(ray.Direction);
        float axisDotRay = Vector3.Dot(axis, rayDir);
        float denominator = 1f - axisDotRay * axisDotRay;
        if (denominator < 1e-6f)
        {
            point = origin;
            return false;
        }

        var toRayOrigin = ray.Origin - origin;
        float t = (Vector3.Dot(toRayOrigin, axis) - axisDotRay * Vector3.Dot(toRayOrigin, rayDir)) / denominator;
        point = origin + axis * t;
        return true;
    }

    private static bool IntersectPlane(Vector3 planePoint, Vector3 planeNormal, ViewportRay ray, out Vector3 hit)
    {
        var rayDir = Vector3.Normalize(ray.Direction);
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

    private static Vector3 RejectOntoPlane(Vector3 vector, Vector3 planeNormal) =>
        vector - planeNormal * Vector3.Dot(vector, planeNormal);

    private void UpdateMoveTransform(ViewportRay ray)
    {
        if (_activeAxis is null)
            return;

        Vector3 axis = AxisVector(_activeAxis.Value);
        if (!ClosestPointOnAxis(_center, axis, ray, out Vector3 dragPoint))
            return;

        // 1D drag: the offset is how far along the axis the pointer has slid since the press.
        _currentTransform = axis * Vector3.Dot(dragPoint - _dragStartAxisPoint, axis);
    }

    private void UpdateRotateTransform(ViewportRay ray)
    {
        if (_activeAxis is null)
            return;

        Vector3 normal = AxisVector(_activeAxis.Value);
        if (!IntersectPlane(_center, normal, ray, out Vector3 hit))
            return;

        Vector3 radial = RejectOntoPlane(hit - _center, normal);
        if (radial.LengthSquared() < 1e-8f)
            return;

        radial = Vector3.Normalize(radial);

        // Signed angle swept around the axis between the press point and the current ray hit,
        // both taken on the plane perpendicular to the active axis.
        float sin = Vector3.Dot(Vector3.Cross(_dragStartRadial, radial), normal);
        float cos = Vector3.Dot(_dragStartRadial, radial);
        float degrees = (float)(Math.Atan2(sin, cos) * 180.0 / Math.PI);

        _currentTransform = new Vector3(degrees, 0f, 0f);
    }

    private void UpdateScaleTransform(ViewportRay ray)
    {
        // Uniform scale from how far the pointer has moved away from (or toward) the center,
        // measured as the perpendicular distance from the center to the pointer ray.
        float distance = Math.Max(DistancePointToRay(_center, ray), 1e-3f);
        float factor = Math.Clamp(distance / _dragStartDistance, 0.01f, 100f);
        _currentTransform = new Vector3(factor, factor, factor);
    }

    private void BuildArrowGeometry(GlApi gl)
    {
        // Placeholder: actual arrow geometry would be built here
        _arrowProgram = CompileProgram(gl);
    }

    private void BuildRingGeometry(GlApi gl)
    {
        // Placeholder: actual ring geometry would be built here
        _ringProgram = CompileProgram(gl);
    }

    private uint CompileProgram(GlApi gl)
    {
        const string vertexShader = """
            #version 330 core
            layout(location = 0) in vec3 aPosition;
            uniform mat4 uModel;
            uniform mat4 uView;
            uniform mat4 uProjection;
            void main() {
                gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
            }
            """;

        const string fragmentShader = """
            #version 330 core
            uniform vec3 uColor;
            out vec4 FragColor;
            void main() {
                FragColor = vec4(uColor, 1.0);
            }
            """;

        uint vs = gl.CreateShader(ShaderType.VertexShader);
        gl.ShaderSource(vs, vertexShader);
        gl.CompileShader(vs);

        uint fs = gl.CreateShader(ShaderType.FragmentShader);
        gl.ShaderSource(fs, fragmentShader);
        gl.CompileShader(fs);

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.LinkProgram(program);

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);

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

    public void Dispose()
    {
        if (_disposed)
            return;

        // GL resources are cleaned up by individual render calls
        _disposed = true;
    }
}
