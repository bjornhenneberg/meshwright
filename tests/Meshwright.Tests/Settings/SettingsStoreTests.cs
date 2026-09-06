using System;
using System.Collections.Generic;
using System.IO;
using Meshwright.Core.Settings;
using Xunit;

namespace Meshwright.Tests.Settings;

/// <summary>
/// The settings file's contract: it round-trips, it survives being wrong, and it never throws.
///
/// <para>
/// The "never throws" half is the point of most of these. Settings are a convenience — a
/// read-only config directory or a file someone edited badly must not stop the app opening a mesh
/// — but the failure has to be <em>reported</em>, not swallowed, or a user whose settings never
/// save has no way to find out (§4, §11).
/// </para>
/// </summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"meshwright-settings-{Guid.NewGuid():N}");

    private string FilePath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void AMissingFile_IsNotAProblem_AndYieldsDefaults()
    {
        var store = new SettingsStore(FilePath);

        AppSettings settings = store.Load();

        Assert.Null(store.LoadWarning);
        Assert.Empty(settings.RecentFiles);
        Assert.True(settings.ShowBuildPlate);
        Assert.Null(settings.BuildVolumeName);
        Assert.Null(settings.Window);
    }

    [Fact]
    public void EverythingRemembered_SurvivesARoundTrip()
    {
        var store = new SettingsStore(FilePath);
        var written = new AppSettings
        {
            BuildVolumeName = "Prusa MINI (180 x 180 x 180)",
            ShowBuildPlate = false,
            OfferUnitScaling = false,
            Window = new WindowPlacement(120, 60, 1280, 800, Maximized: true),
            RecentFiles = new List<string> { "/tmp/a.stl", "/tmp/b.obj" },
        };

        Assert.True(store.Save(written));
        AppSettings read = new SettingsStore(FilePath).Load();

        Assert.Equal("Prusa MINI (180 x 180 x 180)", read.BuildVolumeName);
        Assert.False(read.ShowBuildPlate);
        Assert.False(read.OfferUnitScaling);
        Assert.Equal(new WindowPlacement(120, 60, 1280, 800, Maximized: true), read.Window);
        Assert.Equal(new[] { "/tmp/a.stl", "/tmp/b.obj" }, read.RecentFiles);
    }

    [Fact]
    public void AFileFromAnOlderVersion_LoadsWhatItHasAndDefaultsTheRest()
    {
        // The forward-compatibility claim in AppSettings' summary, made concrete: a document with
        // one key it knows and one it has never heard of must not fail the whole load.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, """{ "showBuildPlate": false, "somethingFromTheFuture": 7 }""");

        var store = new SettingsStore(FilePath);
        AppSettings settings = store.Load();

        Assert.Null(store.LoadWarning);
        Assert.False(settings.ShowBuildPlate);
        Assert.True(settings.OfferUnitScaling);
        Assert.Empty(settings.RecentFiles);
    }

    [Fact]
    public void ACorruptFile_StartsWithDefaultsAndSaysSo()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ this is not json");

        var store = new SettingsStore(FilePath);
        AppSettings settings = store.Load();

        Assert.NotNull(store.LoadWarning);
        Assert.Contains(FilePath, store.LoadWarning);
        Assert.Empty(settings.RecentFiles);
    }

    [Fact]
    public void AFileThatIsValidJsonOfTheWrongShape_IsAlsoSurvived()
    {
        // "null" and "[]" deserialize to null rather than throwing, which is a different code path
        // from the corrupt case above and would be a NullReferenceException on first use.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "null");

        AppSettings settings = new SettingsStore(FilePath).Load();

        Assert.NotNull(settings);
        Assert.Empty(settings.RecentFiles);
    }

    [Fact]
    public void AnUnwritableLocation_ReportsInsteadOfThrowing()
    {
        // A path whose parent is an existing *file* cannot be created as a directory on any
        // platform, which is the portable way to make the write fail.
        Directory.CreateDirectory(_directory);
        string blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        var store = new SettingsStore(Path.Combine(blocker, "settings.json"));
        bool saved = store.Save(new AppSettings());

        Assert.False(saved);
        Assert.NotNull(store.SaveWarning);
    }

    [Fact]
    public void ASuccessfulSave_LeavesNoTemporaryFileBehind()
    {
        // The write goes through a sibling .tmp so an interrupted one cannot truncate the real
        // file. If the move ever stopped happening, the settings would silently never change.
        var store = new SettingsStore(FilePath);
        store.Save(new AppSettings { BuildVolumeName = "x" });

        Assert.True(File.Exists(FilePath));
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void TheEnvironmentVariable_OverridesTheDefaultLocation()
    {
        // Relied on by the whole test suite (see SettingsIsolation) to keep it away from the
        // settings of whoever is running it.
        string previous = Environment.GetEnvironmentVariable(SettingsStore.FilePathEnvironmentVariable)!;
        try
        {
            Environment.SetEnvironmentVariable(SettingsStore.FilePathEnvironmentVariable, FilePath);
            Assert.Equal(FilePath, SettingsStore.DefaultFilePath());
            Assert.Equal(FilePath, new SettingsStore().FilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SettingsStore.FilePathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void TheDefaultLocation_IsUnderTheConfigDirectory()
    {
        string previous = Environment.GetEnvironmentVariable(SettingsStore.FilePathEnvironmentVariable)!;
        try
        {
            Environment.SetEnvironmentVariable(SettingsStore.FilePathEnvironmentVariable, null);

            string path = SettingsStore.DefaultFilePath();

            Assert.Equal("settings.json", Path.GetFileName(path));
            Assert.Equal("meshwright", Path.GetFileName(Path.GetDirectoryName(path)));
        }
        finally
        {
            Environment.SetEnvironmentVariable(SettingsStore.FilePathEnvironmentVariable, previous);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
