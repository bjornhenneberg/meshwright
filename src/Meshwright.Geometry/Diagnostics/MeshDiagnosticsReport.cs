using System.Text.RegularExpressions;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>Full result of running mesh statistics plus all detectors over a mesh.</summary>
public sealed record MeshDiagnosticsReport(
    MeshStatistics Statistics,
    IReadOnlyList<MeshIssue> Issues)
{
    // Maps a detector's stable Category identifier to plain-language (singular, plural) phrasing.
    private static readonly Dictionary<string, (string Singular, string Plural)> CategoryPhrases = new()
    {
        ["NonManifoldEdge"] = ("non-manifold edge", "non-manifold edges"),
        ["BoundaryHole"] = ("hole", "holes"),
        ["SelfIntersection"] = ("self-intersection", "self-intersections"),
        ["InvertedNormal"] = ("flipped face", "flipped faces"),
        ["DegenerateTriangle"] = ("degenerate triangle", "degenerate triangles"),
        ["DuplicateVertex"] = ("duplicate vertex location", "duplicate vertex locations"),
        ["DisconnectedShell"] = ("stray shell", "stray shells"),
    };

    /// <summary>
    /// One plain-language sentence combining issue counts per category, e.g.
    /// "3 holes, 1 stray shell, 14 flipped faces found."
    /// </summary>
    public string Summary
    {
        get
        {
            if (Issues.Count == 0)
            {
                return "No issues found.";
            }

            IEnumerable<string> counts = Issues
                .GroupBy(issue => issue.Category)
                .Select(group => $"{group.Count()} {Phrase(group.Key, group.Count())}");

            return $"{string.Join(", ", counts)} found.";
        }
    }

    private static string Phrase(string category, int count)
    {
        if (CategoryPhrases.TryGetValue(category, out var phrase))
        {
            return count == 1 ? phrase.Singular : phrase.Plural;
        }

        string humanized = Humanize(category);
        return count == 1 ? humanized : humanized + "s";
    }

    // Falls back to a readable phrase for categories not in the lookup, e.g. "FooBar" -> "foo bar".
    private static string Humanize(string category) =>
        Regex.Replace(category, "(?<!^)([A-Z])", " $1").ToLowerInvariant();
}
