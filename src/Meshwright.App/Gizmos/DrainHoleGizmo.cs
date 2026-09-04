using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;
using GlApi = Silk.NET.OpenGL.GL;
using g3;
using Meshwright.Geometry.Spatial;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;

namespace Meshwright.App.Gizmos;

/// <summary>
/// Represents a placed drain hole marker for interaction and rendering.
/// </summary>
public sealed class PlacedDrainHole
{
    public int Id { get; }
    public Vector3d SurfacePoint { get; }
    public Vector3d SurfaceNormal { get; }
    public double Diameter { get; set; }
    public double CountersinkDepth { get; set; }

    public PlacedDrainHole(int id, Vector3d surfacePoint, Vector3d surfaceNormal, double diameter, double countersinkDepth = 0.0)
    {
        Id = id;
        SurfacePoint = surfacePoint;
        SurfaceNormal = surfaceNormal.Normalized;
        Diameter = diameter;
        CountersinkDepth = countersinkDepth;
    }
}

/// <summary>
/// Gizmo for placing drain holes on the mesh surface via ray-casting.
/// Users click on the mesh to place hole markers; the gizmo renders them as small spheres
/// and emits events when holes are placed or deleted.
/// </summary>
public sealed class DrainHoleGizmo : IViewportGizmo, IDisposable
{
    private readonly DMeshAABBTree3? _spatialTree;
    private readonly List<PlacedDrainHole> _holes = new();
    private int _nextHoleId = 1;
    private int? _selectedHoleId;
    private bool _isDragging;

    private uint _sphereVao;
    private uint _sphereVbo;
    private uint _sphereProgram;
    private int _sphereVertexCount;

    private bool _disposed;

    /// <summary>Raised when a new hole is placed, providing its location and normal.</summary>
    public event EventHandler<(Vector3d Point, Vector3d Normal)>? HolePlaced;

    /// <summary>Raised when a hole is removed.</summary>
    public event EventHandler<int>? HoleRemoved;

    /// <summary>Placed holes (read-only list for UI binding).</summary>
    public IReadOnlyList<PlacedDrainHole> Holes => _holes.AsReadOnly();

    /// <summary>Currently selected hole ID, if any.</summary>
    public int? SelectedHoleId
    {
        get => _selectedHoleId;
        set => _selectedHoleId = value;
    }

    /// <summary>
    /// Creates a gizmo for a mesh (builds a spatial tree for ray-casting if the mesh is provided).
    /// </summary>
    public DrainHoleGizmo(DMesh3? mesh = null)
    {
        if (mesh is not null && mesh.TriangleCount > 0)
        {
            _spatialTree = new DMeshAABBTree3(mesh, autoBuild: true);
        }
    }

    /// <summary>Clears all placed holes.</summary>
    public void ClearHoles()
    {
        _holes.Clear();
        _selectedHoleId = null;
    }

    /// <summary>Removes the hole with the given ID, if it exists.</summary>
    public bool RemoveHole(int holeId)
    {
        int idx = _holes.FindIndex(h => h.Id == holeId);
        if (idx < 0)
        {
            return false;
        }

        _holes.RemoveAt(idx);
        if (_selectedHoleId == holeId)
        {
            _selectedHoleId = null;
        }

        HoleRemoved?.Invoke(this, holeId);
        return true;
    }

    public void Render(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_holes.Count == 0)
        {
            return;
        }

        if (_sphereProgram == 0)
        {
            BuildSphereGeometry(gl);
        }

        gl.UseProgram(_sphereProgram);

        foreach (var hole in _holes)
        {
            // Render hole marker as a sphere
            var worldPos = new Vector3((float)hole.SurfacePoint.x, (float)hole.SurfacePoint.y, (float)hole.SurfacePoint.z);
            float markerRadius = (float)(hole.Diameter / 2.0 * 0.3f); // Slightly smaller than actual diameter for clarity

            // Scale then translate: System.Numerics composes left-to-right for row vectors, so
            // the reverse order would scale the translation itself and pull the marker toward the
            // world origin (a hole at x=10 with a 2mm marker would render at x=3).
            SetMatrixUniform(gl, _sphereProgram, "uModel",
                Matrix4x4.CreateScale(markerRadius) * Matrix4x4.CreateTranslation(worldPos));
            SetMatrixUniform(gl, _sphereProgram, "uView", view);
            SetMatrixUniform(gl, _sphereProgram, "uProjection", projection);

            // Color based on selection
            int colorLocation = gl.GetUniformLocation(_sphereProgram, "uColor");
            if (_selectedHoleId == hole.Id)
            {
                gl.Uniform3(colorLocation, 1f, 1f, 0f); // Yellow for selected
            }
            else
            {
                gl.Uniform3(colorLocation, 0.2f, 0.8f, 0.2f); // Green for unselected
            }

            gl.BindVertexArray(_sphereVao);
            gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_sphereVertexCount);
            gl.BindVertexArray(0);
        }
    }

    public bool OnPointerPressed(GizmoPointerEvent e)
    {
        if (e.Button != GizmoPointerButton.Primary || e.Mesh is null || _spatialTree is null)
        {
            return false;
        }

        // Ray-cast to find the surface point
        Ray3d ray3d = e.Ray.ToRay3d();
        MeshRayHit? hit = MeshRaycaster.Raycast(_spatialTree, ray3d);

        if (hit is not null)
        {
            // Place a new hole at the hit location
            var newHole = new PlacedDrainHole(
                _nextHoleId++,
                hit.Value.Point,
                hit.Value.Normal,
                diameter: 2.0, // Default 2mm
                countersinkDepth: 0.0);

            _holes.Add(newHole);
            _selectedHoleId = newHole.Id;
            HolePlaced?.Invoke(this, (hit.Value.Point, hit.Value.Normal));

            _isDragging = true;
            return true;
        }

        return false;
    }

    public bool OnPointerMoved(GizmoPointerEvent e)
    {
        if (!_isDragging || _selectedHoleId is null || e.Mesh is null || _spatialTree is null)
        {
            return false;
        }

        // Optional: update hole position on move (for now, just keep it fixed)
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
        const int latSteps = 6;
        const int lonSteps = 6;

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

        fixed (float* data = vertices.ToArray())
        {
            gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Count * sizeof(float)), data, BufferUsageARB.StaticDraw);
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
