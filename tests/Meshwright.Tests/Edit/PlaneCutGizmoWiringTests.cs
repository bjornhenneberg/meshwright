using System.Numerics;
using System.Reflection;
using Avalonia.Headless.XUnit;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;
using Xunit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// Tests for wiring the interactive <see cref="PlaneCutGizmo"/> into <see cref="PlaneCutPanel"/>
/// (M4 batch: gizmo-first plane cut). Verifies the gizmo reports when it has been dragged and that
/// the panel prefers its values over the manual textboxes once touched.
/// </summary>
public class PlaneCutGizmoWiringTests
{
    private static DMesh3 CreateSimpleCube()
    {
        var mesh = new DMesh3(MeshComponents.VertexNormals);

        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v4 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int v5 = mesh.AppendVertex(new Vector3d(1, 0, 1));
        int v6 = mesh.AppendVertex(new Vector3d(1, 1, 1));
        int v7 = mesh.AppendVertex(new Vector3d(0, 1, 1));

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

    private static GizmoPointerEvent MakeEvent(Vector3 origin, Vector3 direction, GizmoPointerButton button, GizmoModifierKeys modifiers = GizmoModifierKeys.None) =>
        new(new ViewportRay(origin, Vector3.Normalize(direction)), Vector2.Zero, new Vector2(800, 600), button, modifiers, null);

    [Fact]
    public void NewGizmo_WasNotTouched()
    {
        var gizmo = new PlaneCutGizmo(Vector3.Zero);

        Assert.False(gizmo.WasTouched);
    }

    [Fact]
    public void DraggingGizmo_TranslateAlongNormal_MarksTouchedAndRaisesChanged()
    {
        var gizmo = new PlaneCutGizmo(Vector3.Zero);
        bool changedRaised = false;
        gizmo.Changed += (s, e) => changedRaised = true;

        bool pressed = gizmo.OnPointerPressed(MakeEvent(new Vector3(0, 0, 3), new Vector3(0, 0, -1), GizmoPointerButton.Primary, GizmoModifierKeys.Shift));
        Assert.True(pressed);

        bool moved = gizmo.OnPointerMoved(MakeEvent(new Vector3(0, 0, 3), new Vector3(0, 0, -1), GizmoPointerButton.None, GizmoModifierKeys.Shift));

        Assert.True(moved);
        Assert.True(gizmo.WasTouched);
        Assert.True(changedRaised);
    }

    [Fact]
    public void DraggingGizmo_RotateNormal_MarksTouchedAndRaisesChanged()
    {
        var gizmo = new PlaneCutGizmo(Vector3.Zero);
        bool changedRaised = false;
        gizmo.Changed += (s, e) => changedRaised = true;

        gizmo.OnPointerPressed(MakeEvent(new Vector3(0, 0, 3), new Vector3(0, 0, -1), GizmoPointerButton.Primary));
        bool moved = gizmo.OnPointerMoved(MakeEvent(new Vector3(2, 2, 3), new Vector3(0, 0, -1), GizmoPointerButton.None));

        Assert.True(moved);
        Assert.True(gizmo.WasTouched);
        Assert.True(changedRaised);
        Assert.NotEqual(Vector3.UnitZ, gizmo.PlaneNormal);
    }

    [AvaloniaFact]
    public void PlaneCutPanel_BeforeGizmoTouched_UsingGizmoValuesIsFalse()
    {
        var panel = new PlaneCutPanel();
        var gizmo = new PlaneCutGizmo(Vector3.Zero);

        panel.SetGizmo(gizmo);

        Assert.False(panel.UsingGizmoValues);
    }

    [AvaloniaFact]
    public void PlaneCutPanel_AfterGizmoDragged_UsingGizmoValuesIsTrue()
    {
        var panel = new PlaneCutPanel();
        var gizmo = new PlaneCutGizmo(Vector3.Zero);
        panel.SetGizmo(gizmo);

        gizmo.OnPointerPressed(MakeEvent(new Vector3(0, 0, 3), new Vector3(0, 0, -1), GizmoPointerButton.Primary, GizmoModifierKeys.Shift));
        gizmo.OnPointerMoved(MakeEvent(new Vector3(0, 0, 3), new Vector3(0, 0, -1), GizmoPointerButton.None, GizmoModifierKeys.Shift));

        Assert.True(panel.UsingGizmoValues);
    }

    [AvaloniaFact]
    public void PlaneCutPanel_Apply_AfterGizmoDragged_UsesGizmoValuesNotTextboxes()
    {
        var panel = new PlaneCutPanel();
        var document = new MeshDocument();
        document.Load(CreateSimpleCube());
        panel.SetDocument(document);

        var gizmo = new PlaneCutGizmo(new Vector3(0.5f, 0.5f, 0.5f));
        panel.SetGizmo(gizmo);

        // Drag the gizmo along its normal (+Z) — this should move the cut plane away from the
        // textbox-default plane (point (0,0,0), normal (0,0,1)) that Apply would otherwise use.
        gizmo.OnPointerPressed(MakeEvent(new Vector3(0.5f, 0.5f, 3f), new Vector3(0, 0, -1), GizmoPointerButton.Primary, GizmoModifierKeys.Shift));
        gizmo.OnPointerMoved(MakeEvent(new Vector3(0.5f, 0.5f, 3f), new Vector3(0, 0.3f, -1f), GizmoPointerButton.None, GizmoModifierKeys.Shift));
        Assert.True(gizmo.WasTouched);

        InvokeApplyClick(panel);

        var expectedPoint = new Vector3d(gizmo.PlanePosition.X, gizmo.PlanePosition.Y, gizmo.PlanePosition.Z);
        var expectedNormal = new Vector3d(gizmo.PlaneNormal.X, gizmo.PlaneNormal.Y, gizmo.PlaneNormal.Z).Normalized;

        Assert.Equal(expectedPoint.x, panel.CurrentPlanePoint.x, 3);
        Assert.Equal(expectedPoint.y, panel.CurrentPlanePoint.y, 3);
        Assert.Equal(expectedPoint.z, panel.CurrentPlanePoint.z, 3);
        Assert.Equal(expectedNormal.x, panel.CurrentPlaneNormal.x, 3);
        Assert.Equal(expectedNormal.y, panel.CurrentPlaneNormal.y, 3);
        Assert.Equal(expectedNormal.z, panel.CurrentPlaneNormal.z, 3);

        // The plane used should not be the stale textbox default (point (0,0,0)).
        Assert.NotEqual(0.0, panel.CurrentPlanePoint.z, 3);
    }

    private static void InvokeApplyClick(PlaneCutPanel panel)
    {
        MethodInfo method = typeof(PlaneCutPanel).GetMethod("OnApplyClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(panel, new object?[] { null, null });
    }
}
