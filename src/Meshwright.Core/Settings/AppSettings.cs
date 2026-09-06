using System.Text.Json.Serialization;

namespace Meshwright.Core.Settings;

/// <summary>
/// Everything the app remembers between runs.
///
/// <para>
/// One file, one type. Four things wanted persistence independently — the recent-files list, the
/// selected printer bed, whether the build plate is drawn, and the window's own size and place —
/// and each of the first three had shipped as an in-session field with a comment saying it
/// belonged in <c>settings.json</c> (§11, 2026-09-06). Growing a settings subsystem per feature is
/// how those comments accumulate, so this holds all of them and is written by whoever changes one.
/// </para>
///
/// <para>
/// Every property has a usable default and nothing here is required, so a settings file written by
/// an older build — or a hand-edited one missing half its keys — still loads. Deserialization
/// fills in what it finds and leaves the rest at the value declared here.
/// </para>
/// </summary>
public sealed class AppSettings
{
    /// <summary>How many paths <see cref="RecentFiles"/> keeps. Ten is one menu's worth without a
    /// scrollbar; beyond that the list stops being faster than the file picker.</summary>
    public const int MaxRecentFiles = 10;

    /// <summary>
    /// Files opened, most recent first. Absolute paths — a relative one would resolve against
    /// whatever directory the app happened to be launched from next time, which is not the
    /// directory it was opened from.
    /// </summary>
    public List<string> RecentFiles { get; set; } = new();

    /// <summary>
    /// <see cref="Meshwright.Geometry.Printing.BuildVolume.Name"/> of the selected printer, or
    /// null for the default bed. Stored by name rather than by dimensions so that correcting a
    /// preset's published size in a later version reaches everyone who picked that printer,
    /// instead of pinning them to the number that was wrong when they chose it. A name that no
    /// longer matches any preset falls back to the default rather than failing to load.
    /// </summary>
    public string? BuildVolumeName { get; set; }

    /// <summary>Whether the build plate grid is drawn (View → Show Build Plate).</summary>
    public bool ShowBuildPlate { get; set; } = true;

    /// <summary>The main window's last size and position, or null if it has never been recorded.</summary>
    public WindowPlacement? Window { get; set; }

    /// <summary>
    /// Whether an import whose dimensions look like inches offers to scale itself. The offer is
    /// only ever an offer (see <c>Meshwright.IO.Units.ImportUnits</c>); this switch exists for
    /// someone who genuinely works in 2 mm parts and is tired of being asked.
    /// </summary>
    public bool OfferUnitScaling { get; set; } = true;

    /// <summary>
    /// Puts <paramref name="path"/> at the head of <see cref="RecentFiles"/>, removing any earlier
    /// mention of the same file so re-opening a file promotes it rather than listing it twice, and
    /// trims the list to <see cref="MaxRecentFiles"/>.
    /// </summary>
    public void RememberRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string full = SafeFullPath(path);
        RecentFiles.RemoveAll(existing => PathComparer.Equals(SafeFullPath(existing), full));
        RecentFiles.Insert(0, full);

        if (RecentFiles.Count > MaxRecentFiles)
        {
            RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
        }
    }

    /// <summary>Drops one entry, for the case where the file it names has gone away.</summary>
    public void ForgetRecentFile(string path)
    {
        string full = SafeFullPath(path);
        RecentFiles.RemoveAll(existing => PathComparer.Equals(SafeFullPath(existing), full));
    }

    /// <summary>
    /// Case sensitivity follows the platform: two paths differing only in case are the same file on
    /// Windows and macOS and two different files on Linux, and getting this backwards either
    /// duplicates an entry or silently drops a distinct one.
    /// </summary>
    private static StringComparer PathComparer =>
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// <see cref="Path.GetFullPath(string)"/> throws on a path the OS will not even parse, and a
    /// settings file is user-editable, so a single bad line must not take the list with it.
    /// </summary>
    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }
}

/// <summary>
/// The main window's size and position, in the coordinates Avalonia reports.
/// </summary>
/// <param name="X">Left edge in screen pixels.</param>
/// <param name="Y">Top edge in screen pixels.</param>
/// <param name="Width">Window width.</param>
/// <param name="Height">Window height.</param>
/// <param name="Maximized">Whether the window was maximized, in which case the size and position
/// above are the ones it would restore to.</param>
public sealed record WindowPlacement(
    [property: JsonPropertyName("x")] int X,
    [property: JsonPropertyName("y")] int Y,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("maximized")] bool Maximized)
{
    /// <summary>
    /// Whether this placement is worth restoring. A window that is a few pixels across, or one
    /// whose recorded corner is far off any plausible desktop, would come back invisible or
    /// unreachable — restoring it is worse than ignoring it, so a nonsense record is treated as
    /// no record. The bounds are deliberately loose: this is a sanity floor, not a claim about
    /// which monitors exist.
    /// </summary>
    public bool IsUsable =>
        Width >= 640 && Height >= 480 &&
        Width <= 20000 && Height <= 20000 &&
        X > -20000 && X < 20000 &&
        Y > -20000 && Y < 20000;
}
