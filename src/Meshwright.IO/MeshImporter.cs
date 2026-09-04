using g3;
using Meshwright.IO.Stl;
using Meshwright.IO.Wavefront;

namespace Meshwright.IO;

/// <summary>
/// Picks an importer by file extension, so callers (the file picker, drag-and-drop, a future CLI)
/// do not each need to know which formats exist. §5.1's v1.0 import scope is STL and OBJ; the
/// interface is shaped for more, but 3MF and PLY are deferred to v1.x.
/// </summary>
public static class MeshImporter
{
    /// <summary>Extensions this importer accepts, lower-case and dot-prefixed.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".stl", ".obj" };

    /// <summary>File-picker patterns for the supported formats, in the order they should be offered.</summary>
    public static IReadOnlyList<string> SupportedPatterns { get; } = new[] { "*.stl", "*.obj" };

    public static bool CanImport(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    public static DMesh3 ImportFile(string path)
    {
        using Stream stream = File.OpenRead(path);
        return Import(stream, path);
    }

    /// <summary>
    /// Imports from an already-open stream. <paramref name="fileName"/> supplies the extension and
    /// need not be a real path — a picker's display name is enough.
    /// </summary>
    public static DMesh3 Import(Stream stream, string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".stl" => StlReader.Read(stream),
            ".obj" => ObjReader.Read(stream),
            var other => throw new NotSupportedException(
                string.IsNullOrEmpty(other)
                    ? $"'{fileName}' has no file extension, so Meshwright cannot tell which format it is."
                    : $"Meshwright cannot import '{other}' files. Supported formats: {string.Join(", ", SupportedExtensions)}."),
        };
}
