using System.IO;
using System.Linq;
using Meshwright.Core.Settings;
using Xunit;

namespace Meshwright.Tests.Settings;

/// <summary>The recent-files list's rules, and the sanity floor on a remembered window.</summary>
public class AppSettingsTests
{
    [Fact]
    public void TheMostRecentFileIsFirst()
    {
        var settings = new AppSettings();

        settings.RememberRecentFile(Rooted("first.stl"));
        settings.RememberRecentFile(Rooted("second.stl"));

        Assert.Equal(new[] { Rooted("second.stl"), Rooted("first.stl") }, settings.RecentFiles);
    }

    [Fact]
    public void ReopeningAFile_PromotesItRatherThanListingItTwice()
    {
        var settings = new AppSettings();
        settings.RememberRecentFile(Rooted("a.stl"));
        settings.RememberRecentFile(Rooted("b.stl"));

        settings.RememberRecentFile(Rooted("a.stl"));

        Assert.Equal(new[] { Rooted("a.stl"), Rooted("b.stl") }, settings.RecentFiles);
    }

    [Fact]
    public void TwoSpellingsOfOnePath_AreOneEntry()
    {
        // "…/x/../a.stl" and "…/a.stl" are the same file, and a list that shows both is a list
        // that has stopped being a list of files.
        var settings = new AppSettings();

        settings.RememberRecentFile(Rooted("a.stl"));

        // Deliberately NOT put through Rooted, which would normalise it here and leave the test
        // asserting nothing: the round trip through "x/.." is the thing being tested.
        settings.RememberRecentFile(Path.Combine(Path.GetTempPath(), "meshwright-recent", "x", "..", "a.stl"));

        Assert.Single(settings.RecentFiles);
        Assert.Equal(Rooted("a.stl"), settings.RecentFiles[0]);
    }

    [Fact]
    public void ARelativePath_IsStoredAbsolute()
    {
        // A relative path would resolve against whatever directory the app is launched from next
        // time, which is not the directory the file was opened from.
        var settings = new AppSettings();

        settings.RememberRecentFile("model.stl");

        Assert.True(Path.IsPathRooted(settings.RecentFiles[0]));
        Assert.EndsWith("model.stl", settings.RecentFiles[0]);
    }

    [Fact]
    public void TheListIsCappedAndDropsTheOldest()
    {
        var settings = new AppSettings();

        foreach (int i in Enumerable.Range(0, AppSettings.MaxRecentFiles + 5))
        {
            settings.RememberRecentFile(Rooted($"model{i}.stl"));
        }

        Assert.Equal(AppSettings.MaxRecentFiles, settings.RecentFiles.Count);
        Assert.Equal(Rooted("model14.stl"), settings.RecentFiles[0]);
        Assert.DoesNotContain(Rooted("model0.stl"), settings.RecentFiles);
    }

    [Fact]
    public void ForgettingAFile_RemovesIt()
    {
        var settings = new AppSettings();
        settings.RememberRecentFile(Rooted("a.stl"));
        settings.RememberRecentFile(Rooted("b.stl"));

        settings.ForgetRecentFile(Rooted("a.stl"));

        Assert.Equal(new[] { Rooted("b.stl") }, settings.RecentFiles);
    }

    [Fact]
    public void AnEmptyPathIsIgnored()
    {
        var settings = new AppSettings();

        settings.RememberRecentFile("   ");

        Assert.Empty(settings.RecentFiles);
    }

    /// <summary>
    /// A rooted path in the shape the running platform uses, and the shape
    /// <see cref="AppSettings.RememberRecentFile"/> stores. Hard-coded "/tmp/a.stl" literals here
    /// passed on Linux and failed four ways on Windows CI, where <see cref="Path.GetFullPath(string)"/>
    /// turns them into "C:\tmp\a.stl" — the list was right and the expectations were not.
    /// </summary>
    private static string Rooted(string name) =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "meshwright-recent", name));

    [Theory]
    [InlineData(0, 0, 1400, 768, true)]
    [InlineData(-40, -20, 1400, 768, true)]
    [InlineData(0, 0, 10, 10, false)]
    [InlineData(0, 0, 1400, 0, false)]
    [InlineData(900000, 0, 1400, 768, false)]
    public void AWindowPlacementIsOnlyRestoredIfItCouldBeSeen(int x, int y, int width, int height, bool usable)
    {
        // Restoring a 10x10 window, or one whose corner is a million pixels off the desktop, is
        // worse than ignoring the record: the app comes back and there is nothing on screen.
        Assert.Equal(usable, new WindowPlacement(x, y, width, height, Maximized: false).IsUsable);
    }
}
