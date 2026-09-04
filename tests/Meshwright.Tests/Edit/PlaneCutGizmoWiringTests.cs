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

    private static readonly Vector2 ViewportSize = new(800, 600);

    /// <summary>
    /// View/projection for a camera at the synthetic rays' origin distance, so the matrices agree
    /// with the ray these tests hand-build. Camera-driven picking is covered separately in
    /// <c>Gizmos/GizmoPickContractTests</c>; these tests cover panel wiring.
    /// </summary>
    private static readonly Matrix4x4 View = Matrix4x4.CreateLookAt(new Vector3(0f, 0f, 5f), Vector3.Zero, Vector3.UnitY);

    private static readonly Matrix4x4 Projection =
        Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, ViewportSize.X / ViewportSize.Y, 0.01f, 1000f);

    private static GizmoPointerEvent MakeEvent(Vector3 origin, Vector3 direction, GizmoPointerButton button, GizmoModifierKeys modifiers = GizmoModifierKeys.None) =>
        new(new ViewportRay(origin, Vector3.Normalize(direction)), Vector2.Zero, ViewportSize, View, Projection, button, modifiers, null);

    [Fact]
    public void NewGizmo_WasNotTouched()
    {
        var gizmo = new PlaneCutGizmo(Vector3.Zero);

        Assert.False(gizmo.WasTouched);
    }

    [Fact]
    public void OnPointerPressed_RayHitsPlaneSquare_ReturnsTrueEvenFromFarCamera()
    {
        // Regression: picking used to test distance from the *camera* (ray origin) to the
        // plane, not where the click landed - a ray from far away that hits the plane
        // square dead-center used to be rejected outright.
        var gizmo = new PlaneCutGizmo(Vector3.Zero);

        bool pressed = gizmo.OnPointerPressed(MakeEvent(new Vector3(0, 0, 100), new Vector3(0, 0, -1), GizmoPointerButton.Primary));

        Assert.True(pressed);
    }

    [Fact]
    public void OnPointerPressed_RayMissesPlaneSquare_ReturnsFalseEvenFromCloseCamera()
    {
        // Regression: the old distance-to-camera check meant a close-up camera made every
        // click hit the gizmo, even ones nowhere near the rendered square.
        var gizmo = new PlaneCutGizmo(Vector3.Zero);

        // Plane is centered at the origin with normal +Z, extending ±_planeSize (2.0) in
        // its local right/up directions - a ray hitting far outside that footprint should
        // miss even though the camera is close to the plane.
        bool pressed = gizmo.OnPointerPressed(MakeEvent(new Vector3(50, 50, 1), new Vector3(0, 0, -1), GizmoPointerButton.Primary));

        Assert.False(pressed);
    }

    [Fact]
    public void OnPointerPressed_RayParallelToPlane_ReturnsFalse()
    {
        var gizmo = new PlaneCutGizmo(Vector3.Zero);

        bool pressed = gizmo.OnPointerPressed(MakeEvent(new Vector3(0, 0, 3), new Vector3(1, 0, 0), GizmoPointerButton.Primary));

        Assert.False(pressed);
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
