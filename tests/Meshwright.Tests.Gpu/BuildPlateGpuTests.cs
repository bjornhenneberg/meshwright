using System;
using System.Numerics;
using Meshwright.Geometry.Printing;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.GL;
using Silk.NET.OpenGL;
using Xunit;

namespace Meshwright.Tests.Gpu;

/// <summary>
/// The build plate measured in pixels off a real driver.
///
/// <para>
/// A flag assertion cannot tell whether the grid reaches the screen. The two failures that a
/// property test would miss entirely are both visual: a plate that vanishes when the camera looks
/// at it edge-on (Front, Back, Left and Right all view the Z=0 plane exactly edge-on, where it
/// draws a single line and any depth or culling mistake erases it), and a plate that paints over
/// the model instead of sitting behind it.
/// </para>
/// </summary>
public sealed class BuildPlateGpuTests : IClassFixture<GpuTestFixture>, IDisposable
{
    private static readonly Vector3 ClearColor = new(0.15f, 0.15f, 0.18f);
    private static readonly BuildVolume Bed = new("Test", 220, 220, 250);

    private readonly GpuTestFixture _fixture;
    private BuildPlateRenderer? _plate;
    private MeshRenderer? _mesh;

    public BuildPlateGpuTests(GpuTestFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableTheory]
    [InlineData(ProjectionMode.Perspective)]
    [InlineData(ProjectionMode.Orthographic)]
    public void TheBuildPlateIsVisibleFromEveryStandardView(ProjectionMode projection)
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        BuildPlateRenderer plate = CreatePlate();
        OrbitCamera camera = BedCamera(projection);

        foreach (StandardView view in Enum.GetValues<StandardView>())
        {
            camera.SetStandardView(view);
            int coverage = CountNonBackground(RenderPlate(plate, camera));

            // Front/Back/Left/Right look straight along the plate: it is one line, not a surface,
            // and one line is still the difference between "there is a bed there" and nothing.
            Assert.True(coverage > 0, $"{view} in {projection} drew no build plate at all.");
        }
    }

    [SkippableFact]
    public void TheGridSitsOnWorldZ0_WhereverTheCameraIsPointed()
    {
        // Front view: the Z=0 plane is edge-on, so the whole plate collapses to one row of pixels,
        // and that row must be the projection of world Z=0. A grid drawn at the model's base, at
        // the top of the build volume, or on the wrong axis lands somewhere else.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        BuildPlateRenderer plate = CreatePlate();

        // Framed tightly (a ~54 mm view height over 64 pixels, so under a millimetre per pixel):
        // at the whole bed's framing one pixel is 6 mm and a grid drawn at the wrong height would
        // pass unnoticed.
        foreach (float targetZ in new[] { 0f, 15f, -15f })
        {
            OrbitCamera camera = BedCamera(ProjectionMode.Orthographic, targetZ, radius: 20f);
            camera.SetStandardView(StandardView.Front);

            float expectedRow = ProjectToRow(Vector3.Zero, camera);
            (int minRow, int maxRow) = DrawnRowRange(RenderPlate(plate, camera));

            Assert.True(minRow >= 0, $"Nothing was drawn with the camera looking at z={targetZ}.");
            Assert.InRange(minRow, expectedRow - 2, expectedRow + 2);
            Assert.InRange(maxRow, expectedRow - 2, expectedRow + 2);
        }
    }

    [SkippableFact]
    public void TheModelIsDrawnOverThePlate_NotUnderIt()
    {
        // Render order and depth state in one assertion: every pixel the model covers on its own
        // must be exactly the same pixel when the plate is drawn first. A plate drawn afterwards,
        // or drawn with the depth test off the way the gizmo pass is, stripes grid lines across
        // the model.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        BuildPlateRenderer plate = CreatePlate();
        MeshRenderer mesh = CreateMesh();
        mesh.UploadMesh(BoxOnTheBed());

        OrbitCamera camera = BedCamera(ProjectionMode.Perspective);
        camera.SetStandardView(StandardView.Isometric);

        byte[] meshOnly = Render(camera, plate: null, mesh: mesh);
        byte[] both = Render(camera, plate, mesh);

        int modelPixels = 0;
        for (int i = 0; i + 3 < meshOnly.Length; i += 4)
        {
            if (!IsBackground(meshOnly, i))
            {
                modelPixels++;
                Assert.True(
                    meshOnly[i] == both[i] && meshOnly[i + 1] == both[i + 1] && meshOnly[i + 2] == both[i + 2],
                    $"The build plate changed a pixel the model covers (index {i / 4}).");
            }
        }

        Assert.True(modelPixels > 50, $"The control frame only drew {modelPixels} model pixels.");

        // ... and the plate really was drawn: the frame with it differs somewhere.
        Assert.False(meshOnly.AsSpan().SequenceEqual(both), "Adding the build plate changed nothing at all.");
    }

    [SkippableFact]
    public void AnOutOfBoundsModelTurnsTheBedOutlineAmber()
    {
        // The warning has words in the status bar, but the bed itself has to say so without being
        // read. Both frames draw exactly the same geometry; only ModelFits differs.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        BuildPlateRenderer plate = CreatePlate();
        OrbitCamera camera = BedCamera(ProjectionMode.Orthographic);
        camera.SetStandardView(StandardView.Top);

        plate.ModelFits = true;
        byte[] fitting = RenderPlate(plate, camera);
        plate.ModelFits = false;
        byte[] overhanging = RenderPlate(plate, camera);

        Assert.False(fitting.AsSpan().SequenceEqual(overhanging), "The bed looked identical in and out of bounds.");
        Assert.True(
            WarmPixels(overhanging) > WarmPixels(fitting),
            $"Out of bounds drew {WarmPixels(overhanging)} warm pixels against {WarmPixels(fitting)} in bounds.");
    }

    [SkippableFact]
    public void TheMinorGridForATinyModel_IsActuallyVisibleAgainstTheBackground()
    {
        // Being in the vertex buffer is not the same as being on screen. The minor tier first
        // shipped at 0.35 of the plate colour, which resolves to the clear colour to within one
        // 8-bit step: the 2 mm Menger sponge stood on a 220 mm bed whose fine grid was drawn,
        // measurable in every unit test, and invisible to a person looking at it.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        BuildPlateRenderer plate = CreatePlate();

        // Framed on a 2 mm part: the major grid is 10 mm, so anything visible here is minor lines.
        OrbitCamera camera = BedCamera(ProjectionMode.Orthographic, targetZ: 0f, radius: 1.5f);
        camera.SetStandardView(StandardView.Top);

        plate.SetBuildVolume(Bed, modelFootprintMm: 200);
        byte[] majorOnly = RenderPlate(plate, camera);

        plate.SetBuildVolume(Bed, modelFootprintMm: 2);
        byte[] withMinor = RenderPlate(plate, camera);

        // Only the pixels the minor tier added: the two major lines through the origin are in
        // frame either way and are bright enough to hide the question being asked.
        (int count, int contrast) = AddedPixels(majorOnly, withMinor);

        Assert.True(count > 100, $"The minor grid added only {count} pixels.");

        // Contrast, not mere difference: at 0.35 those pixels still counted as "not the clear
        // colour", because they missed it by a single 8-bit step. What a person can see is the
        // size of the gap, so that is what is measured.
        Assert.True(
            contrast >= 20,
            $"The minor grid lines differ from the background by {contrast}/255 - drawn, and invisible.");
    }

    [SkippableFact]
    public void TheBuildPlateLeavesTheGlStateItFound()
    {
        // The context is shared with the mesh pass, the gizmo pass and Avalonia. §11 (2026-09-06):
        // leaving state behind is how gizmos became invisible.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        BuildPlateRenderer plate = CreatePlate();
        OrbitCamera camera = BedCamera(ProjectionMode.Perspective);

        RenderPlate(plate, camera);

        GL gl = _fixture.GL!;
        var polygonMode = new int[2];
        gl.GetInteger(GetPName.PolygonMode, polygonMode);
        gl.GetInteger(GetPName.DepthWritemask, out int depthWriteMask);
        gl.GetInteger(GetPName.LineWidth, out int lineWidth);

        Assert.Equal((int)PolygonMode.Fill, polygonMode[0]);
        Assert.False(gl.IsEnabled(EnableCap.Blend), "The build plate left blending enabled.");
        Assert.NotEqual(0, depthWriteMask);
        Assert.True(gl.IsEnabled(EnableCap.DepthTest), "The build plate left the depth test disabled.");
        Assert.Equal(1, lineWidth);
    }

    /// <summary>A 40 mm box sitting on the bed, well inside a 220 mm plate.</summary>
    private static g3.DMesh3 BoxOnTheBed()
    {
        var mesh = new g3.DMesh3();
        var ids = new int[8];
        int i = 0;
        foreach (double x in new[] { -20.0, 20.0 })
        {
            foreach (double y in new[] { -20.0, 20.0 })
            {
                foreach (double z in new[] { 0.0, 40.0 })
                {
                    ids[i++] = mesh.AppendVertex(new g3.Vector3d(x, y, z));
                }
            }
        }

        int[][] faces =
        {
            new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 },
            new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 },
            new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 },
        };

        foreach (int[] face in faces)
        {
            mesh.AppendTriangle(ids[face[0]], ids[face[1]], ids[face[2]]);
            mesh.AppendTriangle(ids[face[0]], ids[face[2]], ids[face[3]]);
        }

        return mesh;
    }

    private static OrbitCamera BedCamera(ProjectionMode projection, float targetZ = 40f, float radius = 160f)
    {
        var camera = new OrbitCamera { ProjectionMode = projection };
        camera.Frame(new Vector3(0f, 0f, targetZ), radius);
        return camera;
    }

    /// <summary>The pixel row (from the bottom, as ReadPixels returns them) a world point projects to.</summary>
    private static float ProjectToRow(Vector3 world, OrbitCamera camera)
    {
        float aspect = (float)GpuTestFixture.Width / GpuTestFixture.Height;
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), camera.GetViewMatrix() * camera.GetProjectionMatrix(aspect));
        float ndcY = clip.Y / clip.W;
        return ((ndcY * 0.5f) + 0.5f) * GpuTestFixture.Height;
    }

    private static (int MinRow, int MaxRow) DrawnRowRange(byte[] pixels)
    {
        int min = int.MaxValue;
        int max = -1;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (IsBackground(pixels, i))
            {
                continue;
            }

            int row = (i / 4) / GpuTestFixture.Width;
            min = Math.Min(min, row);
            max = Math.Max(max, row);
        }

        return max < 0 ? (-1, -1) : (min, max);
    }

    /// <summary>
    /// How many pixels the second frame paints where the first left the background, and the
    /// largest single-channel gap those pixels open up against it.
    /// </summary>
    private static (int Count, int Contrast) AddedPixels(byte[] before, byte[] after)
    {
        byte[] clear = { ToByte(ClearColor.X), ToByte(ClearColor.Y), ToByte(ClearColor.Z) };
        int count = 0;
        int contrast = 0;

        for (int i = 0; i + 3 < before.Length; i += 4)
        {
            if (!IsBackground(before, i) || IsBackground(after, i))
            {
                continue;
            }

            count++;
            for (int c = 0; c < 3; c++)
            {
                contrast = Math.Max(contrast, Math.Abs(after[i + c] - clear[c]));
            }
        }

        return (count, contrast);
    }

    /// <summary>Pixels whose red channel clearly leads their blue: the amber out-of-bounds outline.</summary>
    private static int WarmPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (!IsBackground(pixels, i) && pixels[i] > pixels[i + 2] + 40)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsBackground(byte[] pixels, int i)
        => Math.Abs(pixels[i] - ToByte(ClearColor.X)) <= 2
            && Math.Abs(pixels[i + 1] - ToByte(ClearColor.Y)) <= 2
            && Math.Abs(pixels[i + 2] - ToByte(ClearColor.Z)) <= 2;

    private static int CountNonBackground(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (!IsBackground(pixels, i))
            {
                count++;
            }
        }

        return count;
    }

    private static byte ToByte(float channel) => (byte)Math.Clamp(channel * 255f, 0f, 255f);

    private BuildPlateRenderer CreatePlate()
    {
        _plate = new BuildPlateRenderer(_fixture.GL!);
        _plate.Initialize();
        _plate.SetBuildVolume(Bed, modelFootprintMm: 40);
        return _plate;
    }

    private MeshRenderer CreateMesh()
    {
        _mesh = new MeshRenderer(_fixture.GL!);
        _mesh.Initialize();
        return _mesh;
    }

    private byte[] RenderPlate(BuildPlateRenderer plate, OrbitCamera camera) => Render(camera, plate, mesh: null);

    private unsafe byte[] Render(OrbitCamera camera, BuildPlateRenderer? plate, MeshRenderer? mesh)
    {
        GL gl = _fixture.GL!;

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, GpuTestFixture.Width, GpuTestFixture.Height);
        gl.Enable(EnableCap.DepthTest);
        gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        float aspect = (float)GpuTestFixture.Width / GpuTestFixture.Height;
        Matrix4x4 view = camera.GetViewMatrix();
        Matrix4x4 projection = camera.GetProjectionMatrix(aspect);

        // The viewport's own order: plate first, then the mesh over it.
        plate?.Render(view, projection);
        mesh?.Render(view, projection, Matrix4x4.Identity);

        var pixels = new byte[GpuTestFixture.Width * GpuTestFixture.Height * 4];
        fixed (byte* data = pixels)
        {
            gl.ReadPixels(0, 0, GpuTestFixture.Width, GpuTestFixture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        return pixels;
    }

    public void Dispose()
    {
        _plate?.Dispose();
        _mesh?.Dispose();
    }
}
