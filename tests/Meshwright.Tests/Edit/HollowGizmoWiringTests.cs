using System.Numerics;
using Avalonia.Headless.XUnit;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Meshwright.Rendering.Camera;
using Meshwright.Tests.Gizmos;
using Xunit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Tests for wiring <see cref="HollowGizmo"/> into <see cref="HollowPanel"/> (M4-8: gizmo coverage
/// for Hollow). Mirrors <c>PlaneCutGizmoWiringTests</c>: verifies the panel reports when the gizmo
/// has been dragged and that it prefers the gizmo's value over the manual textbox once touched.
/// </summary>
public class HollowGizmoWiringTests
{
    private static DMesh3 CreateSimpleCube()
    {
        var mesh = new DMesh3(MeshComponents.VertexNormals);

        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(10, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(10, 10, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 10, 0));
        int v4 = mesh.AppendVertex(new Vector3d(0, 0, 10));
        int v5 = mesh.AppendVertex(new Vector3d(10, 0, 10));
        int v6 = mesh.AppendVertex(new Vector3d(10, 10, 10));
        int v7 = mesh.AppendVertex(new Vector3d(0, 10, 10));

        mesh.AppendTriangle(v0, v1, v2);
        mesh.AppendTriangle(v0, v2, v3);
        mesh.AppendTriangle(v4, v6, v5);
        mesh.AppendTriangle(v4, v7, v6);
        mesh.AppendTriangle(v0, v5, v1);
        mesh.AppendTriangle(v0, v4, v5);
        mesh.AppendTriangle(v2, v6, v7);
        mesh.AppendTriangle(v2, v7, v3);
        mesh.AppendTriangle(v0, v3, v7);
        mesh.AppendTriangle(v0, v7, v4);
        mesh.AppendTriangle(v1, v5, v6);
        mesh.AppendTriangle(v1, v6, v2);

        return mesh;
    }

    [AvaloniaFact]
    public void HollowPanel_BeforeGizmoTouched_UsingGizmoValuesIsFalse()
    {
        var panel = new HollowPanel();
        var gizmo = new HollowGizmo(new Vector3(5, 10, 5), Vector3.UnitY);

        panel.SetGizmo(gizmo);

        Assert.False(panel.UsingGizmoValues);
    }

    [AvaloniaFact]
    public void HollowPanel_AfterGizmoDragged_UsingGizmoValuesIsTrue()
    {
        var panel = new HollowPanel();
        var anchor = new Vector3(5, 10, 5);
        var gizmo = new HollowGizmo(anchor, Vector3.UnitY, initialWallThickness: 2f);
        panel.SetGizmo(gizmo);

        var harness = ViewportHarness.Framed(anchor, 20f);
        Assert.True(harness.PressAtWorld(gizmo, gizmo.InnerPoint));
        harness.MoveToPixel(gizmo, harness.RequireProjectToPixel(anchor - Vector3.UnitY * 4f));

        Assert.True(panel.UsingGizmoValues);
    }

    [AvaloniaFact]
    public void HollowPanel_Apply_AfterGizmoDragged_UsesGizmoValueNotTextbox()
    {
        var panel = new HollowPanel();
        var document = new MeshDocument();
        document.Load(CreateSimpleCube());
        panel.SetDocument(document);

        var anchor = new Vector3(5, 10, 5);
        var gizmo = new HollowGizmo(anchor, Vector3.UnitY, initialWallThickness: 2f);
        panel.SetGizmo(gizmo);

        // Drag the handle to a wall thickness (3mm) different from the textbox default (2.0mm).
        var harness = ViewportHarness.Framed(anchor, 20f);
        Assert.True(harness.PressAtWorld(gizmo, gizmo.InnerPoint));
        harness.MoveToPixel(gizmo, harness.RequireProjectToPixel(anchor - Vector3.UnitY * 3f));
        Assert.True(gizmo.WasTouched);
        Assert.True(System.MathF.Abs(3f - gizmo.WallThickness) <= 0.05f,
            $"Expected wall thickness near 3mm, got {gizmo.WallThickness}mm.");

        InvokeApplyClick(panel);

        // The result summary should mention the gizmo's thickness, not the stale textbox
        // default (2.0mm) — formatted the same way HollowOperation formats it ("0.###").
        string expectedThickness = ((double)gizmo.WallThickness).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"{expectedThickness}mm wall thickness", panel.OperationResultMessage);
        Assert.DoesNotContain("2mm wall thickness", panel.OperationResultMessage);
    }

    private static void InvokeApplyClick(HollowPanel panel)
    {
        System.Reflection.MethodInfo method = typeof(HollowPanel).GetMethod("OnApplyClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(panel, new object?[] { null, null });
    }
}
