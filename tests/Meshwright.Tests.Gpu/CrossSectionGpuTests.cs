using System;
using System.Collections.Generic;
using System.Numerics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.GL;
using Silk.NET.OpenGL;
using Xunit;

namespace Meshwright.Tests.Gpu;

/// <summary>
/// The cross-section measured in pixels off a real driver.
///
/// <para>
/// The section exists only inside the fragment shaders: there is no CPU-side geometry to inspect,
/// so nothing short of reading the framebuffer can tell whether the plane on screen is the plane
/// that was asked for. A sign error, a millimetre offset, or a clip applied to one pass and not
/// the other all leave every property test green.
/// </para>
/// </summary>
public sealed class CrossSectionGpuTests : IClassFixture<GpuTestFixture>, IDisposable
{
    private static readonly Vector3 ClearColor = new(0.15f, 0.15f, 0.18f);

    /// <summary>A 40 mm box based at Z=0, so the section position and the world Z coordinate are
    /// the same number and a mistake in either is a mistake in both.</summary>
    private const float BoxHeight = 40f;

    private readonly GpuTestFixture _fixture;
    private MeshRenderer? _renderer;

    public CrossSectionGpuTests(GpuTestFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public void TheSectionRemovesThePartOfTheModelBeyondThePlane_AndNothingElse()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        renderer.UploadMesh(Box());
        OrbitCamera camera = FrontCamera();

        byte[] whole = Render(renderer, camera);
        renderer.CrossSection = new CrossSectionPlane(CrossSectionAxis.Z, 10f, Flipped: false);
        byte[] sectioned = Render(renderer, camera);

        int drawnWhole = CountDrawn(whole);
        int drawnSectioned = CountDrawn(sectioned);

        Assert.True(drawnWhole > 500, $"The control frame only drew {drawnWhole} pixels of box.");
        Assert.True(
            drawnSectioned < drawnWhole,
            $"The section removed nothing: {drawnSectioned} pixels drawn against {drawnWhole}.");
        Assert.True(drawnSectioned > 100, $"The section removed the whole model, leaving {drawnSectioned} pixels.");

        // A quarter of the box's height survives, so roughly a quarter of its pixels should. This
        // is the assertion that fails if the clip is inverted - hiding the wrong half also removes
        // "some" pixels and would pass a mere inequality.
        double survivingFraction = (double)drawnSectioned / drawnWhole;
        Assert.InRange(survivingFraction, 0.15, 0.35);
    }

    [SkippableFact]
    public void ThePlaneLandsAtTheWorldMillimetreItWasGiven()
    {
        // Front orthographic on a 40 mm box, so a pixel row is a Z coordinate. The topmost row the
        // model still covers must be the projection of the section position itself; an offset of
        // even a couple of millimetres shows up here and nowhere else.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        renderer.UploadMesh(Box());
        OrbitCamera camera = FrontCamera();

        foreach (float position in new[] { 5f, 20f, 32f })
        {
            renderer.CrossSection = new CrossSectionPlane(CrossSectionAxis.Z, position, Flipped: false);
            (int minRow, int maxRow) = DrawnRowRange(Render(renderer, camera));

            float expectedTopRow = ProjectToRow(new Vector3(0f, 0f, position), camera);
            float expectedBottomRow = ProjectToRow(new Vector3(0f, 0f, 0f), camera);

            Assert.True(minRow >= 0, $"Nothing was drawn with the section at z={position}.");
            Assert.InRange(maxRow, expectedTopRow - 2, expectedTopRow + 2);
            Assert.InRange(minRow, expectedBottomRow - 2, expectedBottomRow + 2);
        }
    }

    [SkippableFact]
    public void FlippingKeepsTheOtherHalf_AndTheTwoHalvesTogetherAreTheWholeModel()
    {
        // Neither half on its own proves the sign is right; together they do. Every pixel of the
        // unsectioned box has to be covered by exactly one of the two halves, and neither half may
        // paint anywhere the whole model does not.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        renderer.UploadMesh(Box());
        OrbitCamera camera = FrontCamera();

        byte[] whole = Render(renderer, camera);
        renderer.CrossSection = new CrossSectionPlane(CrossSectionAxis.Z, 20f, Flipped: false);
        byte[] lower = Render(renderer, camera);
        renderer.CrossSection = new CrossSectionPlane(CrossSectionAxis.Z, 20f, Flipped: true);
        byte[] upper = Render(renderer, camera);

        int missed = 0;
        int overlapping = 0;
        int strayed = 0;
        for (int i = 0; i + 3 < whole.Length; i += 4)
        {
            bool inWhole = !IsBackground(whole, i);
            bool inLower = !IsBackground(lower, i);
            bool inUpper = !IsBackground(upper, i);

            if (inWhole && !inLower && !inUpper) missed++;
            if (inLower && inUpper) overlapping++;
            if (!inWhole && (inLower || inUpper)) strayed++;
        }

        // One row of pixels straddles the plane and may legitimately be claimed by both halves.
        Assert.True(missed <= GpuTestFixture.Width, $"{missed} pixels of the model are in neither half.");
        Assert.True(overlapping <= GpuTestFixture.Width, $"{overlapping} pixels are in both halves at once.");
        Assert.Equal(0, strayed);
    }

    [SkippableFact]
    public void TheFlaggedEdgeOverlayIsClippedWithTheSurfaceItMarks()
    {
        // The overlay is a second program with its own shaders. Clipping one and not the other
        // leaves yellow wireframe hanging in the air over geometry that is no longer drawn -
        // visible on screen, invisible to any test that only counts surface pixels.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        g3.DMesh3 box = Box();
        renderer.UploadMesh(box, flaggedTriangleIds: null, flaggedEdges: AllEdges(box));
        OrbitCamera camera = FrontCamera();

        renderer.CrossSection = null;
        int edgesWhole = CountEdgeHighlight(Render(renderer, camera));
        Assert.True(edgesWhole > 20, $"The control frame drew only {edgesWhole} highlighted-edge pixels.");

        renderer.CrossSection = new CrossSectionPlane(CrossSectionAxis.Z, 10f, Flipped: false);
        byte[] sectioned = Render(renderer, camera);

        float planeRow = ProjectToRow(new Vector3(0f, 0f, 10f), camera);
        int aboveThePlane = 0;
        for (int i = 0; i + 3 < sectioned.Length; i += 4)
        {
            int row = (i / 4) / GpuTestFixture.Width;
            if (row > planeRow + 2 && IsEdgeHighlight(sectioned, i))
            {
                aboveThePlane++;
            }
        }

        Assert.Equal(0, aboveThePlane);
        Assert.True(CountEdgeHighlight(sectioned) > 5, "The section removed the edge overlay entirely.");
    }

    [SkippableFact]
    public void TheInteriorTintIsAppliedOnlyWhileASectionIsOpen()
    {
        // Back faces are shaded as front faces, and tinted, only under a section. Doing it
        // unconditionally would hide inverted normals - which look exactly like a back face, and
        // which this app exists to find (InvertedNormalDetector). Both frames below draw the same
        // inside-out box from the same camera; only CrossSection differs.
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        renderer.UploadMesh(InsideOutBox());
        OrbitCamera camera = FrontCamera();

        renderer.CrossSection = null;
        byte[] unsectioned = Render(renderer, camera);

        renderer.CrossSection = new CrossSectionPlane(CrossSectionAxis.Z, 30f, Flipped: false);
        byte[] sectioned = Render(renderer, camera);

        Assert.Equal(0, WarmPixels(unsectioned));
        Assert.True(
            WarmPixels(sectioned) > 100,
            $"A section exposed no tinted interior at all ({WarmPixels(sectioned)} warm pixels).");

        // ... and the untinted frame really is the dark one a flipped normal should look like.
        Assert.True(
            MeanBrightness(sectioned) > MeanBrightness(unsectioned),
            "Sectioning did not brighten the inward-facing surfaces it exposed.");
    }

    [SkippableFact]
    public void TheSectionLeavesTheGlStateItFound()
    {
        // The clip lives entirely in the shaders and must add no state of its own; the context is
        // shared with the build plate pass, the gizmo pass and Avalonia (§11, 2026-09-06).
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");
        MeshRenderer renderer = CreateRenderer();
        renderer.UploadMesh(Box());
        renderer.CrossSection = new CrossSectionPlane(CrossSectionAxis.Z, 20f, Flipped: false);

        Render(renderer, FrontCamera());

        GL gl = _fixture.GL!;
        var polygonMode = new int[2];
        gl.GetInteger(GetPName.PolygonMode, polygonMode);
        gl.GetInteger(GetPName.DepthWritemask, out int depthWriteMask);

        Assert.Equal((int)PolygonMode.Fill, polygonMode[0]);
        Assert.False(gl.IsEnabled(EnableCap.Blend), "The section left blending enabled.");
        Assert.False(gl.IsEnabled(EnableCap.CullFace), "The section left face culling enabled.");
        Assert.NotEqual(0, depthWriteMask);
    }

    private static OrbitCamera FrontCamera()
    {
        // Orthographic and framed on the box's own centre, so a pixel row maps linearly to a world
        // Z and the row assertions above mean millimetres.
        var camera = new OrbitCamera { ProjectionMode = ProjectionMode.Orthographic };
        camera.Frame(new Vector3(0f, 0f, BoxHeight / 2f), BoxHeight / 2f);
        camera.SetStandardView(StandardView.Front);
        return camera;
    }

    private static g3.DMesh3 Box() => BuildBox(reversed: false);

    /// <summary>The same box with every triangle wound the other way, so the camera sees back
    /// faces - what an inverted normal looks like.</summary>
    private static g3.DMesh3 InsideOutBox() => BuildBox(reversed: true);

    private static g3.DMesh3 BuildBox(bool reversed)
    {
        var mesh = new g3.DMesh3();
        var ids = new int[8];
        int i = 0;
        foreach (double x in new[] { -20.0, 20.0 })
        {
            foreach (double y in new[] { -20.0, 20.0 })
            {
                foreach (double z in new[] { 0.0, (double)BoxHeight })
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
            AppendTriangle(mesh, ids[face[0]], ids[face[1]], ids[face[2]], reversed);
            AppendTriangle(mesh, ids[face[0]], ids[face[2]], ids[face[3]], reversed);
        }

        return mesh;
    }

    private static void AppendTriangle(g3.DMesh3 mesh, int a, int b, int c, bool reversed)
        => mesh.AppendTriangle(reversed ? c : a, b, reversed ? a : c);

    private static List<g3.Index2i> AllEdges(g3.DMesh3 mesh)
    {
        var edges = new List<g3.Index2i>();
        foreach (int eid in mesh.EdgeIndices())
        {
            g3.Index2i edge = mesh.GetEdgeV(eid);
            edges.Add(edge);
        }

        return edges;
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

    /// <summary>Pixels whose red channel clearly leads their blue: the interior tint.</summary>
    private static int WarmPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (!IsBackground(pixels, i) && pixels[i] > pixels[i + 2] + 30)
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsEdgeHighlight(byte[] pixels, int i)
        => pixels[i] > 180 && pixels[i + 1] > 140 && pixels[i + 2] < 110;

    private static int CountEdgeHighlight(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (IsEdgeHighlight(pixels, i))
            {
                count++;
            }
        }

        return count;
    }

    private static double MeanBrightness(byte[] pixels)
    {
        long total = 0;
        int count = 0;
        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            if (IsBackground(pixels, i))
            {
                continue;
            }

            total += pixels[i] + pixels[i + 1] + pixels[i + 2];
            count++;
        }

        return count == 0 ? 0 : (double)total / (count * 3);
    }

    private static bool IsBackground(byte[] pixels, int i)
        => Math.Abs(pixels[i] - ToByte(ClearColor.X)) <= 2
            && Math.Abs(pixels[i + 1] - ToByte(ClearColor.Y)) <= 2
            && Math.Abs(pixels[i + 2] - ToByte(ClearColor.Z)) <= 2;

    private static int CountDrawn(byte[] pixels)
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

    private MeshRenderer CreateRenderer()
    {
        _renderer = new MeshRenderer(_fixture.GL!);
        _renderer.Initialize();
        return _renderer;
    }

    private unsafe byte[] Render(MeshRenderer renderer, OrbitCamera camera)
    {
        GL gl = _fixture.GL!;

        gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        gl.Viewport(0, 0, GpuTestFixture.Width, GpuTestFixture.Height);
        gl.Enable(EnableCap.DepthTest);
        gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f);
        gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

        float aspect = (float)GpuTestFixture.Width / GpuTestFixture.Height;
        renderer.Render(camera.GetViewMatrix(), camera.GetProjectionMatrix(aspect), Matrix4x4.Identity);

        var pixels = new byte[GpuTestFixture.Width * GpuTestFixture.Height * 4];
        fixed (byte* data = pixels)
        {
            gl.ReadPixels(0, 0, GpuTestFixture.Width, GpuTestFixture.Height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        return pixels;
    }

    public void Dispose() => _renderer?.Dispose();
}
