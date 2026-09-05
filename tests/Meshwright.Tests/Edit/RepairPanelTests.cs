using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using g3;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Xunit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Covers the Repair panel's wiring to <see cref="MeshDocument"/>. The repair algorithms
/// themselves are tested under Repair/ and Operations/; what matters here is that each button
/// reaches them at all — the panel exists because every one of these operations was implemented,
/// tested, and then left with no way to run it from the app.
/// </summary>
public class RepairPanelTests
{
    /// <summary>A cube with one face missing, so it has a boundary hole to close.</summary>
    private static DMesh3 BuildCubeWithMissingFace(double size)
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
        Quad(v000, v100, v101, v001);
        Quad(v010, v011, v111, v110);
        Quad(v000, v001, v011, v010);
        Quad(v100, v110, v111, v101);
        // Top face (v001, v101, v111, v011) deliberately left open.

        return mesh;
    }

    private static (RepairPanel Panel, MeshDocument Document) PanelWithHolyCube()
    {
        var panel = new RepairPanel();
        var document = new MeshDocument();
        document.Load(BuildCubeWithMissingFace(20.0));
        panel.SetDocument(document);
        return (panel, document);
    }

    private static void Click(RepairPanel panel, string buttonName)
    {
        Button? button = panel.FindControl<Button>(buttonName);
        Assert.NotNull(button);
        button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaFact]
    public void Constructing_RepairPanel_DoesNotThrow()
    {
        var panel = new RepairPanel();

        Assert.NotNull(panel);
    }

    [AvaloniaFact]
    public void SetDocument_ShowsTriangleShellAndIssueCounts()
    {
        (RepairPanel panel, _) = PanelWithHolyCube();

        string? before = panel.FindControl<TextBlock>("BeforeStats")?.Text;

        Assert.NotNull(before);
        Assert.Contains("Triangles: 10", before);
        Assert.Contains("Shells:", before);
        Assert.Contains("Issues:", before);
    }

    [AvaloniaFact]
    public void FillHoles_ClosesTheOpenFace()
    {
        (RepairPanel panel, MeshDocument document) = PanelWithHolyCube();
        int holesBefore = CountHoleIssues(document);
        Assert.True(holesBefore > 0, "fixture should start with an open boundary");

        Click(panel, "FillHolesButton");

        Assert.Equal(0, CountHoleIssues(document));
        Assert.True(document.CanUndo);
    }

    [AvaloniaFact]
    public void AutoRepair_RunsAsOneUndoableStepAndReportsWhatItDid()
    {
        (RepairPanel panel, MeshDocument document) = PanelWithHolyCube();

        Click(panel, "AutoRepairButton");

        Assert.Equal(0, CountHoleIssues(document));
        Assert.NotNull(panel.OperationResultMessage);
        Assert.NotEmpty(panel.OperationResultMessage!);

        // One pipeline run is one undo, not one per step.
        Assert.True(document.Undo());
        Assert.False(document.CanUndo);
        Assert.True(CountHoleIssues(document) > 0);
    }

    [AvaloniaTheory]
    [InlineData("RemoveDegenerateButton")]
    [InlineData("RemoveSmallShellsButton")]
    [InlineData("ResolveSelfIntersectionsButton")]
    [InlineData("UnifyNormalsButton")]
    [InlineData("FillHolesButton")]
    [InlineData("AutoRepairButton")]
    public void EveryRepairButton_ReachesAnOperationRatherThanErroring(string buttonName)
    {
        (RepairPanel panel, _) = PanelWithHolyCube();

        Click(panel, buttonName);

        Assert.NotNull(panel.OperationResultMessage);
        Assert.DoesNotContain("Error:", panel.OperationResultMessage!);
        Assert.DoesNotContain("No mesh loaded", panel.OperationResultMessage!);
    }

    [AvaloniaFact]
    public void UnparseableParameter_ReportsItInsteadOfThrowing()
    {
        (RepairPanel panel, MeshDocument document) = PanelWithHolyCube();
        TextBox? input = panel.FindControl<TextBox>("MinVolumeFractionInput");
        Assert.NotNull(input);
        input!.Text = "not a number";

        Click(panel, "RemoveSmallShellsButton");

        Assert.Contains("Error:", panel.OperationResultMessage!);
        Assert.False(document.CanUndo);
    }

    private static int CountHoleIssues(MeshDocument document) =>
        document.Report!.Issues.Count(issue => issue.Category == "BoundaryHole");
}
