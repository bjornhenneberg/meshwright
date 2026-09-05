using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using g3;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Xunit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Regression coverage for a bug where every Edit panel's "Before" summary was re-read from the
/// document after the operation had already mutated it, so Before always equalled After. The
/// panels never populate their own BeforeStats a second time - that happens because MainWindow
/// subscribes to <see cref="MeshDocument.Changed"/> (raised synchronously inside
/// <see cref="MeshDocument.Apply"/>) and calls SetDocument on every panel to refresh the viewport
/// and diagnostics. Each test below reproduces that exact wiring - subscribing to Changed and
/// calling SetDocument again, the same as MainWindow.RefreshFromDocument does - so a fix that only
/// worked by accident (e.g. relying on ordering inside the panel alone) would still be caught.
/// </summary>
public class BeforeAfterStatsTests
{
    private static DMesh3 BuildCube(double size)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(1, 0, 0) * size);
        int v110 = mesh.AppendVertex(new Vector3d(1, 1, 0) * size);
        int v010 = mesh.AppendVertex(new Vector3d(0, 1, 0) * size);
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, 1) * size);
        int v101 = mesh.AppendVertex(new Vector3d(1, 0, 1) * size);
        int v111 = mesh.AppendVertex(new Vector3d(1, 1, 1) * size);
        int v011 = mesh.AppendVertex(new Vector3d(0, 1, 1) * size);

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
        }

        Quad(v000, v010, v110, v100);
        Quad(v001, v101, v111, v011);
        Quad(v000, v100, v101, v001);
        Quad(v010, v011, v111, v110);
        Quad(v000, v001, v011, v010);
        Quad(v100, v110, v111, v101);

        return mesh;
    }

    private static void TranslateInPlace(DMesh3 mesh, double dx, double dy, double dz)
    {
        var offset = new Vector3d(dx, dy, dz);
        foreach (int vertexId in mesh.VertexIndices())
        {
            mesh.SetVertex(vertexId, mesh.GetVertex(vertexId) + offset);
        }
    }

    /// <summary>Wires a panel's SetDocument as a MeshDocument.Changed subscriber, matching
    /// MainWindow.RefreshFromDocument - the mechanism that clobbered BeforeStats in the bug.</summary>
    private static void SimulateMainWindowRefreshWiring(MeshDocument document, UserControl panel)
    {
        MethodInfo setDocument = panel.GetType().GetMethod("SetDocument", BindingFlags.Public | BindingFlags.Instance)!;
        document.Changed += (_, _) => setDocument.Invoke(panel, new object?[] { document });
    }

    /// <summary>
    /// Invokes the Click handler and awaits its operation. Apply now runs off the UI thread
    /// (backlog item 13), so asserting immediately after the reflection call returns — as this
    /// test file did before that change — would race the background <c>Task.Run</c> rather than
    /// observing the real outcome; awaiting each panel's <c>PendingOperationForTesting</c> is
    /// what keeps this regression test honest.
    /// </summary>
    private static async Task InvokeOnApplyClick(object panel)
    {
        MethodInfo method = panel.GetType().GetMethod("OnApplyClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(panel, new object?[] { null, null });

        PropertyInfo? pendingProperty = panel.GetType().GetProperty("PendingOperationForTesting", BindingFlags.Public | BindingFlags.Instance);
        if (pendingProperty?.GetValue(panel) is Task pending)
        {
            await pending;
        }
    }

    private static object? GetField(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return field.GetValue(instance);
    }

    [AvaloniaFact]
    public async Task PlaneCutPanel_Apply_BeforeStatsKeepsPreOperationFigures()
    {
        var document = new MeshDocument();
        document.Load(BuildCube(10.0));
        var panel = new PlaneCutPanel();
        SimulateMainWindowRefreshWiring(document, panel);
        panel.SetDocument(document);

        // Textbox defaults (point (0,0,0), normal (0,0,1), Keep mode) cut the cube clean in half.
        await InvokeOnApplyClick(panel);

        string before = panel.FindControl<TextBlock>("BeforeStats")!.Text!;
        string after = panel.FindControl<TextBlock>("AfterStats")!.Text!;

        Assert.Contains("Triangles: 12", before);
        Assert.NotEqual(before, after);
        Assert.DoesNotContain("Triangles: 12", after);
    }

    [AvaloniaFact]
    public async Task HollowPanel_Apply_BeforeStatsKeepsPreOperationFigures()
    {
        var document = new MeshDocument();
        document.Load(BuildCube(20.0));
        var panel = new HollowPanel();
        SimulateMainWindowRefreshWiring(document, panel);
        panel.SetDocument(document);

        panel.FindControl<TextBox>("WallThicknessInput")!.Text = "2.0";
        await InvokeOnApplyClick(panel);

        string before = panel.FindControl<TextBlock>("BeforeStats")!.Text!;
        string after = panel.FindControl<TextBlock>("AfterStats")!.Text!;

        Assert.Contains("Volume: 8000", before);
        Assert.NotEqual(before, after);
    }

    [AvaloniaFact]
    public async Task BooleanPanel_Apply_BeforeStatsKeepsPreOperationFigures()
    {
        var document = new MeshDocument();
        document.Load(BuildCube(1.0));
        var panel = new BooleanPanel();
        SimulateMainWindowRefreshWiring(document, panel);
        panel.SetDocument(document);

        // BooleanPanel now requires a secondary mesh loaded from disk (item 14) rather than the
        // old built-in test fixture cube; OnApplyClick early-returns without one. Drive it the
        // same way production code does - through ApplySecondaryMesh - with a cube offset to
        // overlap the primary so Union provably changes the triangle count/volume.
        var secondary = BuildCube(1.0);
        TranslateInPlace(secondary, 0.5, 0.5, 0.0);
        MethodInfo applySecondaryMesh = typeof(BooleanPanel).GetMethod("ApplySecondaryMesh", BindingFlags.NonPublic | BindingFlags.Instance)!;
        applySecondaryMesh.Invoke(panel, new object?[] { secondary, "secondary.stl", null });
        Assert.True(panel.IsApplyEnabled);

        await InvokeOnApplyClick(panel);

        string before = panel.FindControl<TextBlock>("BeforeStats")!.Text!;
        string after = panel.FindControl<TextBlock>("AfterStats")!.Text!;

        Assert.Contains("Triangles: 12", before);
        Assert.Contains("Volume: 1", before);
        Assert.NotEqual(before, after);
    }

    [AvaloniaFact]
    public async Task TransformPanel_ScaleApply_BeforeStatsKeepsPreOperationFigures()
    {
        var document = new MeshDocument();
        document.Load(BuildCube(10.0));
        var panel = new TransformPanel();
        SimulateMainWindowRefreshWiring(document, panel);
        panel.SetDocument(document);

        var modeCombo = panel.FindControl<ComboBox>("ModeCombo")!;
        modeCombo.SelectedIndex = 2; // Scale
        panel.FindControl<TextBox>("ScaleFactorInput")!.Text = "2.0";
        panel.FindControl<TextBox>("ScaleCenterXInput")!.Text = "0";
        panel.FindControl<TextBox>("ScaleCenterYInput")!.Text = "0";
        panel.FindControl<TextBox>("ScaleCenterZInput")!.Text = "0";

        await InvokeOnApplyClick(panel);

        string before = ((Run)GetField(panel, "BeforeStatsText")!).Text!;
        string after = ((Run)GetField(panel, "AfterStatsText")!).Text!;

        Assert.Contains("bounds: 5", before);
        Assert.NotEqual(before, after);
        Assert.DoesNotContain("bounds: 5", after);
    }

    [AvaloniaFact]
    public async Task RepairPanel_RemoveSmallShells_BeforeStatsKeepsPreOperationFigures()
    {
        // A cube plus a tiny disconnected "noise" shell far away: RemoveSmallShells (with a
        // generous min-volume-fraction) discards the small shell and provably changes the mesh.
        var mesh = BuildCube(10.0);
        int n0 = mesh.AppendVertex(new Vector3d(1000, 1000, 1000));
        int n1 = mesh.AppendVertex(new Vector3d(1000.01, 1000, 1000));
        int n2 = mesh.AppendVertex(new Vector3d(1000, 1000.01, 1000));
        int n3 = mesh.AppendVertex(new Vector3d(1000, 1000, 1000.01));
        mesh.AppendTriangle(n0, n1, n2);
        mesh.AppendTriangle(n0, n1, n3);
        mesh.AppendTriangle(n0, n2, n3);
        mesh.AppendTriangle(n1, n2, n3);

        var document = new MeshDocument();
        document.Load(mesh);
        var panel = new RepairPanel();
        SimulateMainWindowRefreshWiring(document, panel);
        panel.SetDocument(document);

        panel.FindControl<TextBox>("MinVolumeFractionInput")!.Text = "0.5";
        MethodInfo method = typeof(RepairPanel).GetMethod("OnRemoveSmallShellsClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(panel, new object?[] { null, null });
        if (panel.PendingOperationForTesting is { } pending)
        {
            await pending;
        }

        string before = panel.FindControl<TextBlock>("BeforeStats")!.Text!;
        string after = panel.FindControl<TextBlock>("AfterStats")!.Text!;

        Assert.Contains("Shells: 2", before);
        Assert.NotEqual(before, after);
        Assert.DoesNotContain("Shells: 2", after);
    }
}
