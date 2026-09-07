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
        ["SeparatePart"] = ("separate part", "separate parts"),
    };

    /// <summary>
    /// Issues that are actually defects — everything at <see cref="MeshIssueSeverity.Warning"/> or
    /// above. <see cref="MeshIssueSeverity.Info"/> findings describe the model rather than fault it
    /// (a deliberately split model's second part is the case this exists for), and counting them as
    /// issues would have the app report a clean result as a broken one.
    /// </summary>
    public IReadOnlyList<MeshIssue> Defects =>
        Issues.Where(issue => issue.Severity >= MeshIssueSeverity.Warning).ToArray();

    /// <summary>Number of <see cref="Defects"/> — the figure to show a user as "N issues found".</summary>
    public int DefectCount => Defects.Count;

    /// <summary>
    /// One plain-language sentence combining issue counts per category, e.g.
    /// "3 holes, 1 stray shell, 14 flipped faces found."
    /// </summary>
    public string Summary
    {
        get
        {
            string defects = Defects.Count == 0
                ? "No issues found."
                : $"{string.Join(", ", Counts(Defects))} found.";

            IReadOnlyList<MeshIssue> notes = Issues
                .Where(issue => issue.Severity < MeshIssueSeverity.Warning)
                .ToArray();

            return notes.Count == 0
                ? defects
                : $"{defects} {string.Join(", ", Counts(notes))}.";
        }
    }

    private static IEnumerable<string> Counts(IEnumerable<MeshIssue> issues) => issues
        .GroupBy(issue => issue.Category)
        .Select(group => $"{group.Count()} {Phrase(group.Key, group.Count())}");

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
