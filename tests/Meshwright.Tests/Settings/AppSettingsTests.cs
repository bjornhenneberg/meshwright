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

        settings.RememberRecentFile("/tmp/first.stl");
        settings.RememberRecentFile("/tmp/second.stl");

        Assert.Equal(new[] { "/tmp/second.stl", "/tmp/first.stl" }, settings.RecentFiles);
    }

    [Fact]
    public void ReopeningAFile_PromotesItRatherThanListingItTwice()
    {
        var settings = new AppSettings();
        settings.RememberRecentFile("/tmp/a.stl");
        settings.RememberRecentFile("/tmp/b.stl");

        settings.RememberRecentFile("/tmp/a.stl");

        Assert.Equal(new[] { "/tmp/a.stl", "/tmp/b.stl" }, settings.RecentFiles);
    }

    [Fact]
    public void TwoSpellingsOfOnePath_AreOneEntry()
    {
        // "/tmp/x/../a.stl" and "/tmp/a.stl" are the same file, and a list that shows both is a
        // list that has stopped being a list of files.
        var settings = new AppSettings();

        settings.RememberRecentFile("/tmp/a.stl");
        settings.RememberRecentFile("/tmp/x/../a.stl");

        Assert.Single(settings.RecentFiles);
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
            settings.RememberRecentFile($"/tmp/model{i}.stl");
        }

        Assert.Equal(AppSettings.MaxRecentFiles, settings.RecentFiles.Count);
        Assert.Equal("/tmp/model14.stl", settings.RecentFiles[0]);
        Assert.DoesNotContain("/tmp/model0.stl", settings.RecentFiles);
    }

    [Fact]
    public void ForgettingAFile_RemovesIt()
    {
        var settings = new AppSettings();
        settings.RememberRecentFile("/tmp/a.stl");
        settings.RememberRecentFile("/tmp/b.stl");

        settings.ForgetRecentFile("/tmp/a.stl");

        Assert.Equal(new[] { "/tmp/b.stl" }, settings.RecentFiles);
    }

    [Fact]
    public void AnEmptyPathIsIgnored()
    {
        var settings = new AppSettings();

        settings.RememberRecentFile("   ");

        Assert.Empty(settings.RecentFiles);
    }

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
