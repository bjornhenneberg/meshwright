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

    /// <summary>
    /// Regression: Viewport.Gizmo is a single slot shared by DrainHolePanel, PlaneCutPanel and
    /// TransformPanel. Before this fix, activating a second panel's gizmo silently stole the
    /// viewport slot from whichever panel activated first, but that first panel's own
    /// "gizmo active" UI state (button text, status text) was never told, so it kept claiming
    /// its gizmo was live and interactive when the viewport had actually moved on.
    /// </summary>
    [AvaloniaFact]
    public void ActivatingSecondPanelsGizmo_ForceDeactivatesTheFirst()
    {
        var window = new MainWindow();
        object planeCutPanel = GetField(window, "PlaneCutPanel")!;
        object transformPanel = GetField(window, "TransformPanel")!;

        InvokePrivate(planeCutPanel, "OnSetViaGizmoClick", null, null);
        Assert.True((bool)GetField(planeCutPanel, "_gizmoActive")!);

        InvokePrivate(transformPanel, "OnActivateGizmoClick", null, null);

        Assert.False((bool)GetField(planeCutPanel, "_gizmoActive")!, "PlaneCutPanel should have been force-deactivated once TransformPanel took the viewport gizmo slot.");
        Assert.True((bool)GetField(transformPanel, "_gizmoActive")!);
    }

    [AvaloniaFact]
    public void ExportFileForTesting_WritesTheLoadedMeshToDisk()
    {
        var window = new MainWindow();
        string path = Path.Combine(Path.GetTempPath(), $"meshwright-export-{Guid.NewGuid():N}.stl");

        try
        {
            window.ExportFileForTesting(path);

            Assert.True(File.Exists(path));
            var mesh = StlReader.ReadFile(path);
            Assert.Equal(window.CurrentReport!.Statistics.TriangleCount, mesh.TriangleCount);
            Assert.Contains("Exported", window.StatusMessage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void ExportFileForTesting_RejectsAnUnsupportedExtension()
    {
        var window = new MainWindow();
        string path = Path.Combine(Path.GetTempPath(), $"meshwright-export-{Guid.NewGuid():N}.3mf");

        window.ExportFileForTesting(path);

        Assert.Contains("Failed to export", window.StatusMessage);
        Assert.False(File.Exists(path));
    }

    private static object? GetField(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return field.GetValue(instance);
    }

    private static void InvokePrivate(object instance, string methodName, params object?[] args)
    {
        MethodInfo method = instance.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(instance.GetType().FullName, methodName);
        method.Invoke(instance, args);
    }
}
