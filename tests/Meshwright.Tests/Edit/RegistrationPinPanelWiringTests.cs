using System;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;
using Xunit;

namespace Meshwright.Tests.Edit;

/// <summary>
/// The registration pin controls, from a typed field value through to a measurement of the mesh the
/// operation produced.
///
/// <para>
/// This is the shape §11 (2026-09-06) settled on after three controls shipped reading values they
/// never used: driving the operation directly and asserting it works proves nothing about whether
/// the panel is connected to it. Every test here starts at a control and ends at a number read off
/// the resulting geometry.
/// </para>
/// </summary>
public class RegistrationPinPanelWiringTests
{
    private static readonly Vector2 ViewportSize = new(800, 600);

    private static readonly Matrix4x4 View = Matrix4x4.CreateLookAt(new Vector3(10f, 10f, 60f), new Vector3(10f, 10f, 10f), Vector3.UnitY);

    private static readonly Matrix4x4 Projection =
        Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4f, ViewportSize.X / ViewportSize.Y, 0.01f, 1000f);

    private static GizmoPointerEvent MakeEvent(Vector3 origin, Vector3 direction, GizmoPointerButton button) =>
        new(new ViewportRay(origin, Vector3.Normalize(direction)), Vector2.Zero, ViewportSize, View, Projection, button, GizmoModifierKeys.None, null);

    private static DMesh3 BuildBox(double size)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(size, 0, 0));
        int v110 = mesh.AppendVertex(new Vector3d(size, size, 0));
        int v010 = mesh.AppendVertex(new Vector3d(0, size, 0));
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, size));
        int v101 = mesh.AppendVertex(new Vector3d(size, 0, size));
        int v111 = mesh.AppendVertex(new Vector3d(size, size, size));
        int v011 = mesh.AppendVertex(new Vector3d(0, size, size));

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

    private static (PlaneCutPanel Panel, MeshDocument Document, PlaneCutGizmo Gizmo) MakePanel()
    {
        var document = new MeshDocument();
        document.Load(BuildBox(20.0));
        var panel = new PlaneCutPanel();
        panel.SetDocument(document);
        var gizmo = new PlaneCutGizmo(new Vector3(10f, 10f, 10f));
        panel.SetGizmo(gizmo);
        return (panel, document, gizmo);
    }

    private static T Control<T>(PlaneCutPanel panel, string name)
        where T : class =>
        (T)(object)panel.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;

    private static void SetUpSplitAtZ10(PlaneCutPanel panel)
    {
        Control<ComboBox>(panel, "ModeComboBox").SelectedIndex = 2; // Split Both Sides
        Control<TextBox>(panel, "PlanePointXInput").Text = "10";
        Control<TextBox>(panel, "PlanePointYInput").Text = "10";
        Control<TextBox>(panel, "PlanePointZInput").Text = "10";
        Control<TextBox>(panel, "PlaneNormalXInput").Text = "0";
        Control<TextBox>(panel, "PlaneNormalYInput").Text = "0";
        Control<TextBox>(panel, "PlaneNormalZInput").Text = "1";
    }

    private static void SetPinFields(PlaneCutPanel panel, string diameter, string clearance, string depth)
    {
        Control<CheckBox>(panel, "AddPinCheckBox").IsChecked = true;
        Control<TextBox>(panel, "PinDiameterInput").Text = diameter;
        Control<TextBox>(panel, "PinClearanceInput").Text = clearance;
        Control<TextBox>(panel, "PinDepthInput").Text = depth;
    }

    private static async Task InvokeApplyClick(PlaneCutPanel panel)
    {
        MethodInfo method = typeof(PlaneCutPanel).GetMethod("OnApplyClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(panel, new object?[] { null, null });
        if (panel.PendingOperationForTesting is { } pending)
        {
            await pending;
        }
    }

    private static void InvokePlacePinClick(PlaneCutPanel panel)
    {
        MethodInfo method = typeof(PlaneCutPanel).GetMethod("OnPlacePinViaGizmoClick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(panel, new object?[] { null, null });
    }

    /// <summary>
    /// The peg's far end sits on a plane of its own, one depth below the cut, and nothing else in
    /// this model does. Its widest vertex is the peg's radius and its centroid is the pin's axis, so
    /// both come straight off the mesh.
    /// </summary>
    private static (double Radius, Vector3d Center, int Count) MeasurePegEnd(DMesh3 mesh, double z)
    {
        Vector3d sum = Vector3d.Zero;
        int count = 0;
        foreach (int vid in mesh.VertexIndices())
        {
            Vector3d p = mesh.GetVertex(vid);
            if (Math.Abs(p.z - z) < 1e-9)
            {
                sum += p;
                count++;
            }
        }

        if (count == 0)
        {
            return (0.0, Vector3d.Zero, 0);
        }

        Vector3d center = sum / count;
        double radius = 0.0;
        foreach (int vid in mesh.VertexIndices())
        {
            Vector3d p = mesh.GetVertex(vid);
            if (Math.Abs(p.z - z) < 1e-9)
            {
                radius = Math.Max(radius, new Vector2d(p.x - center.x, p.y - center.y).Length);
            }
        }

        return (radius, center, count);
    }

    [AvaloniaFact]
    public async Task TypedPinDiameter_EndsUpAsThePegsMeasuredRadiusInTheMesh()
    {
        (PlaneCutPanel panel, MeshDocument document, _) = MakePanel();
        SetUpSplitAtZ10(panel);
        SetPinFields(panel, diameter: "6", clearance: "0.2", depth: "5");

        await InvokeApplyClick(panel);

        (double radius, Vector3d center, int count) = MeasurePegEnd(document.Mesh!, 5.0);

        // 32 ring vertices plus the end disc's centre, at exactly the typed radius.
        Assert.Equal(33, count);
        Assert.Equal(3.0, radius, 6);
        Assert.Equal(10.0, center.x, 6);
        Assert.Equal(10.0, center.y, 6);
        Assert.Contains("Registration pin", panel.OperationResultMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ADifferentTypedDiameter_ChangesTheMesh()
    {
        // The same test as above with one number altered: this is what catches a control that is
        // read but not used, which asserting a single hard-coded expectation cannot.
        (PlaneCutPanel panel, MeshDocument document, _) = MakePanel();
        SetUpSplitAtZ10(panel);
        SetPinFields(panel, diameter: "3", clearance: "0.2", depth: "5");

        await InvokeApplyClick(panel);

        Assert.Equal(1.5, MeasurePegEnd(document.Mesh!, 5.0).Radius, 6);
    }

    [AvaloniaFact]
    public async Task TypedPinDepth_EndsUpAsThePegsMeasuredLength()
    {
        (PlaneCutPanel panel, MeshDocument document, _) = MakePanel();
        SetUpSplitAtZ10(panel);
        SetPinFields(panel, diameter: "4", clearance: "0.2", depth: "7");

        await InvokeApplyClick(panel);

        Assert.Equal(33, MeasurePegEnd(document.Mesh!, 3.0).Count);
        Assert.Equal(2.0, MeasurePegEnd(document.Mesh!, 3.0).Radius, 6);
    }

    [AvaloniaFact]
    public async Task BlankPinDepth_MeansOneDiameter()
    {
        (PlaneCutPanel panel, MeshDocument document, _) = MakePanel();
        SetUpSplitAtZ10(panel);
        SetPinFields(panel, diameter: "4", clearance: "0.2", depth: "");

        await InvokeApplyClick(panel);

        Assert.Equal(33, MeasurePegEnd(document.Mesh!, 6.0).Count);
    }

    [AvaloniaFact]
    public void TypedPinDiameter_ReachesTheGizmoSoTheOutlineDrawnIsTheOneCut()
    {
        (PlaneCutPanel panel, _, PlaneCutGizmo gizmo) = MakePanel();

        Control<TextBox>(panel, "PinDiameterInput").Text = "7.5";

        Assert.Equal(7.5f, gizmo.PinDiameter, 4);
    }

    [AvaloniaFact]
    public async Task PinPlacedInTheViewport_WinsOverAutomaticPlacement()
    {
        (PlaneCutPanel panel, MeshDocument document, PlaneCutGizmo gizmo) = MakePanel();
        SetUpSplitAtZ10(panel);
        SetPinFields(panel, diameter: "4", clearance: "0.2", depth: "5");

        InvokePlacePinClick(panel);
        Assert.True(panel.PinPlacementActive);

        // Click on the cut plane, away from its centre. Automatic placement would put the pin at the
        // middle of the square face; the gizmo puts it where the user pointed.
        bool hit = gizmo.OnPointerPressed(MakeEvent(new Vector3(13f, 12f, 60f), new Vector3(0, 0, -1), GizmoPointerButton.Primary));
        Assert.True(hit, "the click should land on the plane gizmo");
        Assert.True(gizmo.PinWasPlaced);
        Assert.Equal(13.0f, gizmo.PinCenter.X, 4);
        Assert.Equal(12.0f, gizmo.PinCenter.Y, 4);
        Assert.Equal(10.0f, gizmo.PinCenter.Z, 4);
        Assert.Contains("13", panel.PinStatusMessage!, StringComparison.Ordinal);

        await InvokeApplyClick(panel);

        (double radius, Vector3d center, int count) = MeasurePegEnd(document.Mesh!, 5.0);
        Assert.Equal(33, count);
        Assert.Equal(2.0, radius, 6);
        Assert.Equal(13.0, center.x, 5);
        Assert.Equal(12.0, center.y, 5);
    }

    [AvaloniaFact]
    public async Task APinTooLargeForTheFace_SaysSoAndLeavesTheMeshAlone()
    {
        (PlaneCutPanel panel, MeshDocument document, _) = MakePanel();
        SetUpSplitAtZ10(panel);
        SetPinFields(panel, diameter: "18", clearance: "0.2", depth: "5");

        int trianglesBefore = document.Mesh!.TriangleCount;
        double volumeBefore = MeshStatistics.Compute(document.Mesh).Volume;

        await InvokeApplyClick(panel);

        Assert.Equal(trianglesBefore, document.Mesh!.TriangleCount);
        Assert.Equal(volumeBefore, MeshStatistics.Compute(document.Mesh).Volume, 9);
        Assert.Contains("does not fit", panel.OperationResultMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task APinInKeepMode_IsRefusedBeforeAnythingIsCut()
    {
        (PlaneCutPanel panel, MeshDocument document, _) = MakePanel();
        SetUpSplitAtZ10(panel);
        Control<ComboBox>(panel, "ModeComboBox").SelectedIndex = 0; // Keep Positive Side
        SetPinFields(panel, diameter: "4", clearance: "0.2", depth: "5");

        int trianglesBefore = document.Mesh!.TriangleCount;

        await InvokeApplyClick(panel);

        Assert.Equal(trianglesBefore, document.Mesh!.TriangleCount);
        Assert.Contains("Split", panel.OperationResultMessage!, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task WithoutTheCheckbox_TheCutIsAnOrdinaryUnpinnedSplit()
    {
        (PlaneCutPanel panel, MeshDocument document, _) = MakePanel();
        SetUpSplitAtZ10(panel);

        await InvokeApplyClick(panel);

        Assert.Null(panel.CurrentPinOptions);
        Assert.Equal(0, MeasurePegEnd(document.Mesh!, 5.0).Count);
        Assert.DoesNotContain("Registration pin", panel.OperationResultMessage!, StringComparison.Ordinal);
    }
}
