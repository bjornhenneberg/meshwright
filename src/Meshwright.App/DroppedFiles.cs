using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Meshwright.IO;

namespace Meshwright.App;

/// <summary>
/// What a drag carries, read once and described in a form the window can both act on and say out
/// loud.
///
/// <para>
/// Two shapes of payload have to be handled. A <see cref="DataFormat.File"/> item is the normal
/// one. The other is a plain <c>text/uri-list</c> or a bare path as text, which is what some X11
/// sources send and what a terminal drag produces; taking only the first would refuse drags that
/// carry a perfectly good file name.
/// </para>
/// </summary>
/// <param name="Paths">Every local path the drag carried, in the order it carried them.</param>
/// <param name="Importable">The first path Meshwright can open, or null if none of them are.</param>
public readonly record struct DroppedFiles(IReadOnlyList<string> Paths, string? Importable)
{
    /// <summary>
    /// Why this drag is being refused, in the words the drop hint and the status line use. It
    /// names the file and the formats rather than saying "unsupported": a user who has just been
    /// told "Meshwright opens STL and OBJ files" knows what to do next.
    /// </summary>
    public string RefusalMessage
    {
        get
        {
            if (Paths.Count == 0)
            {
                return "Drop an STL or OBJ file to open it.";
            }

            string extensions = string.Join(" and ", MeshImporter.SupportedExtensions.Select(e => e.TrimStart('.').ToUpperInvariant()));
            return Paths.Count == 1
                ? $"Meshwright can't open {Path.GetFileName(Paths[0])} — {extensions} files only."
                : $"None of those {Paths.Count} files is a mesh — {extensions} files only.";
        }
    }

    public static DroppedFiles From(IDataTransfer? data)
    {
        if (data is null)
        {
            return new DroppedFiles(Array.Empty<string>(), null);
        }

        var paths = new List<string>();

        foreach (IStorageItem item in Safely(data.TryGetFiles) ?? Array.Empty<IStorageItem>())
        {
            if (item.TryGetLocalPath() is { Length: > 0 } local)
            {
                paths.Add(local);
            }
        }

        if (paths.Count == 0)
        {
            paths.AddRange(PathsFromText(Safely(data.TryGetText)));
        }

        return new DroppedFiles(paths, paths.FirstOrDefault(MeshImporter.CanImport));
    }

    /// <summary>
    /// A drag's payload is produced by whatever application started the drag, and reading a format
    /// it advertised can still fail. That is a reason to try the next format, not to fail the
    /// drop, so each read is wrapped rather than each being written out with its own try block.
    /// </summary>
    private static T? Safely<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a <c>text/uri-list</c> body, or a bare path. Comment lines starting with '#' are part
    /// of the uri-list format and are skipped; a <c>file://</c> URI is unescaped, so a drag of
    /// "broken cube.stl" does not arrive as "broken%20cube.stl" and get refused for an extension
    /// it does have.
    /// </summary>
    private static IEnumerable<string> PathsFromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (string rawLine in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (Uri.TryCreate(line, UriKind.Absolute, out Uri? uri) && uri.IsFile)
            {
                yield return uri.LocalPath;
            }
            else if (!line.Contains("://", StringComparison.Ordinal))
            {
                yield return line;
            }
        }
    }
}
