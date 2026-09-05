using System.Numerics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Meshwright.App;
using Meshwright.App.Views;
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

    /// <summary>
    /// Same single-slot invariant as <see cref="ActivatingSecondPanelsGizmo_ForceDeactivatesTheFirst"/>,
    /// exercised for the Hollow panel added in M4-8: activating Hollow's gizmo while another
    /// panel's gizmo is live must leave exactly one gizmo in the viewport, and it must be
    /// Hollow's — never both, and never the displaced panel's.
    /// </summary>
    [AvaloniaFact]
    public void ActivatingHollowGizmo_WhileAnotherPanelsGizmoIsActive_LeavesOnlyHollowsGizmoInTheViewport()
    {
        var window = new MainWindow();
        object transformPanel = GetField(window, "TransformPanel")!;
        object hollowPanel = GetField(window, "HollowPanel")!;
        object viewport = GetField(window, "Viewport")!;

        InvokePrivate(transformPanel, "OnActivateGizmoClick", null, null);
        Assert.True((bool)GetField(transformPanel, "_gizmoActive")!);

        InvokePrivate(hollowPanel, "OnActivateGizmoClick", null, null);

        Assert.False((bool)GetField(transformPanel, "_gizmoActive")!, "TransformPanel should have been force-deactivated once HollowPanel took the viewport gizmo slot.");
        Assert.True((bool)GetField(hollowPanel, "_gizmoActive")!);

        object? activeGizmo = GetField(viewport, "_gizmo");
        object hollowGizmo = GetField(window, "_hollowGizmo")!;
        Assert.Same(hollowGizmo, activeGizmo);
    }

    /// <summary>
    /// Regression: Reset View (the toolbar button) used to be a no-op after an orbit. FrameMesh()
    /// recomputed the same Target/Distance as before (the mesh hadn't changed), but never reset
    /// the camera's Yaw/Pitch, so an orbit-only drift was invisible to it and the camera stayed
    /// exactly where the user had orbited it to.
    /// </summary>
    [AvaloniaFact]
    public void ResetViewButtonClick_RestoresCameraToFramedPose()
    {
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;
        var button = (Button)GetField(window, "ResetViewButton")!;

        Vector3 framedPosition = viewport.Camera.Position;
        Vector3 framedTarget = viewport.Camera.Target;

        viewport.Camera.Orbit(1.1f, 0.3f);
        Assert.NotEqual(framedPosition, viewport.Camera.Position);

        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(framedTarget, viewport.Camera.Target);
        Assert.Equal(framedPosition.X, viewport.Camera.Position.X, 4);
        Assert.Equal(framedPosition.Y, viewport.Camera.Position.Y, 4);
        Assert.Equal(framedPosition.Z, viewport.Camera.Position.Z, 4);
    }

    /// <summary>
    /// Same regression as <see cref="ResetViewButtonClick_RestoresCameraToFramedPose"/>, but
    /// through the Ctrl+0 keyboard shortcut - a separate entry point (Avalonia's HotKey routing
    /// on the "Reset View" MenuItem) into the same handler, so it needs its own coverage in case
    /// the two ever diverge.
    /// </summary>
    [AvaloniaFact]
    public void ResetViewShortcut_RestoresCameraToFramedPose()
    {
        var window = new MainWindow();
        window.Show();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;

        Vector3 framedPosition = viewport.Camera.Position;
        Vector3 framedTarget = viewport.Camera.Target;

        viewport.Camera.Orbit(-0.7f, -0.2f);
        Assert.NotEqual(framedPosition, viewport.Camera.Position);

        window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);

        Assert.Equal(framedTarget, viewport.Camera.Target);
        Assert.Equal(framedPosition.X, viewport.Camera.Position.X, 4);
        Assert.Equal(framedPosition.Y, viewport.Camera.Position.Y, 4);
        Assert.Equal(framedPosition.Z, viewport.Camera.Position.Z, 4);
    }

    /// <summary>
    /// Regression: the XAML <c>HotKey="Ctrl+0"</c> parsed to <see cref="Key.None"/> - Avalonia's
    /// gesture parser wants the digit key's enum name ("D0"), not the literal digit - so the
    /// shortcut was never bound to anything at all, independent of the OrbitCamera.Frame() bug
    /// above. This guards against that class of typo recurring on this or any future HotKey:
    /// a gesture that silently resolves to no key should fail loudly instead of just not firing.
    /// </summary>
    [AvaloniaFact]
    public void ResetViewMenuItem_HotKeyParsesToARealKey_NotNone()
    {
        var window = new MainWindow();
        var menuItem = (MenuItem)GetField(window, "ResetViewMenuItem")!;

        KeyGesture? hotKey = HotKeyManager.GetHotKey(menuItem);

        Assert.NotNull(hotKey);
        Assert.NotEqual(Key.None, hotKey!.Key);
        Assert.Equal(Key.D0, hotKey.Key);
        Assert.Equal(KeyModifiers.Control, hotKey.KeyModifiers);
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
