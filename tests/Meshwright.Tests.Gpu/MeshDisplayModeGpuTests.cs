using System;
using System.Numerics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.GL;
using Silk.NET.OpenGL;
using Xunit;

namespace Meshwright.Tests.Gpu;

/// <summary>
/// The display modes and the orthographic projection, measured in pixels off a real driver.
///
/// <para>
/// A flag assertion cannot tell whether wireframe draws wires or x-ray sees through anything; only
/// the framebuffer can. Each test here states the property in terms of what appears on screen:
/// wireframe hollows the surface out, x-ray reveals geometry that shaded rendering hides, and an
/// orthographic camera draws the same object the same size at two different depths.
/// </para>
/// </summary>
public sealed class MeshDisplayModeGpuTests : IClassFixture<GpuTestFixture>, IDisposable
{
    private static readonly Vector3 ClearColor = new(0.15f, 0.15f, 0.18f);

    private readonly GpuTestFixture _fixture;
    private MeshRenderer? _renderer;

    public MeshDisplayModeGpuTests(GpuTestFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public void Wireframe_DrawsEdgesAndHollowsOutTheSurface()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        OrbitCamera camera = FramedOn(TriangleMeshFixtures.BuildCube().Mesh, renderer);

        renderer.DisplayMode = MeshDisplayMode.Shaded;
        int shadedCoverage = CountNonBackground(RenderAndReadPixels(camera));

        renderer.DisplayMode = MeshDisplayMode.Wireframe;
        int wireframeCoverage = CountNonBackground(RenderAndReadPixels(camera));

        Assert.True(shadedCoverage > 0, "The shaded control frame drew nothing at all.");
        Assert.True(wireframeCoverage > 0, "Wireframe drew nothing at all.");

        // The defining difference: the fill is gone, so the mesh covers markedly fewer pixels
        // while still occupying the same outline.
        Assert.True(
            wireframeCoverage < shadedCoverage / 2,
            $"Wireframe covered {wireframeCoverage} of {shadedCoverage} shaded pixels - the interior is still filled.");
    }

    [SkippableFact]
    public void XRay_RevealsGeometryHiddenInsideTheModel_WhichShadedRenderingDoesNot()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();

        g3.DMesh3 cube = TriangleMeshFixtures.BuildCube().Mesh;
        g3.DMesh3 cubeWithInnerTriangle = TriangleMeshFixtures.BuildCube().Mesh;
        AppendInteriorTriangle(cubeWithInnerTriangle);

        OrbitCamera camera = FramedOn(cube, renderer);

        // Shaded: the enclosed triangle is behind an opaque wall and must make no difference.
        renderer.DisplayMode = MeshDisplayMode.Shaded;
        renderer.UploadMesh(cube);
        byte[] shadedPlain = RenderAndReadPixels(camera);
        renderer.UploadMesh(cubeWithInnerTriangle);
        byte[] shadedWithInner = RenderAndReadPixels(camera);
        Assert.True(
            shadedPlain.AsSpan().SequenceEqual(shadedWithInner),
            "Shaded rendering showed geometry sealed inside the model - the depth test is not doing its job, "
            + "which would make the x-ray half of this test meaningless.");

        // X-ray: the same enclosed triangle must now show through.
        renderer.DisplayMode = MeshDisplayMode.XRay;
        renderer.UploadMesh(cube);
        byte[] xrayPlain = RenderAndReadPixels(camera);
        renderer.UploadMesh(cubeWithInnerTriangle);
        byte[] xrayWithInner = RenderAndReadPixels(camera);

        Assert.False(
            xrayPlain.AsSpan().SequenceEqual(xrayWithInner),
            "X-ray did not reveal a triangle sealed inside the model.");
        Assert.False(
            xrayPlain.AsSpan().SequenceEqual(shadedPlain),
            "X-ray rendered the same pixels as shaded - the surface is still opaque.");
    }

    [SkippableTheory]
    [InlineData(MeshDisplayMode.Shaded)]
    [InlineData(MeshDisplayMode.Wireframe)]
    [InlineData(MeshDisplayMode.XRay)]
    public void EveryDisplayMode_LeavesTheGlStateItFound(MeshDisplayMode mode)
    {
        // The GL context is shared with the gizmo pass and with Avalonia itself. Leaving polygon
        // mode on Line or blending switched on is how the gizmo became invisible once already
        // (§11, 2026-09-06); a mode that draws correctly and corrupts the next pass is not done.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        OrbitCamera camera = FramedOn(TriangleMeshFixtures.BuildCube().Mesh, renderer);

        renderer.DisplayMode = mode;
        RenderAndReadPixels(camera);

        GL gl = _fixture.GL!;
        var polygonMode = new int[2];
        gl.GetInteger(GetPName.PolygonMode, polygonMode);
        gl.GetInteger(GetPName.DepthWritemask, out int depthWriteMask);

        Assert.Equal((int)PolygonMode.Fill, polygonMode[0]);
        Assert.False(gl.IsEnabled(EnableCap.Blend), $"{mode} left blending enabled.");
        Assert.NotEqual(0, depthWriteMask);
        Assert.True(gl.IsEnabled(EnableCap.DepthTest), $"{mode} left the depth test disabled.");
    }

    [SkippableFact]
    public void Orthographic_DrawsTheSameObjectTheSameSizeAtDifferentDepths()
    {
        // The end-to-end proof that the orthographic path reaches the framebuffer: one mesh drawn
        // twice, moved along the camera's own view axis. In perspective the near copy must be
        // visibly larger; in orthographic the two must measure the same.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        g3.DMesh3 cube = TriangleMeshFixtures.BuildCube().Mesh;
        OrbitCamera camera = FramedOn(cube, renderer);

        Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
        Matrix4x4 nearer = Matrix4x4.CreateTranslation(-forward * (camera.Distance * 0.4f));
        Matrix4x4 further = Matrix4x4.CreateTranslation(forward * (camera.Distance * 0.4f));

        camera.ProjectionMode = ProjectionMode.Perspective;
        int perspectiveNear = CountNonBackground(RenderAndReadPixels(camera, nearer));
        int perspectiveFar = CountNonBackground(RenderAndReadPixels(camera, further));

        camera.ProjectionMode = ProjectionMode.Orthographic;
        int orthographicNear = CountNonBackground(RenderAndReadPixels(camera, nearer));
        int orthographicFar = CountNonBackground(RenderAndReadPixels(camera, further));

        Assert.True(perspectiveFar > 0 && orthographicFar > 0, "The far copy was not drawn at all.");
        Assert.True(
            perspectiveNear > perspectiveFar * 1.5,
            $"Perspective control failed: near={perspectiveNear} px, far={perspectiveFar} px should differ markedly.");

        // Rasterisation of the two depths is not bit-identical (sub-pixel coverage differs), so
        // compare areas with a tolerance far tighter than the perspective difference above.
        double ratio = (double)orthographicNear / orthographicFar;
        Assert.InRange(ratio, 0.97, 1.03);
    }

    [SkippableFact]
    public void EveryStandardView_ShowsALitModel_NotAFlatSilhouette()
    {
        // Found by looking at the running app: with the light fixed in world space at
        // (-0.5, -1, -0.3), the Front, Left and Bottom presets stare at the model's unlit side and
        // draw it at the 0.2 ambient floor - a near-black silhouette on a dark background. Half of
        // a feature whose whole point is "look at the model from this side" was unusable, and no
        // test could see it because none of them looked at brightness.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();

        // Its own cube rather than the shared fixture: that one is not consistently outward-wound,
        // so its averaged vertex normals point in directions no lighting test could reason about.
        OrbitCamera camera = FramedOn(CreateOutwardWoundCube(), renderer);
        camera.ProjectionMode = ProjectionMode.Orthographic;

        // The ambient term alone: base colour times 0.2, the brightest an unlit surface can be.
        const int ambientOnly = (int)(0.7f * 0.2f * 255f);

        foreach (StandardView view in Enum.GetValues<StandardView>())
        {
            camera.SetStandardView(view);
            byte[] pixels = RenderAndReadPixels(camera);

            Assert.True(CountNonBackground(pixels) > 0, $"{view} drew nothing at all.");
            Assert.True(
                BrightestChannel(pixels) > ambientOnly * 2,
                $"{view} rendered at the ambient floor - the brightest pixel was {BrightestChannel(pixels)}, "
                + $"barely above the unlit {ambientOnly}. The model is a black silhouette in this view.");
        }
    }

    /// <summary>A closed unit cube with every face wound outward, so vertex normals point out.</summary>
    private static g3.DMesh3 CreateOutwardWoundCube()
    {
        var mesh = new g3.DMesh3();
        var ids = new int[8];
        int i = 0;
        foreach (int x in new[] { -1, 1 })
        {
            foreach (int y in new[] { -1, 1 })
            {
                foreach (int z in new[] { -1, 1 })
                {
                    ids[i++] = mesh.AppendVertex(new g3.Vector3d(x, y, z));
                }
            }
        }

        // Vertex index bits: x = 4, y = 2, z = 1.
        int[][] faces =
        {
            new[] { 0, 1, 3, 2 },  // -x
            new[] { 4, 6, 7, 5 },  // +x
            new[] { 0, 4, 5, 1 },  // -y
            new[] { 2, 3, 7, 6 },  // +y
            new[] { 0, 2, 6, 4 },  // -z
            new[] { 1, 5, 7, 3 },  // +z
        };

        foreach (int[] face in faces)
        {
            mesh.AppendTriangle(ids[face[0]], ids[face[1]], ids[face[2]]);
            mesh.AppendTriangle(ids[face[0]], ids[face[2]], ids[face[3]]);
        }

        return mesh;
    }

    private static int BrightestChannel(byte[] pixels)
    {
        int brightest = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            brightest = Math.Max(brightest, Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2])));
        }

        return brightest;
    }

    /// <summary>A triangle wholly inside the unit cube fixture, invisible unless the surface is see-through.</summary>
    private static void AppendInteriorTriangle(g3.DMesh3 mesh)
    {
        int a = mesh.AppendVertex(new g3.Vector3d(-0.5, -0.5, 0));
        int b = mesh.AppendVertex(new g3.Vector3d(0.5, -0.5, 0));
        int c = mesh.AppendVertex(new g3.Vector3d(0, 0.5, 0));
        mesh.AppendTriangle(a, b, c);
    }

    private MeshRenderer CreateRenderer()
    {
        _renderer = new MeshRenderer(_fixture.GL!);
        _renderer.Initialize();
        return _renderer;
    }

    private static OrbitCamera FramedOn(g3.DMesh3 mesh, MeshRenderer renderer)
    {
        renderer.UploadMesh(mesh);
        g3.AxisAlignedBox3d bounds = mesh.CachedBounds;
        g3.Vector3d center = bounds.Center;
        var camera = new OrbitCamera();
        camera.Frame(new Vector3((float)center.x, (float)center.y, (float)center.z), (float)(bounds.DiagonalLength / 2.0));
        return camera;
    }

    private static int CountNonBackground(byte[] pixels)
    {
        byte clearR = ToByte(ClearColor.X);
        byte clearG = ToByte(ClearColor.Y);
        byte clearB = ToByte(ClearColor.Z);

        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (Math.Abs(pixels[i] - clearR) > 2 || Math.Abs(pixels[i + 1] - clearG) > 2 || Math.Abs(pixels[i + 2] - clearB) > 2)
            {
                count++;
            }
        }

        return count;
    }

    private unsafe byte[] RenderAndReadPixels(OrbitCamera camera, Matrix4x4? model = null)
    {
        GL gl = _fixture.GL!;

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, GpuTestFixture.Width, GpuTestFixture.Height);
        gl.Enable(EnableCap.DepthTest);
        gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        float aspect = (float)GpuTestFixture.Width / GpuTestFixture.Height;
        _renderer!.Render(camera.GetViewMatrix(), camera.GetProjectionMatrix(aspect), model ?? Matrix4x4.Identity);

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
