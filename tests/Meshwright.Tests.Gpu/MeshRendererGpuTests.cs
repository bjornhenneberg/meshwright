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
        g3.AxisAlignedBox3d singleBounds = single.Mesh.CachedBounds;
        g3.Vector3d singleCenter = singleBounds.Center;
        double singleRadius = singleBounds.DiagonalLength / 2.0;
        camera.Frame(new Vector3((float)singleCenter.x, (float)singleCenter.y, (float)singleCenter.z), (float)singleRadius);
        byte[] firstFrame = RenderAndReadPixels(camera);

        TriangleMeshFixtures.Cube cube = TriangleMeshFixtures.BuildCube();
        _renderer.UploadMesh(cube.Mesh);
        g3.AxisAlignedBox3d cubeBounds = cube.Mesh.CachedBounds;
        g3.Vector3d cubeCenter = cubeBounds.Center;
        double cubeRadius = cubeBounds.DiagonalLength / 2.0;
        camera.Frame(new Vector3((float)cubeCenter.x, (float)cubeCenter.y, (float)cubeCenter.z), (float)cubeRadius);
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
        g3.AxisAlignedBox3d bounds = cube.Mesh.CachedBounds;
        g3.Vector3d center = bounds.Center;
        double radius = bounds.DiagonalLength / 2.0;
        camera.Frame(new Vector3((float)center.x, (float)center.y, (float)center.z), (float)radius);

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

    [SkippableFact]
    public void UploadMesh_WithFlaggedTriangle_ChangesRenderedPixelsVersusUnflagged()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");

        _renderer = new MeshRenderer(_fixture.GL!);
        _renderer.Initialize();

        var camera = new OrbitCamera();
        TriangleMeshFixtures.Cube cube = TriangleMeshFixtures.BuildCube();
        g3.AxisAlignedBox3d bounds = cube.Mesh.CachedBounds;
        g3.Vector3d center = bounds.Center;
        double radius = bounds.DiagonalLength / 2.0;
        camera.Frame(new Vector3((float)center.x, (float)center.y, (float)center.z), (float)radius);

        _renderer.UploadMesh(cube.Mesh);
        byte[] unflaggedFrame = RenderAndReadPixels(camera);

        // Triangle 2 is on the +z "front" face, which faces the default camera (yaw=45°,
        // pitch=30°); triangle 0 is on the occluded -z "back" face and wouldn't show any
        // visible pixel difference regardless of highlighting.
        _renderer.UploadMesh(cube.Mesh, flaggedTriangleIds: new[] { 2 });
        byte[] flaggedFrame = RenderAndReadPixels(camera);

        Assert.False(
            unflaggedFrame.AsSpan().SequenceEqual(flaggedFrame),
            "Flagging a triangle for highlighting did not change the rendered output.");
    }

    [SkippableFact]
    public void UploadMesh_WithFlaggedEdge_ChangesRenderedPixelsVersusUnflagged()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");

        _renderer = new MeshRenderer(_fixture.GL!);
        _renderer.Initialize();

        var camera = new OrbitCamera();
        TriangleMeshFixtures.Cube cube = TriangleMeshFixtures.BuildCube();
        g3.AxisAlignedBox3d bounds = cube.Mesh.CachedBounds;
        g3.Vector3d center = bounds.Center;
        double radius = bounds.DiagonalLength / 2.0;
        camera.Frame(new Vector3((float)center.x, (float)center.y, (float)center.z), (float)radius);

        // Triangle 2 is on the +z "front" face, which faces the default camera; an edge on
        // the occluded -z "back" face wouldn't show any visible pixel difference.
        g3.Index3i frontTriangle = cube.Mesh.GetTriangle(2);
        var flaggedEdges = new[] { new g3.Index2i(frontTriangle.a, frontTriangle.b) };

        _renderer.UploadMesh(cube.Mesh);
        byte[] unflaggedFrame = RenderAndReadPixels(camera);

        _renderer.UploadMesh(cube.Mesh, flaggedEdges: flaggedEdges);
        byte[] flaggedFrame = RenderAndReadPixels(camera);

        Assert.False(
            unflaggedFrame.AsSpan().SequenceEqual(flaggedFrame),
            "Flagging an edge for highlighting did not change the rendered output.");
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
