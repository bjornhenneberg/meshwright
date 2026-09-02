using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using Meshwright.Core;
using Meshwright.Geometry.Diagnostics;
using Meshwright.IO.Stl;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.GL;
using Silk.NET.OpenGL;
using Xunit;

namespace Meshwright.Tests.Gpu;

/// <summary>
/// End-to-end regression test proving Inspect's highlight rendering works against a real, on-disk
/// broken STL file: loads it via <see cref="StlReader"/>, runs it through <see cref="MeshDocument"/>,
/// then feeds the resulting report's flagged triangles/edges into <see cref="MeshRenderer"/> (the
/// same highlight-extraction logic used by <c>MeshViewportControl.UploadCurrentMesh</c>) against a
/// real GPU/driver, and saves the framebuffer to disk as visual evidence.
/// </summary>
public sealed class BrokenSampleRenderGpuTests : IClassFixture<GpuTestFixture>, IDisposable
{
    private static readonly Vector3 ClearColor = new(0.15f, 0.15f, 0.18f);

    private readonly GpuTestFixture _fixture;
    private MeshRenderer? _renderer;

    public BrokenSampleRenderGpuTests(GpuTestFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public void RenderingBrokenSample_HighlightsFlaggedTrianglesAndCapturesPng()
    {
        Skip.IfNot(_fixture.IsAvailable, _fixture.UnavailableReason ?? "No real GL context available.");

        g3.DMesh3 mesh = StlReader.ReadFile(GetFixturePath("BrokenSample.stl"));

        var document = new MeshDocument();
        document.Load(mesh);
        MeshDiagnosticsReport report = document.Report!;
        Assert.NotEmpty(report.Issues);

        var flaggedTriangleIds = new HashSet<int>();
        var flaggedEdges = new List<g3.Index2i>();
        foreach (MeshIssue issue in report.Issues)
        {
            foreach (int triangleId in issue.TriangleIds)
            {
                flaggedTriangleIds.Add(triangleId);
            }

            flaggedEdges.AddRange(issue.EdgeIds);
        }

        Assert.NotEmpty(flaggedTriangleIds);

        _renderer = new MeshRenderer(_fixture.GL!);
        _renderer.Initialize();
        _renderer.UploadMesh(mesh, flaggedTriangleIds, flaggedEdges);

        var camera = new OrbitCamera();
        g3.AxisAlignedBox3d bounds = mesh.CachedBounds;
        g3.Vector3d center = bounds.Center;
        double radius = bounds.DiagonalLength / 2.0;
        camera.Frame(new Vector3((float)center.x, (float)center.y, (float)center.z), (float)radius);

        byte[] pixels = RenderAndReadPixels(camera);

        string outputDir = Path.Combine(Path.GetTempPath(), "meshwright-gpu-evidence");
        Directory.CreateDirectory(outputDir);
        string pngPath = Path.Combine(outputDir, "BrokenSample-highlighted.png");
        PngWriter.WriteRgba(pngPath, GpuTestFixture.Width, GpuTestFixture.Height, pixels);
        Assert.True(File.Exists(pngPath), $"Expected PNG evidence file at {pngPath}.");

        bool anyHighlightPixel = false;
        bool anyBaseShadedPixel = false;
        byte clearR = ToByte(ClearColor.X);
        byte clearG = ToByte(ClearColor.Y);
        byte clearB = ToByte(ClearColor.Z);

        for (int i = 0; i + 3 < pixels.Length; i += 4)
        {
            byte r = pixels[i];
            byte g = pixels[i + 1];
            byte b = pixels[i + 2];

            bool isBackground = Math.Abs(r - clearR) <= 2 && Math.Abs(g - clearG) <= 2 && Math.Abs(b - clearB) <= 2;
            if (isBackground)
            {
                continue;
            }

            // MeshRenderer.HighlightColor (0.95, 0.15, 0.1) is strongly red-dominant, unlike the
            // near-neutral-gray BaseColor (0.7, 0.7, 0.75); distinguish the two by hue rather than
            // an exact RGB match, since lighting dims both by the same diffuse factor.
            bool isReddish = r > g + 30 && r > b + 30;
            if (isReddish)
            {
                anyHighlightPixel = true;
            }
            else
            {
                anyBaseShadedPixel = true;
            }
        }

        Assert.True(anyHighlightPixel, $"No highlight-colored pixels found in rendered frame; see {pngPath} for the captured frame.");
        Assert.True(anyBaseShadedPixel, $"No base-shaded pixels found in rendered frame; see {pngPath} for the captured frame.");
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

    private static string GetFixturePath(string fileName, [CallerFilePath] string sourceFile = "")
    {
        string testsDir = Path.GetDirectoryName(Path.GetDirectoryName(sourceFile)!)!; // tests/
        return Path.Combine(testsDir, "Meshwright.Tests", "Fixtures", fileName);
    }

    public void Dispose()
    {
        _renderer?.Dispose();
    }
}
