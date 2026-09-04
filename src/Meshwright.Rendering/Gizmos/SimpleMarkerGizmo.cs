using System;
using System.Numerics;
using Silk.NET.OpenGL;
using GlApi = Silk.NET.OpenGL.GL;

namespace Meshwright.Rendering.Gizmos;

/// <summary>
/// A minimal proof-of-concept gizmo: a white sphere at a fixed position, draggable along a line
/// parallel to the camera's right axis (mimicking an x-axis handle on a transform gizmo). Exercises
/// all paths of the <see cref="IViewportGizmo"/> contract for smoke-testing the infrastructure.
/// This gizmo can be instantiated in tests but is not intended for permanent shipping in the UI;
/// it exists purely to verify that the render + pointer-event machinery works.
/// </summary>
public sealed class SimpleMarkerGizmo : IViewportGizmo, IDisposable
{
    private Vector3 _position;
    private Vector3 _dragStartPosition;
    private bool _isDragging;
    private float _markerRadius = 0.1f;

    private uint _sphereVao;
    private uint _sphereVbo;
    private uint _sphereProgram;
    private int _sphereVertexCount;

    private bool _disposed;

    public Vector3 Position => _position;

    /// <summary>Initialize the marker at the given position (typically a mesh center or picked point).</summary>
    public SimpleMarkerGizmo(Vector3 initialPosition)
    {
        _position = initialPosition;
    }

    public void Render(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_sphereProgram == 0)
        {
            BuildSphereGeometry(gl);
        }

        gl.UseProgram(_sphereProgram);
        SetMatrixUniform(gl, _sphereProgram, "uModel", Matrix4x4.CreateTranslation(_position) * Matrix4x4.CreateScale(_markerRadius));
        SetMatrixUniform(gl, _sphereProgram, "uView", view);
        SetMatrixUniform(gl, _sphereProgram, "uProjection", projection);

        int colorLocation = gl.GetUniformLocation(_sphereProgram, "uColor");
        gl.Uniform3(colorLocation, 1f, 1f, 1f);

        gl.BindVertexArray(_sphereVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_sphereVertexCount);
        gl.BindVertexArray(0);
    }

    public bool OnPointerPressed(GizmoPointerEvent e)
    {
        // Pick interaction: accept primary button only, and only if the ray passes near the marker
        if (e.Button != GizmoPointerButton.Primary)
        {
            return false;
        }

        float distanceToMarker = Vector3.Distance(e.Ray.Origin, _position);
        float markerScreenRadius = _markerRadius * 2f; // rough pick radius

        if (distanceToMarker < markerScreenRadius + 1f) // within ~1 unit + marker size
        {
            _isDragging = true;
            _dragStartPosition = _position;
            return true;
        }

        return false;
    }

    public bool OnPointerMoved(GizmoPointerEvent e)
    {
        if (!_isDragging)
        {
            return false;
        }

        // Drag along a line: project the ray onto a line parallel to the camera's right axis
        // passing through the marker's start position (a simplified 1D drag, not full 3D).
        // In a real transform gizmo, this would be one of several axis handles.
        Vector3 rayDir = e.Ray.Direction;
        float t = (float)((_dragStartPosition - e.Ray.Origin).Length() / rayDir.Length());
        t = Math.Clamp(t, 0f, 100f);

        Vector3 pointOnRay = e.Ray.PointAt(t);
        // Move the marker along x-axis by the difference in the ray's y-component
        Vector3 delta = pointOnRay - _dragStartPosition;
        _position = _dragStartPosition + new Vector3(delta.Y * 0.5f, 0f, 0f);

        return true;
    }

    public bool OnPointerReleased(GizmoPointerEvent e)
    {
        bool wasActive = _isDragging;
        _isDragging = false;
        return wasActive;
    }

    private unsafe void BuildSphereGeometry(GlApi gl)
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

                // Two triangles per quad
                vertices.AddRange(new[] { v0.X, v0.Y, v0.Z, v1.X, v1.Y, v1.Z, v2.X, v2.Y, v2.Z });
                vertices.AddRange(new[] { v1.X, v1.Y, v1.Z, v3.X, v3.Y, v3.Z, v2.X, v2.Y, v2.Z });
            }
        }

        _sphereVertexCount = vertices.Count / 3;

        _sphereVao = gl.GenVertexArray();
        _sphereVbo = gl.GenBuffer();

        gl.BindVertexArray(_sphereVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sphereVbo);

        unsafe
        {
            fixed (float* data = vertices.ToArray())
            {
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Count * sizeof(float)), data, BufferUsageARB.StaticDraw);
            }
        }

        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
        gl.BindVertexArray(0);

        _sphereProgram = CompileProgram(gl);
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
        {
            return;
        }

        if (_sphereVao != 0)
        {
            // Note: GL context may not be current here; caller must dispose from OnOpenGlDeinit
        }

        _disposed = true;
    }
}
