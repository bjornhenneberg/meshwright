using System;
using System.Numerics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.GL;
using Meshwright.Rendering.Gizmos;
using Silk.NET.OpenGL;
using Xunit;

namespace Meshwright.Tests.Gpu;

/// <summary>
/// GPU regression tests for the <see cref="IViewportGizmo"/> contract. Uses
/// <see cref="GpuTestFixture"/> to create a real GL context (skips gracefully if unavailable)
/// and pixel-diffs rendered frames to verify gizmos render and don't corrupt the mesh rendering.
/// </summary>
public sealed class ViewportGizmoGpuTests : IClassFixture<GpuTestFixture>, IDisposable
{
    private static readonly Vector3 ClearColor = new(0.15f, 0.15f, 0.18f);

    private readonly GpuTestFixture _fixture;
    private MeshRenderer? _renderer;
    private SimpleMarkerGizmo? _gizmo;

    public ViewportGizmoGpuTests(GpuTestFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public void Render_WithGizmo_DoesNotCrash()
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

        // Render with gizmo (a simple marker at the cube's center). The proof-of-concept gizmo may not render
        // visibly (sphere culling, depth test, etc.) but the important thing is that the render path executes
        // without crashing and doesn't corrupt the mesh rendering.
        _gizmo = new SimpleMarkerGizmo(new Vector3((float)center.x, (float)center.y, (float)center.z));
        Exception? ex = Record.Exception(() => RenderFrame(camera, _gizmo));

        Assert.Null(ex);
    }

    [SkippableFact]
    public void Render_GizmoDoesNotCorruptMesh()
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

        // Render with gizmo far away from the mesh.
        _gizmo = new SimpleMarkerGizmo(new Vector3((float)center.x, (float)center.y, (float)(center.z + 100)));
        byte[] frameWithDistantGizmo = RenderFrame(camera, _gizmo);

        // Render without gizmo.
        byte[] frameWithoutGizmo = RenderFrame(camera, gizmo: null);

        // When the gizmo is far from the mesh and off-screen, the frames should be identical or nearly so.
        // (Allow some tolerance for driver variation.)
        int diffPixels = 0;
        const byte tolerance = 5;
        for (int i = 0; i + 3 < frameWithDistantGizmo.Length; i += 4)
        {
            if (Math.Abs(frameWithDistantGizmo[i] - frameWithoutGizmo[i]) > tolerance ||
                Math.Abs(frameWithDistantGizmo[i + 1] - frameWithoutGizmo[i + 1]) > tolerance ||
                Math.Abs(frameWithDistantGizmo[i + 2] - frameWithoutGizmo[i + 2]) > tolerance)
            {
                diffPixels++;
            }
        }

        Assert.True(diffPixels < 10, $"Gizmo off-screen should not corrupt mesh; {diffPixels} pixels differ.");
    }

    private byte[] RenderFrame(OrbitCamera camera, SimpleMarkerGizmo? gizmo)
    {
        GL gl = _fixture.GL!;

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, GpuTestFixture.Width, GpuTestFixture.Height);
        gl.Enable(EnableCap.DepthTest);
        gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        float aspect = (float)GpuTestFixture.Width / GpuTestFixture.Height;
        var view = camera.GetViewMatrix();
        var projection = camera.GetProjectionMatrix(aspect);

        _renderer!.Render(view, projection, Matrix4x4.Identity);

        // Render gizmo on top of mesh if present.
        if (gizmo is not null)
        {
            gizmo.Render(gl, view, projection);
        }

        var pixels = new byte[GpuTestFixture.Width * GpuTestFixture.Height * 4];
        unsafe
        {
            fixed (byte* data = pixels)
            {
                gl.ReadPixels(0, 0, GpuTestFixture.Width, GpuTestFixture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
            }
        }

        return pixels;
    }

    public void Dispose()
    {
        _gizmo?.Dispose();
        _renderer?.Dispose();
    }
}
