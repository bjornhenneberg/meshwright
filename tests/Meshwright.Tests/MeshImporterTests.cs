using System.Text;
using Meshwright.IO;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// Tests for <see cref="MeshImporter"/>, the extension-to-importer dispatch behind the file picker
/// (§5.1: STL and OBJ in v1.0; 3MF and PLY deferred).
/// </summary>
public class MeshImporterTests
{
    private const string TriangleObj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";

    private const string TriangleStl =
        "solid t\nfacet normal 0 0 1\nouter loop\n" +
        "vertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\n" +
        "endloop\nendfacet\nendsolid t\n";

    private static Stream StreamOf(string text) => new MemoryStream(Encoding.ASCII.GetBytes(text));

    [Theory]
    [InlineData("part.obj")]
    [InlineData("part.OBJ")]
    [InlineData("/some/dir/part.Obj")]
    public void ImportsObjByExtension_CaseInsensitively(string fileName)
    {
        Assert.Equal(1, MeshImporter.Import(StreamOf(TriangleObj), fileName).TriangleCount);
    }

    [Theory]
    [InlineData("part.stl")]
    [InlineData("part.STL")]
    public void ImportsStlByExtension_CaseInsensitively(string fileName)
    {
        Assert.Equal(1, MeshImporter.Import(StreamOf(TriangleStl), fileName).TriangleCount);
    }

    [Fact]
    public void ImportFile_ReadsFromDisk()
    {
        string path = Path.Combine(Path.GetTempPath(), $"meshwright-import-{Guid.NewGuid():N}.obj");
        try
        {
            File.WriteAllText(path, TriangleObj);
            Assert.Equal(1, MeshImporter.ImportFile(path).TriangleCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsAFormatItDoesNotSupport_NamingWhatItDoes()
    {
        var ex = Assert.Throws<NotSupportedException>(() => MeshImporter.Import(StreamOf(TriangleObj), "part.3mf"));

        Assert.Contains(".3mf", ex.Message);
        Assert.Contains(".stl", ex.Message);
        Assert.Contains(".obj", ex.Message);
    }

    [Fact]
    public void RejectsAFileWithNoExtension()
    {
        var ex = Assert.Throws<NotSupportedException>(() => MeshImporter.Import(StreamOf(TriangleObj), "part"));
        Assert.Contains("no file extension", ex.Message);
    }

    [Theory]
    [InlineData("a.stl", true)]
    [InlineData("a.obj", true)]
    [InlineData("a.ply", false)]
    [InlineData("a", false)]
    public void CanImport_MatchesTheSupportedSet(string fileName, bool expected)
    {
        Assert.Equal(expected, MeshImporter.CanImport(fileName));
    }

    [Fact]
    public void SupportedPatternsCoverEverySupportedExtensionInLowerAndUpperCase()
    {
        // The file picker builds its filter from SupportedPatterns; if the two lists drift, the
        // picker silently stops offering a format the importer actually handles. GTK matches
        // these patterns case-sensitively, so both the lower-case and upper-case spelling of
        // every extension must be present, not just the canonical lower-case one.
        foreach (string extension in MeshImporter.SupportedExtensions)
        {
            Assert.Contains("*" + extension, MeshImporter.SupportedPatterns);
            Assert.Contains("*" + extension.ToUpperInvariant(), MeshImporter.SupportedPatterns);
        }

        // And nothing in SupportedPatterns should name a format the importer doesn't handle.
        foreach (string pattern in MeshImporter.SupportedPatterns)
        {
            string extension = pattern.TrimStart('*').ToLowerInvariant();
            Assert.Contains(extension, MeshImporter.SupportedExtensions);
        }
    }

    [Theory]
    [InlineData("Eiffel_tower_sample.STL")]
    [InlineData("part.OBJ")]
    public void SupportedPatterns_MatchUppercaseExtensionFileNames(string fileName)
    {
        // Reproduces a real bug: GTK's file chooser matches FilePickerFileType patterns
        // case-sensitively, so a lower-case-only pattern list left files like this one — the
        // Eiffel Tower corpus sample SPECIFICATION.md references — invisible in the Open dialog.
        Assert.Contains(
            MeshImporter.SupportedPatterns,
            pattern => fileName.EndsWith(pattern.TrimStart('*'), StringComparison.Ordinal));
    }
}
