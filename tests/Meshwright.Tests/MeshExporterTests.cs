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
    public void SupportedPatternsCoverEverySupportedExtension()
    {
        // The save-file picker builds its filter from SupportedPatterns; if the two lists drift,
        // the picker silently stops offering a format the exporter actually handles.
        Assert.Equal(
            MeshExporter.SupportedExtensions.Select(e => "*" + e).OrderBy(p => p),
            MeshExporter.SupportedPatterns.OrderBy(p => p));
    }
}
