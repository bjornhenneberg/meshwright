using g3;
using Meshwright.IO.Stl;
using Meshwright.IO.Wavefront;

namespace Meshwright.IO;

/// <summary>
/// Picks a writer by file extension, mirroring <see cref="MeshImporter"/> so the save-file
/// picker's filter and the writer set cannot drift apart. §5.1's v1.0 export scope is binary
/// STL and ASCII OBJ; 3MF and PLY are deferred to v1.x.
/// </summary>
public static class MeshExporter
{
    /// <summary>Extensions this exporter writes, lower-case and dot-prefixed.</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".stl", ".obj" };

    /// <summary>File-picker patterns for the supported formats, in the order they should be offered.</summary>
    public static IReadOnlyList<string> SupportedPatterns { get; } = new[] { "*.stl", "*.obj" };

    public static bool CanExport(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    public static void ExportFile(string path, DMesh3 mesh)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".stl":
                StlWriter.WriteFile(path, mesh);
                break;
            case ".obj":
                ObjWriter.WriteFile(path, mesh);
                break;
            case var other:
                throw new NotSupportedException(
                    string.IsNullOrEmpty(other)
                        ? $"'{path}' has no file extension, so Meshwright cannot tell which format to export."
                        : $"Meshwright cannot export '{other}' files. Supported formats: {string.Join(", ", SupportedExtensions)}.");
        }
    }

    /// <summary>
    /// Exports to an already-open stream. <paramref name="fileName"/> supplies the extension and
    /// need not be a real path — a picker's display name is enough.
    /// </summary>
    public static void Export(Stream stream, DMesh3 mesh, string fileName)
    {
        switch (Path.GetExtension(fileName).ToLowerInvariant())
        {
            case ".stl":
                StlWriter.Write(stream, mesh);
                break;
            case ".obj":
                ObjWriter.Write(stream, mesh);
                break;
            case var other:
                throw new NotSupportedException(
                    string.IsNullOrEmpty(other)
                        ? $"'{fileName}' has no file extension, so Meshwright cannot tell which format to export."
                        : $"Meshwright cannot export '{other}' files. Supported formats: {string.Join(", ", SupportedExtensions)}.");
        }
    }
}
