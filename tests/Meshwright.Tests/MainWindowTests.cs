using System.Reflection;
using Avalonia.Headless.XUnit;
using Meshwright.App;
using Meshwright.IO.Stl;
using Xunit;

namespace Meshwright.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public void Constructing_MainWindow_DoesNotThrow()
    {
        var window = new MainWindow();

        Assert.NotNull(window);
    }

    [Fact]
    public void SampleMeshResource_IsEmbeddedAndParsesAsStl()
    {
        var assembly = typeof(MainWindow).Assembly;
        using var stream = assembly.GetManifestResourceStream("Meshwright.App.Assets.SampleMesh.stl");

        Assert.NotNull(stream);
        var mesh = StlReader.Read(stream!);
        Assert.Equal(4, mesh.TriangleCount);
    }

    [AvaloniaFact]
    public void Constructing_MainWindow_RunsDiagnosticsOnSampleMeshAndPopulatesReport()
    {
        var window = new MainWindow();

        Assert.NotNull(window.CurrentReport);
        Assert.Equal(4, window.CurrentReport!.Statistics.TriangleCount);
    }

    [AvaloniaFact]
    public void Constructing_MainWindow_StatusTextReflectsIssueCount()
    {
        var window = new MainWindow();

        Assert.Contains($"{window.CurrentReport!.Issues.Count} issues found", window.StatusMessage);
    }
}
