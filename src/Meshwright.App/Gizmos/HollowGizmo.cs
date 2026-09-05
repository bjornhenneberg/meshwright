using System;
using System.Numerics;
using System.Collections.Generic;
using Silk.NET.OpenGL;
using GlApi = Silk.NET.OpenGL.GL;
using g3;
using Meshwright.Geometry.Spatial;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;

namespace Meshwright.App.Gizmos;

/// <summary>
/// Visual preview of the shell Hollow will produce, and a draggable handle for wall thickness
/// (M4-8: gizmo coverage for Hollow). Wall thickness is a single scalar, but it is still a
/// spatial quantity — "how far the inner surface sits from the outer one" — so per the
/// gizmo-first direction (SPECIFICATION.md §11, 2026-09-04) the user should be able to see and
/// drag that distance in the viewport rather than only type a number.
///
/// <para>
/// Renders two markers connected by a line: <see cref="AnchorPoint"/> on the mesh's outer surface
/// (fixed), and an inner marker at <c>AnchorPoint - OutwardNormal * WallThickness</c> that the user
/// drags along the surface normal. Dragging it away from the outer surface increases wall
/// thickness; dragging it toward the outer surface decreases it. This mirrors
/// <c>TransformGizmo</c>'s Move-mode single-axis drag (<c>ClosestPointOnAxis</c>), constrained to
/// the one axis this operation actually has.
/// </para>
/// </summary>
public sealed class HollowGizmo : IViewportGizmo, IDisposable
{
    /// <summary>Minimum wall thickness the handle can be dragged to — zero or negative has no
    /// meaningful shell and would make <see cref="Meshwright.Core.Operations.HollowOperation"/> throw.</summary>
    private const float MinWallThickness = 0.01f;

    /// <summary>Handle marker radius as a fraction of viewport height (see <see cref="GizmoScale"/>
    /// — sized on screen, not in world units, so it stays grabbable at any zoom or model size).</summary>
    private const float HandleRadiusFraction = 0.035f;

    /// <summary>Render/pick scale applied to the fixed, non-interactive anchor marker —
    /// smaller than the draggable handle so the eye (and the pointer) is drawn to the part
    /// of the gizmo that actually does something.</summary>
    private const float AnchorRadiusScale = 0.5f;

    /// <summary>Render/pick scale applied to the draggable inner handle. Kept larger than the
    /// anchor so it reads as the prominent, interactive element.</summary>
    private const float HandleRadiusScale = 1.2f;

    private readonly Vector3 _anchorPoint;
    private readonly Vector3 _outwardNormal;

    private float _wallThickness;
    private bool _isDragging;

    private uint _sphereVao;
    private uint _sphereVbo;
    private uint _sphereProgram;
    private int _sphereVertexCount;

    private uint _lineVao;
    private uint _lineVbo;
    private uint _lineProgram;

    private bool _disposed;

    /// <summary>Fixed point on the mesh's outer surface the shell is measured from.</summary>
    public Vector3 AnchorPoint => _anchorPoint;

    /// <summary>Outward surface normal at <see cref="AnchorPoint"/>; the axis the handle drags along.</summary>
    public Vector3 OutwardNormal => _outwardNormal;

    /// <summary>Current wall thickness, in mesh units. Starts at the panel's textbox default (2mm)
    /// so the gizmo and textbox agree until the user touches one or the other.</summary>
    public float WallThickness => _wallThickness;

    /// <summary>Where the inner shell surface would sit for the current <see cref="WallThickness"/>.</summary>
    public Vector3 InnerPoint => _anchorPoint - _outwardNormal * _wallThickness;

    /// <summary>True once the user has dragged the handle; the gizmo's value should then win
    /// outright over the wall-thickness textbox on Apply (gizmo-first, §11 2026-09-04).</summary>
    public bool WasTouched { get; private set; }

    /// <summary>Raised whenever <see cref="WallThickness"/> changes as a result of dragging.</summary>
    public event EventHandler? Changed;

    public HollowGizmo(Vector3 anchorPoint, Vector3 outwardNormal, float initialWallThickness = 2.0f)
    {
        _anchorPoint = anchorPoint;
        _outwardNormal = outwardNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(outwardNormal) : Vector3.UnitZ;
        _wallThickness = MathF.Max(initialWallThickness, MinWallThickness);
    }

    /// <summary>
    /// Number of grid steps (per axis) sampled across the mesh's top-down XY footprint when the
    /// straight-down-the-centre ray misses. A Menger-sponge-shaped model has a hole straight
    /// through the middle of every face, so a single centre ray is not a reliable way to find
    /// "the surface" — this widens the search until it hits solid material.
    /// </summary>
    private const int AnchorSearchGridSteps = 9;

    /// <summary>
    /// Picks a surface anchor and outward normal for <paramref name="mesh"/> by casting a ray
    /// straight down onto it from above its bounding box — the same raycasting machinery
    /// <see cref="DrainHoleGizmo"/> uses for its own surface picks. Z is up in this app (see
    /// SPECIFICATION.md §11, 2026-09-05 — STL/3MF and the print bed put the build direction along
    /// +Z), so "straight down" is -Z from above the bounding box's top.
    ///
    /// <para>
    /// The centre of the bounding box is tried first, but a single ray there is not reliable: a
    /// model with a hole through the middle of its top face (a Menger sponge is the extreme case,
    /// but any part with a top-face cutout has the same problem) sends that ray straight through
    /// without hitting anything. Rather than fall back to a synthetic point floating in space — the
    /// wrong answer for an operation whose entire purpose is to show a wall on the surface — this
    /// samples further rays across the mesh's XY footprint, nearest the centre first, until one
    /// lands on actual geometry. Only a mesh with no top-down-visible surface at all (fully open,
    /// or genuinely empty) falls back to a synthetic point.
    /// </para>
    /// </summary>
    public static (Vector3 Point, Vector3 Normal) ComputeSurfaceAnchor(DMesh3? mesh)
    {
        if (mesh is null || mesh.TriangleCount == 0)
        {
            return (Vector3.Zero, Vector3.UnitZ);
        }

        AxisAlignedBox3d bounds = mesh.CachedBounds;
        Vector3d center = bounds.Center;
        double margin = Math.Max(bounds.MaxDim, 1e-6);
        double top = bounds.Max.z + margin;

        var tree = new DMeshAABBTree3(mesh, autoBuild: true);

        double halfX = Math.Max(bounds.Extents.x, 1e-6);
        double halfY = Math.Max(bounds.Extents.y, 1e-6);

        foreach ((double fx, double fy) in SampleOffsetsNearestFirst(AnchorSearchGridSteps))
        {
            var rayOrigin = new Vector3d(center.x + fx * halfX, center.y + fy * halfY, top);
            var ray = new Ray3d(rayOrigin, -Vector3d.AxisZ);

            MeshRayHit? hit = MeshRaycaster.Raycast(tree, ray);
            if (hit is null)
            {
                continue;
            }

            var point = new Vector3((float)hit.Value.Point.x, (float)hit.Value.Point.y, (float)hit.Value.Point.z);
            var normal = new Vector3((float)hit.Value.Normal.x, (float)hit.Value.Normal.y, (float)hit.Value.Normal.z);
            if (normal.LengthSquared() < 1e-12f)
            {
                normal = Vector3.UnitZ;
            }
            return (point, Vector3.Normalize(normal));
        }

        // No sample hit anything (e.g. a fully open mesh) — put the handle a sensible distance
        // above the mesh's centre so it is still visible and draggable, even though it is not
        // resting on a real surface.
        var fallback = new Vector3((float)center.x, (float)center.y, (float)top);
        return (fallback, Vector3.UnitZ);
    }

    /// <summary>
    /// A <paramref name="steps"/>-by-<paramref name="steps"/> grid of fractional XY offsets in
    /// [-0.9, 0.9], ordered nearest-to-centre first (ties broken arbitrarily). The 0.9 cap keeps
    /// samples off the very edge of the bounding box, where a ray is likely to clip past the mesh
    /// entirely rather than land on its top surface.
    /// </summary>
    private static IEnumerable<(double Fx, double Fy)> SampleOffsetsNearestFirst(int steps)
    {
        var offsets = new List<(double Fx, double Fy)>();
        for (int i = 0; i < steps; i++)
        {
            double fx = steps == 1 ? 0.0 : -0.9 + 1.8 * i / (steps - 1);
            for (int j = 0; j < steps; j++)
            {
                double fy = steps == 1 ? 0.0 : -0.9 + 1.8 * j / (steps - 1);
                offsets.Add((fx, fy));
            }
        }

        offsets.Sort((a, b) => (a.Fx * a.Fx + a.Fy * a.Fy).CompareTo(b.Fx * b.Fx + b.Fy * b.Fy));
        return offsets;
    }

    /// <summary>
    /// A wall thickness sized to the model rather than a fixed guess: a fixed 2mm default renders
    /// as a marker buried at the model's centre — sometimes past its opposite face — on a part only
    /// a few mm across, which reads as "a gizmo that does nothing". Scales with the mesh's smallest
    /// bounding dimension (the dimension a wall actually has to fit inside twice, for both shells),
    /// capped at the old 2mm default for anything not tiny and floored so it never collapses to
    /// zero on a razor-thin part.
    /// </summary>
    public static float ComputeDefaultWallThickness(DMesh3? mesh)
    {
        const float DefaultThickness = 2.0f;
        const float MinDefaultThickness = 0.1f;
        const float ThicknessFractionOfMinDim = 0.15f;

        if (mesh is null || mesh.TriangleCount == 0)
        {
            return DefaultThickness;
        }

        AxisAlignedBox3d bounds = mesh.CachedBounds;
        double minDim = Math.Min(bounds.Width, Math.Min(bounds.Height, bounds.Depth));
        float scaled = (float)(minDim * ThicknessFractionOfMinDim);
        return Math.Clamp(scaled, MinDefaultThickness, DefaultThickness);
    }

    private float HandleRadiusFor(Vector3 worldPoint, Matrix4x4 view, Matrix4x4 projection) =>
        GizmoScale.ForFractionOfHeight(worldPoint, view, projection, HandleRadiusFraction);

    public void Render(GlApi gl, Matrix4x4 view, Matrix4x4 projection)
    {
        if (_sphereProgram == 0)
        {
            BuildGeometry(gl);
        }

        Vector3 inner = InnerPoint;

        RenderLine(gl, view, projection, _anchorPoint, inner);
        // The anchor is fixed reference geometry, not something to click — kept small and muted so
        // the eye (and the pointer) goes to the orange handle that actually drags.
        RenderSphere(gl, view, projection, _anchorPoint, HandleRadiusFor(_anchorPoint, view, projection) * AnchorRadiusScale, new Vector3(0.2f, 0.6f, 0.2f));
        RenderSphere(gl, view, projection, inner, HandleRadiusFor(inner, view, projection) * HandleRadiusScale, new Vector3(1f, 0.55f, 0.05f));
    }

    public bool OnPointerPressed(GizmoPointerEvent e)
    {
        if (e.Button != GizmoPointerButton.Primary)
        {
            return false;
        }

        Vector3 inner = InnerPoint;
        float radius = HandleRadiusFor(inner, e.View, e.Projection) * HandleRadiusScale;
        float distance = DistancePointToRay(inner, e.Ray);
        if (distance > radius)
        {
            return false;
        }

        _isDragging = true;
        return true;
    }

    public bool OnPointerMoved(GizmoPointerEvent e)
    {
        if (!_isDragging)
        {
            return false;
        }

        if (!ClosestPointOnAxis(_anchorPoint, _outwardNormal, e.Ray, out Vector3 dragPoint))
        {
            return false;
        }

        // The handle sits at AnchorPoint - normal * thickness, so thickness is how far *against*
        // the outward normal the drag point has moved from the anchor.
        float signedOffset = Vector3.Dot(dragPoint - _anchorPoint, _outwardNormal);
        _wallThickness = MathF.Max(-signedOffset, MinWallThickness);

        WasTouched = true;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool OnPointerReleased(GizmoPointerEvent e)
    {
        bool wasActive = _isDragging;
        _isDragging = false;
        return wasActive;
    }

    /// <summary>Closest point on the infinite line through <paramref name="origin"/> along
    /// <paramref name="axis"/> to the given ray; false if the two are (near-)parallel. Same
    /// derivation as <c>TransformGizmo.ClosestPointOnAxis</c>.</summary>
    private static bool ClosestPointOnAxis(Vector3 origin, Vector3 axis, ViewportRay ray, out Vector3 point)
    {
        Vector3 rayDir = Vector3.Normalize(ray.Direction);
        float axisDotRay = Vector3.Dot(axis, rayDir);
        float denominator = 1f - axisDotRay * axisDotRay;
        if (denominator < 1e-6f)
        {
            point = origin;
            return false;
        }

        Vector3 toRayOrigin = ray.Origin - origin;
        float t = (Vector3.Dot(toRayOrigin, axis) - axisDotRay * Vector3.Dot(toRayOrigin, rayDir)) / denominator;
        point = origin + axis * t;
        return true;
    }

    private static float DistancePointToRay(Vector3 point, ViewportRay ray)
    {
        Vector3 rayDir = Vector3.Normalize(ray.Direction);
        Vector3 toPoint = point - ray.Origin;
        float projected = Vector3.Dot(toPoint, rayDir);
        Vector3 closestPoint = ray.Origin + rayDir * Math.Max(0, projected);
        return Vector3.Distance(point, closestPoint);
    }

    private void RenderLine(GlApi gl, Matrix4x4 view, Matrix4x4 projection, Vector3 from, Vector3 to)
    {
        gl.UseProgram(_lineProgram);
        SetMatrixUniform(gl, _lineProgram, "uModel", Matrix4x4.Identity);
        SetMatrixUniform(gl, _lineProgram, "uView", view);
        SetMatrixUniform(gl, _lineProgram, "uProjection", projection);

        int colorLocation = gl.GetUniformLocation(_lineProgram, "uColor");
        gl.Uniform3(colorLocation, 0.8f, 0.8f, 0.8f);

        var verts = new[] { from.X, from.Y, from.Z, to.X, to.Y, to.Z };
        unsafe
        {
            fixed (float* ptr = verts)
            {
                gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);
                gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)), ptr, BufferUsageARB.DynamicDraw);
            }
        }

        gl.BindVertexArray(_lineVao);
        gl.DrawArrays(PrimitiveType.Lines, 0, 2);
        gl.BindVertexArray(0);
    }

    private void RenderSphere(GlApi gl, Matrix4x4 view, Matrix4x4 projection, Vector3 center, float radius, Vector3 color)
    {
        gl.UseProgram(_sphereProgram);

        SetMatrixUniform(gl, _sphereProgram, "uModel", Matrix4x4.CreateScale(radius) * Matrix4x4.CreateTranslation(center));
        SetMatrixUniform(gl, _sphereProgram, "uView", view);
        SetMatrixUniform(gl, _sphereProgram, "uProjection", projection);

        int colorLocation = gl.GetUniformLocation(_sphereProgram, "uColor");
        gl.Uniform3(colorLocation, color.X, color.Y, color.Z);

        gl.BindVertexArray(_sphereVao);
        gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_sphereVertexCount);
        gl.BindVertexArray(0);
    }

    private unsafe void BuildGeometry(GlApi gl)
    {
        const int latSteps = 6;
        const int lonSteps = 6;

        var vertices = new System.Collections.Generic.List<float>();

        for (int lat = 0; lat < latSteps; lat++)
        {
            float lat0 = (float)(Math.PI * (lat / (float)latSteps - 0.5));
            float lat1 = (float)(Math.PI * ((lat + 1) / (float)latSteps - 0.5));

            for (int lon = 0; lon < lonSteps; lon++)
            {
                float lon0 = (float)(2f * Math.PI * (lon / (float)lonSteps));
                float lon1 = (float)(2f * Math.PI * ((lon + 1) / (float)lonSteps));

                var v0 = new Vector3((float)(Math.Cos(lon0) * Math.Cos(lat0)), (float)Math.Sin(lat0), (float)(Math.Sin(lon0) * Math.Cos(lat0)));
                var v1 = new Vector3((float)(Math.Cos(lon1) * Math.Cos(lat0)), (float)Math.Sin(lat0), (float)(Math.Sin(lon1) * Math.Cos(lat0)));
                var v2 = new Vector3((float)(Math.Cos(lon0) * Math.Cos(lat1)), (float)Math.Sin(lat1), (float)(Math.Sin(lon0) * Math.Cos(lat1)));
                var v3 = new Vector3((float)(Math.Cos(lon1) * Math.Cos(lat1)), (float)Math.Sin(lat1), (float)(Math.Sin(lon1) * Math.Cos(lat1)));

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

        _lineVao = gl.GenVertexArray();
        _lineVbo = gl.GenBuffer();
        gl.BindVertexArray(_lineVao);
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _lineVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(6 * sizeof(float)), IntPtr.Zero.ToPointer(), BufferUsageARB.DynamicDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);
        gl.BindVertexArray(0);

        _lineProgram = CompileProgram(gl);
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

        // Note: GL context may not be current here; caller must dispose from OnOpenGlDeinit,
        // same convention as the other App-layer gizmos (see DrainHoleGizmo.Dispose).
        _disposed = true;
    }
}
