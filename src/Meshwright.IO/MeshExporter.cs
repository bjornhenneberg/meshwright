using g3;
using Meshwright.Geometry.Edit;
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

    /// <summary>
    /// File-picker patterns for the supported formats. GTK's (and other Linux toolkits') file
    /// choosers match <c>FilePickerFileType</c> patterns case-sensitively, so a lower-case-only
    /// pattern list makes a file like "Model.STL" simply not appear in the save dialog's default
    /// filter, with nothing telling the user why. Each extension is therefore listed in
    /// lower-case, upper-case, and capitalized form; export itself already accepts any case (see
    /// <see cref="Export"/>), so this only affects what the dialog shows.
    /// </summary>
    public static IReadOnlyList<string> SupportedPatterns { get; } = new[]
    {
        "*.stl", "*.STL", "*.Stl",
        "*.obj", "*.OBJ", "*.Obj",
    };

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
    /// Writes one file per connected shell — one file per printable part — beside
    /// <paramref name="basePath"/>, named after it with a <c>-part1</c>, <c>-part2</c>… suffix, and
    /// returns the paths written in the order the parts were numbered (largest part first, see
    /// <see cref="MeshShells.Separate"/>).
    ///
    /// <para>
    /// A split model is one document holding two solids, and a printer needs them as two objects.
    /// Handing a slicer the single file works — every slicer can split a multi-body STL — but it
    /// makes the user do a step this app already has the answer to, and OBJ has no notion of the
    /// separation at all. The format comes from <paramref name="basePath"/>'s extension, exactly as
    /// for <see cref="ExportFile"/>.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> ExportParts(string basePath, DMesh3 mesh)
    {
        IReadOnlyList<DMesh3> parts = MeshShells.Separate(mesh);

        string directory = Path.GetDirectoryName(Path.GetFullPath(basePath)) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(basePath);
        string extension = Path.GetExtension(basePath);

        var written = new List<string>(parts.Count);
        for (int i = 0; i < parts.Count; i++)
        {
            string path = Path.Combine(directory, $"{stem}-part{i + 1}{extension}");
            ExportFile(path, parts[i]);
            written.Add(path);
        }

        return written;
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
