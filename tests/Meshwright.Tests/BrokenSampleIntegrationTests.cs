using System;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Headless.XUnit;
using Meshwright.App;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// End-to-end regression test proving Inspect works against a real, on-disk broken STL file
/// (holes, a stray shell, and a flipped face) through the real MainWindow/MeshDocument load
/// path, rather than exercising detectors directly against in-memory fixtures.
/// </summary>
public class BrokenSampleIntegrationTests
{
    [AvaloniaFact]
    public void LoadingBrokenSample_ThroughRealPipeline_FlagsHoleShellAndInvertedNormal()
    {
        var window = new MainWindow();

        window.LoadFileForTesting(GetFixturePath("BrokenSample.stl"));

        var report = window.CurrentReport;
        Assert.NotNull(report);
        Assert.Contains(report!.Issues, issue => issue.Category == "BoundaryHole");
        Assert.Contains(report.Issues, issue => issue.Category == "DisconnectedShell");
        Assert.Contains(report.Issues, issue => issue.Category == "InvertedNormal");

        Assert.False(string.IsNullOrWhiteSpace(window.StatusMessage));
        Assert.False(string.IsNullOrWhiteSpace(window.SummaryMessage));
        Assert.Contains("hole", window.SummaryMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shell", window.SummaryMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetFixturePath(string fileName, [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Fixtures", fileName);
}
