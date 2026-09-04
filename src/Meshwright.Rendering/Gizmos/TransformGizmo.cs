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

    private Vector3 _center;
    private TransformMode _mode = TransformMode.Move;
    private Vector3 _currentTransform = Vector3.Zero;
    private bool _isDragging;
    private int? _activeAxis; // 0=X, 1=Y, 2=Z
    private Vector3 _dragStartPosition;
    private Matrix4x4 _dragStartView;
    private Matrix4x4 _dragStartProjection;

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
    public Vector3 CurrentTransform => _currentTransform;

    public TransformGizmo(Vector3 initialCenter)
    {
        _center = initialCenter;
    }

    public void SetMode(TransformMode mode)
    {
        _mode = mode;
        _isDragging = false;
        _activeAxis = null;
    }

    public void SetCenter(Vector3 center)
    {
        _center = center;
    }

    public void ResetTransform()
    {
        _currentTransform = Vector3.Zero;
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
        var model = Matrix4x4.CreateTranslation(_center) * Matrix4x4.CreateScale(0.15f);
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
        float arrowLength = 0.5f;
        var endpoint = base_ + direction * arrowLength;

        // Build arrow line and cone from base to endpoint
        var model = Matrix4x4.CreateTranslation(base_);
        SetMatrixUniform(gl, program, "uModel", model);
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
        const float radius = 0.4f;
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

    public bool OnPointerPressed(GizmoPointerEvent e)
    {
        if (e.Button != GizmoPointerButton.Primary)
            return false;

        float distToCenter = Vector3.Distance(e.Ray.Origin, _center);
        if (distToCenter > 5f)
            return false;

        _isDragging = true;
        _dragStartPosition = _center;
        _dragStartView = Matrix4x4.Identity; // Note: would need actual matrices from viewport
        _dragStartProjection = Matrix4x4.Identity;

        // Pick the nearest axis (for move/rotate) or just activate drag (for scale)
        _activeAxis = PickNearestAxis(e.Ray);
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

        return true;
    }

    public bool OnPointerReleased(GizmoPointerEvent e)
    {
        bool wasActive = _isDragging;
        _isDragging = false;
        _activeAxis = null;
        return wasActive;
    }

    private int? PickNearestAxis(ViewportRay ray)
    {
        float minDist = float.MaxValue;
        int? nearest = null;

        Vector3[] axes = { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        for (int i = 0; i < 3; i++)
        {
            var axisEnd = _center + axes[i] * 0.5f;
            float dist = DistancePointToRay(axisEnd, ray);
            if (dist < minDist && dist < 0.2f)
            {
                minDist = dist;
                nearest = i;
            }
        }

        return nearest;
    }

    private float DistancePointToRay(Vector3 point, ViewportRay ray)
    {
        var rayOrigin = ray.Origin;
        var rayDir = Vector3.Normalize(ray.Direction);
        var toPoint = point - rayOrigin;
        var projected = Vector3.Dot(toPoint, rayDir);
        var closestPoint = rayOrigin + rayDir * Math.Max(0, projected);
        return Vector3.Distance(point, closestPoint);
    }

    private void UpdateMoveTransform(ViewportRay ray)
    {
        if (_activeAxis == null)
            return;

        Vector3 axis = _activeAxis switch
        {
            0 => Vector3.UnitX,
            1 => Vector3.UnitY,
            2 => Vector3.UnitZ,
            _ => Vector3.Zero,
        };

        // Simple 1D drag along the axis
        var toPoint = _dragStartPosition + axis * 10f - ray.Origin;
        float t = Vector3.Dot(toPoint, Vector3.Normalize(ray.Direction));
        var dragPos = ray.Origin + Vector3.Normalize(ray.Direction) * t;

        _currentTransform = (dragPos - _center) * axis;
    }

    private void UpdateRotateTransform(ViewportRay ray)
    {
        if (_activeAxis == null)
            return;

        // Simplified: accumulate angle based on ray movement
        // In a production gizmo, this would track actual rotation around the axis
        _currentTransform.X += 0.5f;
    }

    private void UpdateScaleTransform(ViewportRay ray)
    {
        // Scale based on distance from center
        float dist = Vector3.Distance(ray.Origin, _center);
        _currentTransform = new Vector3(dist / 5f, dist / 5f, dist / 5f);
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
