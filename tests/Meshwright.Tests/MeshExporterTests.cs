using g3;
using Meshwright.IO;
using Meshwright.IO.Stl;
using Meshwright.IO.Wavefront;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// Tests for <see cref="MeshExporter"/>, the extension-to-writer dispatch behind the save-file
/// picker (§5.1: STL and OBJ in v1.0; 3MF and PLY deferred).
/// </summary>
public class MeshExporterTests
{
    private static DMesh3 BuildTriangle()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a, b, c);
        return mesh;
    }

    [Theory]
    [InlineData("part.stl")]
    [InlineData("part.STL")]
    public void ExportsStlByExtension_CaseInsensitively(string fileName)
    {
        DMesh3 mesh = BuildTriangle();
        using var stream = new MemoryStream();

        MeshExporter.Export(stream, mesh, fileName);
        stream.Position = 0;

        Assert.Equal(1, StlReader.Read(stream).TriangleCount);
    }

    [Theory]
    [InlineData("part.obj")]
    [InlineData("part.OBJ")]
    public void ExportsObjByExtension_CaseInsensitively(string fileName)
    {
        DMesh3 mesh = BuildTriangle();
        using var stream = new MemoryStream();

        MeshExporter.Export(stream, mesh, fileName);
        stream.Position = 0;

        Assert.Equal(1, ObjReader.Read(stream).TriangleCount);
    }

    [Fact]
    public void ExportFile_WritesToDisk()
    {
        DMesh3 mesh = BuildTriangle();
        string path = Path.Combine(Path.GetTempPath(), $"meshwright-export-{Guid.NewGuid():N}.stl");

        try
        {
            MeshExporter.ExportFile(path, mesh);
            Assert.Equal(1, StlReader.ReadFile(path).TriangleCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsAFormatItDoesNotSupport_NamingWhatItDoes()
    {
        DMesh3 mesh = BuildTriangle();
        using var stream = new MemoryStream();

        var ex = Assert.Throws<NotSupportedException>(() => MeshExporter.Export(stream, mesh, "part.3mf"));

        Assert.Contains(".3mf", ex.Message);
        Assert.Contains(".stl", ex.Message);
        Assert.Contains(".obj", ex.Message);
    }

    [Fact]
    public void RejectsAFileWithNoExtension()
    {
        DMesh3 mesh = BuildTriangle();
        using var stream = new MemoryStream();

        var ex = Assert.Throws<NotSupportedException>(() => MeshExporter.Export(stream, mesh, "part"));
        Assert.Contains("no file extension", ex.Message);
    }

    [Theory]
    [InlineData("a.stl", true)]
    [InlineData("a.obj", true)]
    [InlineData("a.ply", false)]
    [InlineData("a", false)]
    public void CanExport_MatchesTheSupportedSet(string fileName, bool expected)
    {
        Assert.Equal(expected, MeshExporter.CanExport(fileName));
    }

    [Fact]
    public void SupportedPatternsCoverEverySupportedExtensionInLowerAndUpperCase()
    {
        // The save-file picker builds its filter from SupportedPatterns; if the two lists drift,
        // the picker silently stops offering a format the exporter actually handles. GTK matches
        // these patterns case-sensitively, so both the lower-case and upper-case spelling of
        // every extension must be present, not just the canonical lower-case one.
        foreach (string extension in MeshExporter.SupportedExtensions)
        {
            Assert.Contains("*" + extension, MeshExporter.SupportedPatterns);
            Assert.Contains("*" + extension.ToUpperInvariant(), MeshExporter.SupportedPatterns);
        }

        // And nothing in SupportedPatterns should name a format the exporter doesn't handle.
        foreach (string pattern in MeshExporter.SupportedPatterns)
        {
            string extension = pattern.TrimStart('*').ToLowerInvariant();
            Assert.Contains(extension, MeshExporter.SupportedExtensions);
        }
    }

    [Theory]
    [InlineData("Eiffel_tower_sample.STL")]
    [InlineData("part.OBJ")]
    public void SupportedPatterns_MatchUppercaseExtensionFileNames(string fileName)
    {
        // Mirrors the same fix in MeshImporterTests: GTK's save dialog matches patterns
        // case-sensitively too, so an uppercase target extension must still be offered.
        Assert.Contains(
            MeshExporter.SupportedPatterns,
            pattern => fileName.EndsWith(pattern.TrimStart('*'), StringComparison.Ordinal));
    }

    /// <summary>
    /// A split model is two solids in one document; Export Parts is what turns it into the two
    /// files a printer wants. The assertion that matters is that each written file holds exactly
    /// one part, not merely that two files appeared.
    /// </summary>
    [Fact]
    public void ExportParts_WritesOneFilePerShell_NumberedFromTheBaseName()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);
        AppendTranslated(mesh, BuildCube(Vector3d.Zero, 4.0), new Vector3d(100, 0, 0));

        string directory = Path.Combine(Path.GetTempPath(), $"meshwright-parts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string basePath = Path.Combine(directory, "model.stl");

        try
        {
            IReadOnlyList<string> written = MeshExporter.ExportParts(basePath, mesh);

            Assert.Equal(2, written.Count);
            Assert.Equal(Path.Combine(directory, "model-part1.stl"), written[0]);
            Assert.Equal(Path.Combine(directory, "model-part2.stl"), written[1]);

            // Largest part first, one shell each, and every triangle accounted for.
            DMesh3 first = StlReader.ReadFile(written[0]);
            DMesh3 second = StlReader.ReadFile(written[1]);
            Assert.Equal(12, first.TriangleCount);
            Assert.Equal(12, second.TriangleCount);
            Assert.Equal(1000.0, Volume(first), 6);
            Assert.Equal(64.0, Volume(second), 6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExportParts_HonoursTheBaseNamesFormat()
    {
        DMesh3 mesh = BuildCube(Vector3d.Zero, 10.0);
        AppendTranslated(mesh, BuildCube(Vector3d.Zero, 4.0), new Vector3d(100, 0, 0));

        string directory = Path.Combine(Path.GetTempPath(), $"meshwright-parts-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            IReadOnlyList<string> written = MeshExporter.ExportParts(Path.Combine(directory, "model.obj"), mesh);

            Assert.All(written, path => Assert.Equal(".obj", Path.GetExtension(path)));
            Assert.All(written, path => Assert.True(File.Exists(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static double Volume(DMesh3 mesh)
    {
        double volume = 0.0;
        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            volume += mesh.GetVertex(tri.a).Dot(mesh.GetVertex(tri.b).Cross(mesh.GetVertex(tri.c))) / 6.0;
        }

        return Math.Abs(volume);
    }

    private static void AppendTranslated(DMesh3 target, DMesh3 source, Vector3d offset)
    {
        var map = new Dictionary<int, int>();
        foreach (int vid in source.VertexIndices())
        {
            map[vid] = target.AppendVertex(source.GetVertex(vid) + offset);
        }

        foreach (int tid in source.TriangleIndices())
        {
            Index3i tri = source.GetTriangle(tid);
            target.AppendTriangle(map[tri.a], map[tri.b], map[tri.c]);
        }
    }

    private static DMesh3 BuildCube(Vector3d origin, double size)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(origin + new Vector3d(0, 0, 0) * size);
        int v100 = mesh.AppendVertex(origin + new Vector3d(1, 0, 0) * size);
        int v110 = mesh.AppendVertex(origin + new Vector3d(1, 1, 0) * size);
        int v010 = mesh.AppendVertex(origin + new Vector3d(0, 1, 0) * size);
        int v001 = mesh.AppendVertex(origin + new Vector3d(0, 0, 1) * size);
        int v101 = mesh.AppendVertex(origin + new Vector3d(1, 0, 1) * size);
        int v111 = mesh.AppendVertex(origin + new Vector3d(1, 1, 1) * size);
        int v011 = mesh.AppendVertex(origin + new Vector3d(0, 1, 1) * size);

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
        }

        Quad(v000, v010, v110, v100);
        Quad(v001, v101, v111, v011);
        Quad(v000, v100, v101, v001);
        Quad(v010, v011, v111, v110);
        Quad(v000, v001, v011, v010);
        Quad(v100, v110, v111, v101);

        return mesh;
    }
}
