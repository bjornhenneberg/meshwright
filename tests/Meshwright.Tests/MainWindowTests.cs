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

    [AvaloniaFact]
    public void Constructing_MainWindow_WithEditPanels_DoesNotThrow()
    {
        // This test verifies that the new Edit panel initialization in MainWindow
        // completes successfully without throwing exceptions.
        var window = new MainWindow();

        Assert.NotNull(window);
        Assert.NotNull(window.CurrentReport);
    }

    [AvaloniaFact]
    public void FreshlyLoadedMesh_HasNothingToUndoOrRedo()
    {
        var window = new MainWindow();

        Assert.Equal(string.Empty, window.UndoRedoStatusMessage);
    }

    [AvaloniaFact]
    public void Undo_WithNoHistory_ReportsNothingToUndo()
    {
        var window = new MainWindow();

        window.TriggerUndoForTesting();

        Assert.Equal("Nothing to undo", window.StatusMessage);
    }

    [AvaloniaFact]
    public void Redo_WithNoHistory_ReportsNothingToRedo()
    {
        var window = new MainWindow();

        window.TriggerRedoForTesting();

        Assert.Equal("Nothing to redo", window.StatusMessage);
    }
}
