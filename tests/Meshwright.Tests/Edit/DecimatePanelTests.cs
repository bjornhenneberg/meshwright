using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using g3;
using Meshwright.App.Views.Edit;
using Xunit;

namespace Meshwright.Tests.Edit;

public class DecimatePanelTests
{
    private static DMesh3 BuildTestMesh()
    {
        // A simple 4-triangle tetrahedron for predictable testing.
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int d = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(a, c, b);
        mesh.AppendTriangle(a, b, d);
        mesh.AppendTriangle(b, c, d);
        mesh.AppendTriangle(c, a, d);

        return mesh;
    }

    [AvaloniaFact]
    public void Constructing_DecimatePanel_DoesNotThrow()
    {
        var panel = new DecimatePanel();

        Assert.NotNull(panel);
        Assert.Null(panel.Mesh);
    }

    [AvaloniaFact]
    public void Setting_Mesh_UpdatesCurrentTriangleCount()
    {
        var panel = new DecimatePanel();
        var mesh = BuildTestMesh();

        panel.Mesh = mesh;

        Assert.Equal(4, panel.CurrentTriangleCount);
    }

    [AvaloniaFact]
    public void ResolvedTargetTriangleCount_WithSmallMesh_ClampsToCurrentCount()
    {
        var panel = new DecimatePanel();
        var mesh = BuildTestMesh(); // 4 triangles
        panel.Mesh = mesh;

        // Default mode is TriangleCount with target "100".
        // Since the mesh only has 4 triangles, the resolved target should clamp to 4 (no reduction possible).
        Assert.Equal(4, panel.ResolvedTargetTriangleCount);
    }

    [AvaloniaFact]
    public void NoMesh_CurrentTriangleCount_ReturnsZero()
    {
        var panel = new DecimatePanel();

        Assert.Equal(0, panel.CurrentTriangleCount);
        Assert.Equal(0, panel.ResolvedTargetTriangleCount);
    }

    // --- Backlog item 21: switching Mode to Percentage left the unit label reading
    // "triangles" — the panel showed "Target: 100 triangles" for a 100-*percent* target.

    [AvaloniaFact]
    public void DefaultMode_TargetUnitLabel_ReadsTriangles()
    {
        var panel = new DecimatePanel();

        Assert.Equal("triangles", panel.FindControl<TextBlock>("TargetUnitLabel")!.Text);
    }

    [AvaloniaFact]
    public void SwitchingToPercentageMode_TargetUnitLabel_ReadsPercent()
    {
        var panel = new DecimatePanel();
        var modeCombo = panel.FindControl<ComboBox>("ModeCombo")!;

        modeCombo.SelectedIndex = 1; // Percentage

        Assert.Equal("%", panel.FindControl<TextBlock>("TargetUnitLabel")!.Text);
    }

    [AvaloniaFact]
    public void SwitchingToPercentageMode_WithNoMeshLoaded_StillUpdatesTheLabel()
    {
        // The label used to only get set in UpdateLivePreview's no-mesh branch, hardcoded to
        // "triangles" — so with no mesh loaded, switching to Percentage left it wrong even
        // though "No mesh loaded" also shows in the live-preview text.
        var panel = new DecimatePanel();
        Assert.Null(panel.Mesh);
        var modeCombo = panel.FindControl<ComboBox>("ModeCombo")!;

        modeCombo.SelectedIndex = 1; // Percentage

        Assert.Equal("%", panel.FindControl<TextBlock>("TargetUnitLabel")!.Text);
    }

    [AvaloniaFact]
    public void SwitchingBackToTriangleCountMode_TargetUnitLabel_ReadsTrianglesAgain()
    {
        var panel = new DecimatePanel();
        var modeCombo = panel.FindControl<ComboBox>("ModeCombo")!;

        modeCombo.SelectedIndex = 1; // Percentage
        modeCombo.SelectedIndex = 0; // back to TriangleCount

        Assert.Equal("triangles", panel.FindControl<TextBlock>("TargetUnitLabel")!.Text);
    }
}
