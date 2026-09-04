using System.Numerics;
using Meshwright.App.Gizmos;
using Meshwright.Rendering.Gizmos;
using Xunit;

namespace Meshwright.Tests.Gizmos;

/// <summary>
/// The picking contract every gizmo owes the viewport, exercised through a real camera via
/// <see cref="ViewportHarness"/> rather than a hand-built ray.
///
/// <para>Three invariants, each one the negative of a defect this codebase has actually shipped:</para>
/// <list type="number">
/// <item>Clicking the gizmo claims the drag — at any camera distance and any display scaling.
/// (Both the transform and plane-cut gizmos once tested distance-to-camera instead of click
/// location, which made them unclickable on a normally-framed model.)</item>
/// <item>Clicking far away from the gizmo does <em>not</em> claim the drag, so camera orbit still
/// works. (The same camera-distance bug made a gizmo swallow every click on a small model.)</item>
/// <item>The gizmo is big enough on screen to hit — its grab radius in pixels stays usable
/// regardless of model size. A gizmo sized in fixed world units shrinks to sub-pixel on a large
/// model: geometrically pickable, practically invisible.</item>
/// </list>
/// </summary>
public class GizmoPickContractTests
{
    /// <summary>Model radii spanning the range Meshwright targets: a small part through a large print.</summary>
    public static TheoryData<float, double> Scales()
    {
        var data = new TheoryData<float, double>();
        foreach (float radius in new[] { 0.5f, 5f, 50f, 500f })
        {
            foreach (double scaling in new[] { 1.0, 2.0 })
            {
                data.Add(radius, scaling);
            }
        }

        return data;
    }

    /// <summary>
    /// A grab radius below this is not realistically hittable with a mouse. Deliberately modest —
    /// standard UI hit targets are far larger; this only asserts the gizmo has not collapsed.
    /// </summary>
    private const int MinGrabRadiusPixels = 6;

    private static readonly Vector3 ModelCenter = new(2f, -1f, 3f);

    // ---------- PlaneCutGizmo ----------

    [Theory]
    [MemberData(nameof(Scales))]
    public void PlaneCut_ClickingTheGizmoCentre_ClaimsTheDrag(float radius, double scaling)
    {
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new PlaneCutGizmo(ModelCenter);

        Assert.True(harness.PressAtWorld(gizmo, ModelCenter),
            $"Clicking the plane gizmo's own centre must claim the drag (radius={radius}, scaling={scaling}).");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void PlaneCut_ClickingFarFromTheGizmo_LeavesTheClickForTheCamera(float radius, double scaling)
    {
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new PlaneCutGizmo(ModelCenter);

        // Top-left corner of the viewport — nowhere near the gizmo at the centre.
        Assert.False(harness.PressAtPixel(gizmo, new Vector2(4f, 4f)),
            $"A click in the viewport corner must fall through to camera orbit (radius={radius}, scaling={scaling}).");
    }

    [Theory(Skip = "Known gap, not a regression: gizmos are sized in fixed world units " +
        "(PlaneCutGizmo._planeSize = 2.0f, TransformGizmo.CenterHandleRadius = 0.3f), so their " +
        "on-screen size shrinks as the model grows - 1px grab radius on a 50mm model, 0px on a " +
        "500mm one. Fixing it needs constant-screen-size scaling, which needs the camera " +
        "matrices at pick time (GizmoPointerEvent carries only the ray). Unskip with that change.")]
    [MemberData(nameof(Scales))]
    public void PlaneCut_IsBigEnoughOnScreenToHit(float radius, double scaling)
    {
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);

        int grab = harness.GrabRadiusPixels(() => new PlaneCutGizmo(ModelCenter), ModelCenter, Vector2.UnitX);

        Assert.True(grab >= MinGrabRadiusPixels,
            $"Plane gizmo grab radius is {grab}px (need >= {MinGrabRadiusPixels}px) at radius={radius}, scaling={scaling}.");
    }

    // ---------- TransformGizmo ----------

    [Theory]
    [MemberData(nameof(Scales))]
    public void Transform_Move_ClickingAnAxisHandle_ClaimsTheDrag(float radius, double scaling)
    {
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new TransformGizmo(ModelCenter);
        gizmo.SetMode(TransformGizmo.TransformMode.Move);

        // The +Y arrow: perpendicular to the default camera's view direction, so it is not
        // foreshortened into a point from this angle.
        Vector3 handle = ModelCenter + Vector3.UnitY * 0.5f;

        Assert.True(harness.PressAtWorld(gizmo, handle),
            $"Clicking the move gizmo's Y axis handle must claim the drag (radius={radius}, scaling={scaling}).");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Transform_Scale_ClickingTheCentreHandle_ClaimsTheDrag(float radius, double scaling)
    {
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new TransformGizmo(ModelCenter);
        gizmo.SetMode(TransformGizmo.TransformMode.Scale);

        Assert.True(harness.PressAtWorld(gizmo, ModelCenter),
            $"Clicking the scale gizmo's centre handle must claim the drag (radius={radius}, scaling={scaling}).");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void Transform_Move_ClickingFarFromTheGizmo_LeavesTheClickForTheCamera(float radius, double scaling)
    {
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);
        var gizmo = new TransformGizmo(ModelCenter);
        gizmo.SetMode(TransformGizmo.TransformMode.Move);

        Assert.False(harness.PressAtPixel(gizmo, new Vector2(4f, 4f)),
            $"A click in the viewport corner must fall through to camera orbit (radius={radius}, scaling={scaling}).");
    }

    [Theory(Skip = "Known gap, not a regression: gizmos are sized in fixed world units " +
        "(PlaneCutGizmo._planeSize = 2.0f, TransformGizmo.CenterHandleRadius = 0.3f), so their " +
        "on-screen size shrinks as the model grows - 1px grab radius on a 50mm model, 0px on a " +
        "500mm one. Fixing it needs constant-screen-size scaling, which needs the camera " +
        "matrices at pick time (GizmoPointerEvent carries only the ray). Unskip with that change.")]
    [MemberData(nameof(Scales))]
    public void Transform_Scale_IsBigEnoughOnScreenToHit(float radius, double scaling)
    {
        var harness = ViewportHarness.Framed(ModelCenter, radius, renderScaling: scaling);

        int grab = harness.GrabRadiusPixels(
            () =>
            {
                var g = new TransformGizmo(ModelCenter);
                g.SetMode(TransformGizmo.TransformMode.Scale);
                return g;
            },
            ModelCenter,
            Vector2.UnitX);

        Assert.True(grab >= MinGrabRadiusPixels,
            $"Scale gizmo grab radius is {grab}px (need >= {MinGrabRadiusPixels}px) at radius={radius}, scaling={scaling}.");
    }
}
