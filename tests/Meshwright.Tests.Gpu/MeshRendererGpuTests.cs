using System;
using System.Numerics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.GL;
using Silk.NET.OpenGL;
using Xunit;

namespace Meshwright.Tests.Gpu;

/// <summary>
/// Regression tests for <see cref="MeshRenderer"/> against a real GPU/driver (not Avalonia
/// headless, which never registers a real <c>IPlatformGraphics</c>). Skips gracefully via
/// <see cref="SkippableFact"/> if no real GL context is available on this machine.
/// </summary>
public sealed class MeshRendererGpuTests : IClassFixture<GpuTestFixture>, IDisposable
{
    private static readonly Vector3 ClearColor = new(0.15f, 0.15f, 0.18f);

    private readonly GpuTestFixture _fixture;
    private MeshRenderer? _renderer;

    public MeshRendererGpuTests(GpuTestFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public void Initialize_CompilesAndLinksShadersOnRealDriver()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");

        _renderer = new MeshRenderer(_fixture.GL!);

        Exception? exception = Record.Exception(() => _renderer.Initialize());

        Assert.Null(exception);
    }

    [SkippableFact]
    public void UploadMesh_SwappingMeshChangesRenderedPixels()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");

        _renderer = new MeshRenderer(_fixture.GL!);
        _renderer.Initialize();

        var camera = new OrbitCamera();

        TriangleMeshFixtures.SingleTriangle single = TriangleMeshFixtures.BuildSingleTriangle();
        _renderer.UploadMesh(single.Mesh);
        (Vector3 singleCenter, float singleRadius) = single.Mesh.GetBounds();
        camera.Frame(singleCenter, singleRadius);
        byte[] firstFrame = RenderAndReadPixels(camera);

        TriangleMeshFixtures.Cube cube = TriangleMeshFixtures.BuildCube();
        _renderer.UploadMesh(cube.Mesh);
        (Vector3 cubeCenter, float cubeRadius) = cube.Mesh.GetBounds();
        camera.Frame(cubeCenter, cubeRadius);
        byte[] secondFrame = RenderAndReadPixels(camera);

        Assert.False(firstFrame.AsSpan().SequenceEqual(secondFrame), "Switching meshes did not change the rendered output.");
    }

    [SkippableFact]
    public void Render_AtMaxDistance_MeshIsStillAtLeastPartiallyVisible()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");

        _renderer = new MeshRenderer(_fixture.GL!);
        _renderer.Initialize();

        TriangleMeshFixtures.Cube cube = TriangleMeshFixtures.BuildCube();
        _renderer.UploadMesh(cube.Mesh);

        var camera = new OrbitCamera();
        (Vector3 center, float radius) = cube.Mesh.GetBounds();
        camera.Frame(center, radius);

        // Frame() sets MaxDistance to ~200x the mesh radius, at which the mesh would subtend
        // well under a pixel at the default FOV and 64x64 resolution regardless of clipping.
        // Narrow the FOV (pure zoom-lens effect, independent of the Distance/FarPlane relationship
        // under test) so the mesh stays visibly sized once pushed out to MaxDistance.
        camera.FovRadians = MathF.PI / 36f;
        camera.Zoom(camera.MaxDistance);
        Assert.Equal(camera.MaxDistance, camera.Distance);

        byte[] pixels = RenderAndReadPixels(camera);

        bool anyNonBackgroundPixel = false;
        byte clearR = ToByte(ClearColor.X);
        byte clearG = ToByte(ClearColor.Y);
        byte clearB = ToByte(ClearColor.Z);

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (Math.Abs(pixels[i] - clearR) > 2 || Math.Abs(pixels[i + 1] - clearG) > 2 || Math.Abs(pixels[i + 2] - clearB) > 2)
            {
                anyNonBackgroundPixel = true;
                break;
            }
        }

        Assert.True(anyNonBackgroundPixel, "The mesh was fully clipped out of view at MaxDistance (far-plane regression).");
    }

    private unsafe byte[] RenderAndReadPixels(OrbitCamera camera)
    {
        GL gl = _fixture.GL!;

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, GpuTestFixture.Width, GpuTestFixture.Height);
        gl.Enable(EnableCap.DepthTest);
        gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        float aspect = (float)GpuTestFixture.Width / GpuTestFixture.Height;
        _renderer!.Render(camera.GetViewMatrix(), camera.GetProjectionMatrix(aspect), Matrix4x4.Identity);

        var pixels = new byte[GpuTestFixture.Width * GpuTestFixture.Height * 4];
        fixed (byte* data = pixels)
        {
            gl.ReadPixels(0, 0, GpuTestFixture.Width, GpuTestFixture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        return pixels;
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp(channel * 255f, 0f, 255f);

    public void Dispose()
    {
        _renderer?.Dispose();
    }
}
