using System.Numerics;
using Meshwright.Geometry.Printing;
using Silk.NET.OpenGL;

namespace Meshwright.Rendering.GL;

/// <summary>
/// Draws the printer's build plate: a grid on the Z=0 plane sized to a <see cref="BuildVolume"/>,
/// plus the bed outline, which turns amber when the model does not fit.
///
/// <para>
/// <b>The grid is deliberately unlit.</b> The mesh pass takes its key light from the view matrix
/// (§11, 2026-09-06) because a world-fixed light left half the view presets drawing a black
/// silhouette; the same reasoning does not carry over here, because a line has no surface to
/// shade. Its normal is undefined, and any value picked for one would make the grid's brightness
/// depend on the camera for no reason a user could interpret. The plate is therefore a constant
/// colour, dim enough to sit behind the model, with the minor grid at a fraction of the major
/// one's brightness.
/// </para>
/// <para>
/// <b>It renders before the mesh, depth-testing and depth-writing normally.</b> It is opaque
/// geometry sitting in the world, not an overlay: the model must occlude it where the model is in
/// front, and the plate must occlude anything that has sunk below the bed — which is precisely the
/// case the out-of-bounds warning is shouting about. No blending is enabled at any point, and the
/// line width is restored, because the context is shared with the mesh pass, the gizmo pass and
/// Avalonia itself (§11, 2026-09-06: leaving state behind is how gizmos went invisible).
/// </para>
/// </summary>
public sealed class BuildPlateRenderer : IDisposable
{
    private const string VertexShaderSource = """
        #version 330 core

        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in float aIntensity;

        uniform mat4 uView;
        uniform mat4 uProjection;

        out float vIntensity;

        void main()
        {
            vIntensity = aIntensity;
            gl_Position = uProjection * uView * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core

        in float vIntensity;
        out vec4 FragColor;

        uniform vec3 uColor;

        void main()
        {
            FragColor = vec4(uColor * vIntensity, 1.0);
        }
        """;

    private readonly Silk.NET.OpenGL.GL _gl;

    private uint _program;
    private uint _gridVao;
    private uint _gridPositionVbo;
    private uint _gridIntensityVbo;
    private int _gridVertexCount;

    private uint _outlineVao;
    private uint _outlinePositionVbo;
    private uint _outlineIntensityVbo;
    private int _outlineVertexCount;

    private BuildVolume _volume = BuildVolume.Default;
    private double _modelFootprintMm;
    private bool _geometryDirty = true;
    private bool _disposed;

    public BuildPlateRenderer(Silk.NET.OpenGL.GL gl)
    {
        _gl = gl;
    }

    /// <summary>Grid colour. Dim: the plate is a reference, not the subject.</summary>
    public Vector3 GridColor { get; set; } = new(0.42f, 0.46f, 0.52f);

    /// <summary>Bed outline colour while the model fits.</summary>
    public Vector3 OutlineColor { get; set; } = new(0.55f, 0.62f, 0.70f);

    /// <summary>Bed outline colour while any part of the model is outside the build volume.</summary>
    public Vector3 OutOfBoundsOutlineColor { get; set; } = new(1f, 0.55f, 0.1f);

    /// <summary>
    /// Whether the model currently fits. Drives the outline colour alone — the warning's words
    /// live in the UI, but a colour on the bed itself is what a user sees without reading.
    /// </summary>
    public bool ModelFits { get; set; } = true;

    /// <summary>The spacing of the bright grid lines, in mm, for the frame last built.</summary>
    public double MajorSpacingMm { get; private set; }

    /// <summary>The spacing of the dim grid lines, in mm, or null when there are none.</summary>
    public double? MinorSpacingMm { get; private set; }

    /// <summary>
    /// The bed to draw, and the model's footprint (larger of its X/Y extents) which sets the minor
    /// grid spacing. Rebuilding is deferred to the next <see cref="Render"/> so this can be called
    /// without a current GL context.
    /// </summary>
    public void SetBuildVolume(BuildVolume volume, double modelFootprintMm)
    {
        if (_volume == volume && Math.Abs(_modelFootprintMm - modelFootprintMm) < 1e-9)
        {
            return;
        }

        _volume = volume;
        _modelFootprintMm = modelFootprintMm;
        _geometryDirty = true;
    }

    public void Initialize()
    {
        _program = LinkProgram();
        _gridVao = _gl.GenVertexArray();
        _outlineVao = _gl.GenVertexArray();
    }

    public unsafe void Render(Matrix4x4 view, Matrix4x4 projection)
    {
        if (_geometryDirty)
        {
            UploadGeometry();
            _geometryDirty = false;
        }

        if (_gridVertexCount == 0 && _outlineVertexCount == 0)
        {
            return;
        }

        _gl.UseProgram(_program);
        SetMatrixUniform("uView", view);
        SetMatrixUniform("uProjection", projection);

        int colorLocation = _gl.GetUniformLocation(_program, "uColor");

        _gl.LineWidth(1f);
        _gl.Uniform3(colorLocation, GridColor.X, GridColor.Y, GridColor.Z);
        _gl.BindVertexArray(_gridVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);

        Vector3 outlineColor = ModelFits ? OutlineColor : OutOfBoundsOutlineColor;
        _gl.LineWidth(2f);
        _gl.Uniform3(colorLocation, outlineColor.X, outlineColor.Y, outlineColor.Z);
        _gl.BindVertexArray(_outlineVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_outlineVertexCount);

        _gl.BindVertexArray(0);
        _gl.LineWidth(1f);
    }

    private unsafe void UploadGeometry()
    {
        BuildPlateGridLines lines = BuildPlateGrid.Build(_volume, _modelFootprintMm);
        MajorSpacingMm = lines.MajorSpacingMm;
        MinorSpacingMm = lines.MinorSpacingMm;

        _gridVertexCount = lines.Positions.Length / 3;
        _outlineVertexCount = lines.OutlinePositions.Length / 3;

        UploadLineBuffers(_gridVao, ref _gridPositionVbo, ref _gridIntensityVbo, lines.Positions, lines.Intensities);

        var outlineIntensities = new float[_outlineVertexCount];
        Array.Fill(outlineIntensities, 1f);
        UploadLineBuffers(_outlineVao, ref _outlinePositionVbo, ref _outlineIntensityVbo, lines.OutlinePositions, outlineIntensities);
    }

    private unsafe void UploadLineBuffers(uint vao, ref uint positionVbo, ref uint intensityVbo, float[] positions, float[] intensities)
    {
        _gl.BindVertexArray(vao);

        if (positionVbo == 0)
        {
            positionVbo = _gl.GenBuffer();
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, positionVbo);
        fixed (float* data = positions)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(positions.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), null);

        if (intensityVbo == 0)
        {
            intensityVbo = _gl.GenBuffer();
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, intensityVbo);
        fixed (float* data = intensities)
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(intensities.Length * sizeof(float)), data, BufferUsageARB.StaticDraw);
        }

        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 1, VertexAttribPointerType.Float, false, sizeof(float), null);

        _gl.BindVertexArray(0);
    }

    private unsafe void SetMatrixUniform(string name, Matrix4x4 matrix)
    {
        int location = _gl.GetUniformLocation(_program, name);
        _gl.UniformMatrix4(location, 1, false, (float*)&matrix);
    }

    private uint LinkProgram()
    {
        uint vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vertexShader);
        _gl.AttachShader(program, fragmentShader);
        _gl.LinkProgram(program);

        _gl.GetProgram(program, GLEnum.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            throw new InvalidOperationException($"Build plate shader link failed: {_gl.GetProgramInfoLog(program)}");
        }

        _gl.DetachShader(program, vertexShader);
        _gl.DetachShader(program, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        return program;
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
            throw new InvalidOperationException($"Build plate {type} compilation failed: {log}");
        }

        return shader;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (uint buffer in new[] { _gridPositionVbo, _gridIntensityVbo, _outlinePositionVbo, _outlineIntensityVbo })
        {
            if (buffer != 0)
            {
                _gl.DeleteBuffer(buffer);
            }
        }

        foreach (uint vao in new[] { _gridVao, _outlineVao })
        {
            if (vao != 0)
            {
                _gl.DeleteVertexArray(vao);
            }
        }

        if (_program != 0)
        {
            _gl.DeleteProgram(_program);
        }

        _disposed = true;
    }
}
