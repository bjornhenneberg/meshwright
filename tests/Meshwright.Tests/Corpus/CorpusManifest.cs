using System.Globalization;

namespace Meshwright.Tests.Corpus;

/// <summary>One row of <c>tests/corpus/manifest.tsv</c>.</summary>
/// <param name="FileName">Local file name under <c>tests/corpus/files</c>.</param>
/// <param name="Source">Upstream dataset or repository.</param>
/// <param name="License">Licence recorded for this file, or <c>see-upstream</c> when per-file licensing is not published.</param>
/// <param name="GroundTruth">
/// Independent per-file measurements published by the upstream dataset, as key/value pairs. Empty
/// for sources that publish none — only the Thingi10K rows carry ground truth.
/// </param>
internal sealed record CorpusEntry(
    string FileName,
    string Source,
    string License,
    IReadOnlyDictionary<string, int> GroundTruth);

/// <summary>
/// Reads <c>tests/corpus/manifest.tsv</c>. The manifest is the single source of truth for what the
/// corpus contains, so the fetch script and the tests cannot disagree about it.
/// </summary>
internal static class CorpusManifest
{
    internal static IReadOnlyList<CorpusEntry> Entries { get; } = Parse();

    private static IReadOnlyList<CorpusEntry> Parse()
    {
        string? dir = CorpusPaths.Directory;
        string? manifest = dir is null ? null : Path.Combine(Path.GetDirectoryName(dir)!, "manifest.tsv");
        if (manifest is null || !File.Exists(manifest))
        {
            return Array.Empty<CorpusEntry>();
        }

        var entries = new List<CorpusEntry>();
        foreach (string line in File.ReadLines(manifest))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] columns = line.Split('\t');
            if (columns.Length < 4)
            {
                continue;
            }

            entries.Add(new CorpusEntry(
                columns[0],
                columns[3],
                columns.Length > 4 ? columns[4] : "unknown",
                columns.Length > 5 ? ParseGroundTruth(columns[5]) : new Dictionary<string, int>()));
        }

        return entries;
    }

    /// <summary>Parses the notes column's <c>key=value;key=value</c> measurements, ignoring free text.</summary>
    private static IReadOnlyDictionary<string, int> ParseGroundTruth(string notes)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string pair in notes.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq > 0 && int.TryParse(pair[(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                values[pair[..eq].Trim()] = value;
            }
        }

        return values;
    }
}
