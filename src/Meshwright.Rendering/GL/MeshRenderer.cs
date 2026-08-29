using System.Numerics;
using Meshwright.Geometry;
using Silk.NET.OpenGL;

namespace Meshwright.Rendering.GL;

/// <summary>
/// Renders a <see cref="TriangleMesh"/> with flat Lambertian shading using an existing,
/// already-current <see cref="Silk.NET.OpenGL.GL"/> instance (e.g. supplied by Avalonia's
/// OpenGlControlBase). Does not create a window, context, or GL loader itself.
/// </summary>
public sealed class MeshRenderer : IDisposable
{
    private const string VertexShaderSource = """
        #version 330 core

        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;

        uniform mat4 uModel;
        uniform mat4 uView;
        uniform mat4 uProjection;

        out vec3 vNormal;

        void main()
        {
            vNormal = mat3(uModel) * aNormal;
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core

        in vec3 vNormal;
        out vec4 FragColor;

        uniform vec3 uLightDirection;
        uniform vec3 uBaseColor;

        void main()
        {
            vec3 normal = normalize(vNormal);
            float diffuse = max(dot(normal, normalize(-uLightDirection)), 0.0);
            vec3 color = uBaseColor * (0.2 + 0.8 * diffuse);
            FragColor = vec4(color, 1.0);
        }
        """;

    private readonly Silk.NET.OpenGL.GL _gl;

    private uint _program;
    private uint _vao;
    private uint _positionVbo;
    private uint _normalVbo;
    private int _vertexCount;
    private bool _disposed;

    public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(-0.5f, -1f, -0.3f));
    public Vector3 BaseColor { get; set; } = new(0.7f, 0.7f, 0.75f);

    public MeshRenderer(Silk.NET.OpenGL.GL gl)
    {
        _gl = gl;
    }

    /// <summary>Compiles the shader program and creates the VAO. Must be called with a current GL context.</summary>
    public unsafe void Initialize()
    {
        uint vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        uint fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);

        _program = _gl.CreateProgram();
        _gl.AttachShader(_program, vertexShader);
        _gl.AttachShader(_program, fragmentShader);
        _gl.LinkProgram(_program);

        _gl.GetProgram(_program, GLEnum.LinkStatus, out int linkStatus);
        if (linkStatus == 0)
        {
            string log = _gl.GetProgramInfoLog(_program);
            throw new InvalidOperationException($"Shader program link failed: {log}");
        }

        _gl.DetachShader(_program, vertexShader);
        _gl.DetachShader(_program, fragmentShader);
        _gl.DeleteShader(vertexShader);
        _gl.DeleteShader(fragmentShader);

        _vao = _gl.GenVertexArray();
    }

    /// <summary>Uploads mesh position/normal data into VBOs bound to the VAO.</summary>
    public unsafe void UploadMesh(TriangleMesh mesh)
    {
        float[] positions = VertexDataBuilder.Flatten(VertexDataBuilder.BuildPositions(mesh));
        float[] normals = VertexDataBuilder.Flatten(VertexDataBuilder.BuildPerVertexNormals(mesh));
        _vertexCount = mesh.Positions.Length;

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

        _gl.BindVertexArray(0);
    }

    /// <summary>Binds the program/VAO, sets the view/projection/model uniforms, and draws the uploaded mesh.</summary>
    public unsafe void Render(Matrix4x4 view, Matrix4x4 projection, Matrix4x4 model)
    {
        if (_vertexCount == 0)
        {
            return;
        }

        _gl.UseProgram(_program);
        SetMatrixUniform("uModel", model);
        SetMatrixUniform("uView", view);
        SetMatrixUniform("uProjection", projection);

        int lightLocation = _gl.GetUniformLocation(_program, "uLightDirection");
        _gl.Uniform3(lightLocation, LightDirection.X, LightDirection.Y, LightDirection.Z);

        int colorLocation = _gl.GetUniformLocation(_program, "uBaseColor");
        _gl.Uniform3(colorLocation, BaseColor.X, BaseColor.Y, BaseColor.Z);

        _gl.BindVertexArray(_vao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_vertexCount);
        _gl.BindVertexArray(0);
    }

    private unsafe void SetMatrixUniform(string name, Matrix4x4 matrix)
    {
        int location = _gl.GetUniformLocation(_program, name);
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

        if (_vao != 0)
        {
            _gl.DeleteVertexArray(_vao);
        }

        if (_program != 0)
        {
            _gl.DeleteProgram(_program);
        }

        _disposed = true;
    }
}
