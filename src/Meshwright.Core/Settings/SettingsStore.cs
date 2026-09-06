using System.Text.Json;
using System.Text.Json.Serialization;

namespace Meshwright.Core.Settings;

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON in the platform's config directory.
///
/// <para>
/// Decided 2026-09-06: plain JSON through <c>System.Text.Json</c>, no dependency and no database.
/// The file is meant to be readable and hand-editable — indented, camel-cased, comments and
/// trailing commas tolerated on read — because the alternative to a user being able to fix their
/// own settings file is a support conversation about deleting it.
/// </para>
///
/// <para>
/// <b>Nothing here throws.</b> Settings are a convenience: a config directory that is read-only,
/// a file half-written by a machine that lost power, a JSON document someone edited badly — none
/// of those are reasons the app should fail to start or fail to open a mesh. Each failure instead
/// leaves a plain-language sentence in <see cref="LoadWarning"/> or <see cref="SaveWarning"/> for
/// the caller to show, because silently starting with default settings and silently discarding
/// every change are exactly the "reported success while being wrong" failures §11 catalogues.
/// </para>
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? DefaultFilePath();
    }

    /// <summary>The file this store reads and writes.</summary>
    public string FilePath { get; }

    /// <summary>Why the last <see cref="Load"/> could not use the file on disk, or null if it
    /// could (a file that simply does not exist yet is not a problem and leaves this null).</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>Why the last <see cref="Save"/> did not reach disk, or null if it did.</summary>
    public string? SaveWarning { get; private set; }

    /// <summary>
    /// <c>~/.config/meshwright/settings.json</c> on Linux, <c>%APPDATA%\meshwright\</c> on
    /// Windows. <see cref="Environment.SpecialFolder.ApplicationData"/> is the one name that
    /// resolves to the right place on all three platforms and honours <c>XDG_CONFIG_HOME</c>
    /// where that is set.
    /// </summary>
    /// <summary>
    /// Environment variable that overrides <see cref="DefaultFilePath"/> outright. It exists for
    /// two callers: someone who wants to run a second configuration side by side, and the test
    /// suite, which must never read or rewrite the settings of the person running it.
    /// </summary>
    public const string FilePathEnvironmentVariable = "MESHWRIGHT_SETTINGS_FILE";

    public static string DefaultFilePath()
    {
        if (Environment.GetEnvironmentVariable(FilePathEnvironmentVariable) is { Length: > 0 } overridden)
        {
            return overridden;
        }

        string configRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(configRoot))
        {
            // A service account with no home directory reports an empty path here rather than
            // failing. Falling back to the working directory keeps the feature working instead of
            // writing to the filesystem root.
            configRoot = Directory.GetCurrentDirectory();
        }

        return Path.Combine(configRoot, "meshwright", "settings.json");
    }

    /// <summary>
    /// Loads the settings file, or returns defaults. Never throws; see <see cref="LoadWarning"/>.
    /// </summary>
    public AppSettings Load()
    {
        LoadWarning = null;

        try
        {
            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new AppSettings();
            }

            // A valid JSON document of the wrong shape ("null", "[]") deserializes to null rather
            // than throwing, so the null-coalesce is doing real work here.
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            LoadWarning = $"Couldn't read settings from {FilePath} ({ex.Message}) — starting with defaults.";
            return new AppSettings();
        }
    }

    /// <summary>
    /// Writes the settings file, creating its directory if needed. Never throws; see
    /// <see cref="SaveWarning"/>.
    ///
    /// <para>
    /// Written to a sibling temporary file and then moved into place, so an interrupted write
    /// leaves the previous settings intact rather than a truncated file that the next
    /// <see cref="Load"/> has to report as corrupt. The move is same-directory, so it is a rename
    /// rather than a copy on every platform.
    /// </para>
    /// </summary>
    public bool Save(AppSettings settings)
    {
        SaveWarning = null;

        string? directory = Path.GetDirectoryName(FilePath);
        string temporaryPath = FilePath + ".tmp";

        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            SaveWarning = $"Couldn't save settings to {FilePath} ({ex.Message}).";

            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception)
            {
                // Cleaning up after a failed write is best-effort; the write failure is what the
                // caller is being told about.
            }

            return false;
        }
    }
}
