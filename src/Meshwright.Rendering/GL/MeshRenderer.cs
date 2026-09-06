using System.Numerics;
using Silk.NET.OpenGL;

namespace Meshwright.Rendering.GL;

/// <summary>
/// Renders an indexed <see cref="g3.DMesh3"/> with flat Lambertian shading using an existing,
/// already-current <see cref="Silk.NET.OpenGL.GL"/> instance (e.g. supplied by Avalonia's
/// OpenGlControlBase). Does not create a window, context, or GL loader itself.
/// Triangles flagged via <see cref="UploadMesh"/> render blended with <see cref="HighlightColor"/>,
/// and flagged edges are drawn as an emphasized wireframe overlay in <see cref="EdgeHighlightColor"/>.
/// </summary>
public sealed class MeshRenderer : IDisposable
{
    private const string VertexShaderSource = """
        #version 330 core

        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in float aHighlight;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vNormal;
        out float vHighlight;

        void main()
        {
            vNormal = mat3(uModel) * aNormal;
            vHighlight = aHighlight;
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core

        in vec3 vNormal;
        in float vHighlight;
        out vec4 FragColor;

        uniform vec3 uLightDirection;
        uniform vec3 uBaseColor;
        uniform vec3 uHighlightColor;

        // Display mode (see MeshDisplayMode): uAlpha < 1 makes the surface see-through for x-ray,
        // and uUnlit = 1 drops the diffuse term so wireframe lines read as lines rather than as
        // strands shaded by the normal of whichever triangle they happen to bound.
        uniform float uAlpha;
        uniform float uUnlit;

        void main()
        {
            vec3 normal = normalize(vNormal);
            float diffuse = max(dot(normal, normalize(-uLightDirection)), 0.0);
            vec3 baseColor = mix(uBaseColor, uHighlightColor, vHighlight);
            vec3 color = mix(baseColor * (0.2 + 0.8 * diffuse), baseColor, uUnlit);
            FragColor = vec4(color, uAlpha);
        }
        """;

    private const string EdgeVertexShaderSource = """
        #version 330 core

        layout(location = 0) in vec3 aPosition;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        void main()
        {
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string EdgeFragmentShaderSource = """
        #version 330 core

        out vec4 FragColor;
        uniform vec3 uEdgeColor;

        void main()
        {
            FragColor = vec4(uEdgeColor, 1.0);
        }
        """;

    private readonly Silk.NET.OpenGL.GL _gl;

    private uint _program;
    private uint _vao;
    private uint _positionVbo;
    private uint _normalVbo;
    private uint _highlightVbo;
    private int _vertexCount;

    private uint _edgeProgram;
    private uint _edgeVao;
    private uint _edgeVbo;
    private int _edgeVertexCount;

    private bool _disposed;

    /// <summary>
    /// World-space direction the light travels, used when <see cref="UseHeadlight"/> is false.
    /// </summary>
    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f));

    /// <summary>
    /// Light with the camera rather than from a fixed world direction. A world-fixed light makes
    /// the view presets unusable: with the light coming from -Y, the Front, Left and Bottom views
    /// look straight at the model's unlit side and render it near-black at the ambient floor. A
    /// headlight is what CAD and slicer viewers use for exactly this reason - every view is lit,
    /// and the key is offset off-axis so the shading still describes the shape instead of
    /// flattening it.
    /// </summary>
    public bool UseHeadlight { get; set; } = true;
    public Vector3 BaseColor { get; set; } = new(0.7f, 0.7f, 0.75f);
    public Vector3 HighlightColor { get; set; } = new(0.95f, 0.15f, 0.1f);
    public Vector3 EdgeHighlightColor { get; set; } = new(1f, 0.85f, 0.1f);
    public float EdgeHighlightLineWidth { get; set; } = 3f;

    /// <summary>How the surface is drawn: shaded, wireframe or x-ray. See <see cref="MeshDisplayMode"/>.</summary>
    public MeshDisplayMode DisplayMode { get; set; } = MeshDisplayMode.Shaded;

    /// <summary>Surface opacity used by <see cref="MeshDisplayMode.XRay"/>.</summary>
    public float XRayAlpha { get; set; } = 0.35f;

    public MeshRenderer(Silk.NET.OpenGL.GL gl)
    {
        _gl = gl;
    }

    /// <summary>Compiles the shader programs and creates the VAOs. Must be called with a current GL context.</summary>
    public unsafe void Initialize()
    {
        _program = LinkProgram(VertexShaderSource, FragmentShaderSource);
        _vao = _gl.GenVertexArray();

        _edgeProgram = LinkProgram(EdgeVertexShaderSource, EdgeFragmentShaderSource);
        _edgeVao = _gl.GenVertexArray();
    }

    private uint LinkProgram(string vertexSource, string fragmentSource)
    {
        uint vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, GLEnum.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string log = _gl.GetProgramInfoLog(program);
            throw new InvalidOperationException($"Shader program link failed: {log}");
        }

        _gl.DetachShader(program, vertexShader);
        _gl.DetachShader(program, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        return program;
    }

    /// <summary>
    /// Uploads mesh position/normal/highlight data into VBOs bound to the VAO, plus a line list
    /// for any flagged edges. <paramref name="flaggedTriangleIds"/> and <paramref name="flaggedEdges"/>
    /// default to none, in which case rendering is identical to the unflagged (pre-diagnostics) path.
    /// </summary>
    public unsafe void UploadMesh(
        g3.DMesh3 mesh,
        IReadOnlyCollection<int>? flaggedTriangleIds = null,
        IReadOnlyList<g3.Index2i>? flaggedEdges = null)
    {
        float[] positions = VertexDataBuilder.Flatten(VertexDataBuilder.BuildPositions(mesh));
        float[] normals = VertexDataBuilder.Flatten(VertexDataBuilder.BuildPerVertexNormals(mesh));
        float[] highlights = VertexDataBuilder.BuildTriangleHighlightFlags(mesh, flaggedTriangleIds ?? Array.Empty<int>());
        _vertexCount = positions.Length / 3;

        _gl.BindVertexArray(_vao);

        if (_positionVbo == 0)
        {
            _positionVbo = _gl.GenBuffer();
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _positionVbo);
        fixed (float* data = positions)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(positions.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);

        if (_normalVbo == 0)
        {
            _normalVbo = _gl.GenBuffer();
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _normalVbo);
        fixed (float* data = normals)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(normals.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);

        if (_highlightVbo == 0)
        {
            _highlightVbo = _gl.GenBuffer();
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _highlightVbo);
        fixed (float* data = highlights)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(highlights.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 1, VertexAttribPointerType.Float, false, sizeof(float), null);

        _gl.BindVertexArray(0);

        UploadEdgeHighlights(mesh, flaggedEdges ?? Array.Empty<g3.Index2i>());
    }

    private unsafe void UploadEdgeHighlights(g3.DMesh3 mesh, IReadOnlyList<g3.Index2i> flaggedEdges)
    {
        float[] edgePositions = VertexDataBuilder.Flatten(VertexDataBuilder.BuildEdgeLinePositions(mesh, flaggedEdges));
        _edgeVertexCount = edgePositions.Length / 3;

        _gl.BindVertexArray(_edgeVao);

        if (_edgeVbo == 0)
        {
            _edgeVbo = _gl.GenBuffer();
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _edgeVbo);
        if (edgePositions.Length > 0)
        {
            fixed (float* data = edgePositions)
            {
                _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(edgePositions.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
            }
        }
        else
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, 0, null, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);

        _gl.BindVertexArray(0);
    }

    /// <summary>Binds the program/VAO, sets the view/projection/model uniforms, and draws the uploaded mesh
    /// plus any flagged-edge overlay.</summary>
    public unsafe void Render(Matrix4x4 view, Matrix4x4 projection, Matrix4x4 model)
    {
        if (_vertexCount == 0)
        {
            return;
        }

        _gl.UseProgram(_program);
        SetMatrixUniform(_program, "uModel", model);
        SetMatrixUniform(_program, "uView", view);
        SetMatrixUniform(_program, "uProjection", projection);

        Vector3 lightDirection = UseHeadlight ? HeadlightDirection(view) : LightDirection;
        int lightLocation = _gl.GetUniformLocation(_program, "uLightDirection");
        _gl.Uniform3(lightLocation, lightDirection.X, lightDirection.Y, lightDirection.Z);

        int colorLocation = _gl.GetUniformLocation(_program, "uBaseColor");
        _gl.Uniform3(colorLocation, BaseColor.X, BaseColor.Y, BaseColor.Z);

        int highlightColorLocation = _gl.GetUniformLocation(_program, "uHighlightColor");
        _gl.Uniform3(highlightColorLocation, HighlightColor.X, HighlightColor.Y, HighlightColor.Z);

        _gl.Uniform1(_gl.GetUniformLocation(_program, "uAlpha"), DisplayMode == MeshDisplayMode.XRay ? XRayAlpha : 1f);
        _gl.Uniform1(_gl.GetUniformLocation(_program, "uUnlit"), DisplayMode == MeshDisplayMode.Wireframe ? 1f : 0f);

        ApplyDisplayModeState();

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);

        RestoreDefaultState();

        if (_edgeVertexCount > 0)
        {
            _gl.UseProgram(_edgeProgram);
            SetMatrixUniform(_edgeProgram, "uModel", model);
            SetMatrixUniform(_edgeProgram, "uView", view);
            SetMatrixUniform(_edgeProgram, "uProjection", projection);

            int edgeColorLocation = _gl.GetUniformLocation(_edgeProgram, "uEdgeColor");
            _gl.Uniform3(edgeColorLocation, EdgeHighlightColor.X, EdgeHighlightColor.Y, EdgeHighlightColor.Z);

            _gl.LineWidth(EdgeHighlightLineWidth);
            _gl.BindVertexArray(_edgeVao);
            _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_edgeVertexCount);
            _gl.BindVertexArray(0);
        }
    }

    /// <summary>
    /// The world-space light direction for a key light sitting just over the viewer's left
    /// shoulder, derived from the camera's own axes so it follows every orbit and every preset.
    /// </summary>
    private static Vector3 HeadlightDirection(Matrix4x4 view)
    {
        // CreateLookAt's row-vector result holds the camera basis in its columns: right in the
        // first, up in the second, and *backward* (the camera looks down -z) in the third.
        var right = new Vector3(view.M11, view.M21, view.M31);
        var up = new Vector3(view.M12, view.M22, view.M32);
        var forward = -new Vector3(view.M13, view.M23, view.M33);

        Vector3 direction = forward + (0.35f * right) - (0.5f * up);
        return direction.LengthSquared() < 1e-12f ? forward : Vector3.Normalize(direction);
    }

    /// <summary>
    /// Sets the GL state the current <see cref="DisplayMode"/> needs for the surface pass.
    /// Always paired with <see cref="RestoreDefaultState"/>: the caller's context is shared with
    /// the gizmo pass and with Avalonia itself, and leaving polygon mode or blending switched on
    /// is exactly how gizmos once became invisible (§11, 2026-09-06).
    /// </summary>
    private void ApplyDisplayModeState()
    {
        switch (DisplayMode)
        {
            case MeshDisplayMode.Wireframe:
                _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                _gl.LineWidth(1f);
                break;

            case MeshDisplayMode.XRay:
                // Depth writes off, not the depth test: the surface must not occlude the geometry
                // behind it (that is the whole point), but it must still be occluded by anything
                // already in the depth buffer.
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                _gl.DepthMask(false);
                break;
        }
    }

    /// <summary>Returns the state touched by <see cref="ApplyDisplayModeState"/> to the viewport's
    /// baseline: filled polygons, no blending, depth writes on.</summary>
    private void RestoreDefaultState()
    {
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(true);
    }

    private unsafe void SetMatrixUniform(uint program, string name, Matrix4x4 matrix)
    {
        int location = _gl.GetUniformLocation(program, name);
        _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
    }

    private uint CompileShader(ShaderType type, string source)
    {
        uint shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);

        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compileStatus);
        if (compileStatus == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"{type} compilation failed: {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_positionVbo != 0)
        {
            _gl.DeleteBuffer(_positionVbo);
        }

        if (_normalVbo != 0)
        {
            _gl.DeleteBuffer(_normalVbo);
        }

        if (_highlightVbo != 0)
        {
            _gl.DeleteBuffer(_highlightVbo);
        }

        if (_vao != 0)
        {
            _gl.DeleteVertexArray(_vao);
        }

        if (_program != 0)
        {
            _gl.DeleteProgram(_program);
        }

        if (_edgeVbo != 0)
        {
            _gl.DeleteBuffer(_edgeVbo);
        }

        if (_edgeVao != 0)
        {
            _gl.DeleteVertexArray(_edgeVao);
        }

        if (_edgeProgram != 0)
        {
            _gl.DeleteProgram(_edgeProgram);
        }

        _disposed = true;
    }
}
