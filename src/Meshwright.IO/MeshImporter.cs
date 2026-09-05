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

    /// <summary>
    /// File-picker patterns for the supported formats. GTK's (and other Linux toolkits') file
    /// choosers match <c>FilePickerFileType</c> patterns case-sensitively, so a lower-case-only
    /// pattern list makes a file like "Model.STL" — routine output from CAD exporters — simply
    /// not appear in the dialog, with nothing telling the user why. Each extension is therefore
    /// listed in lower-case, upper-case, and capitalized form; import itself already accepts any
    /// case (see <see cref="ImportWithDiagnostics"/>), so this only affects what the dialog shows.
    /// </summary>
    public static IReadOnlyList<string> SupportedPatterns { get; } = new[]
    {
        "*.stl", "*.STL", "*.Stl",
        "*.obj", "*.OBJ", "*.Obj",
    };

    public static bool CanImport(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    public static DMesh3 ImportFile(string path) => ImportFileWithDiagnostics(path).Mesh;

    /// <summary>
    /// Imports and reports what the mesh representation could not hold. Prefer this wherever the
    /// result is shown to a user: see <see cref="MeshImportResult"/>.
    /// </summary>
    public static MeshImportResult ImportFileWithDiagnostics(string path)
    {
        using Stream stream = File.OpenRead(path);
        return ImportWithDiagnostics(stream, path);
    }

    /// <summary>
    /// Imports from an already-open stream. <paramref name="fileName"/> supplies the extension and
    /// need not be a real path — a picker's display name is enough.
    /// </summary>
    public static DMesh3 Import(Stream stream, string fileName) => ImportWithDiagnostics(stream, fileName).Mesh;

    /// <inheritdoc cref="ImportFileWithDiagnostics"/>
    public static MeshImportResult ImportWithDiagnostics(Stream stream, string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".stl" => StlReader.ReadWithDiagnostics(stream),
            ".obj" => ObjReader.ReadWithDiagnostics(stream),
            var other => throw new NotSupportedException(
                string.IsNullOrEmpty(other)
                    ? $"'{fileName}' has no file extension, so Meshwright cannot tell which format it is."
                    : $"Meshwright cannot import '{other}' files. Supported formats: {string.Join(", ", SupportedExtensions)}."),
        };
}
