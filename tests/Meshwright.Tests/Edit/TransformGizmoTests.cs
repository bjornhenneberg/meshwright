using System.Numerics;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.Gizmos;
using Xunit;

namespace Meshwright.Tests.Edit;

public class TransformGizmoTests
{
    /// <summary>A pointer event whose ray shoots straight down -Z from the given XY position.</summary>
    private static GizmoPointerEvent RayFromAbove(float x, float y, GizmoPointerButton button) =>
        new(
            new ViewportRay(new Vector3(x, y, 5f), new Vector3(0f, 0f, -1f)),
            Vector2.Zero,
            new Vector2(800f, 600f),
            button,
            GizmoModifierKeys.None,
            null);

    [Fact]
    public void MoveDragAlongAxisProducesOffsetOfDragDistance()
    {
        var gizmo = new TransformGizmo(Vector3.Zero);
        gizmo.SetMode(TransformGizmo.TransformMode.Move);

        // Press on the tip of the X arrow, then slide 1 unit further along +X.
        Assert.True(gizmo.OnPointerPressed(RayFromAbove(0.5f, 0f, GizmoPointerButton.Primary)));
        Assert.Equal(0, gizmo.ActiveAxis);
        gizmo.OnPointerMoved(RayFromAbove(1.5f, 0f, GizmoPointerButton.None));

        Assert.True(gizmo.HasTransform);
        Assert.Equal(1f, gizmo.CurrentTransform.X, 3);
        Assert.Equal(0f, gizmo.CurrentTransform.Y, 3);
        Assert.Equal(0f, gizmo.CurrentTransform.Z, 3);
    }

    [Fact]
    public void PressAwayFromAnyHandleIsDeclined()
    {
        var gizmo = new TransformGizmo(Vector3.Zero);
        gizmo.SetMode(TransformGizmo.TransformMode.Move);

        Assert.False(gizmo.OnPointerPressed(RayFromAbove(5f, 5f, GizmoPointerButton.Primary)));
        Assert.False(gizmo.HasTransform);
    }

    [Fact]
    public void RotateDragTracksSignedAngleSweptAroundAxis()
    {
        var gizmo = new TransformGizmo(Vector3.Zero);
        gizmo.SetMode(TransformGizmo.TransformMode.Rotate);

        // Grab the Z ring at +X and sweep a quarter turn to +Y.
        Assert.True(gizmo.OnPointerPressed(RayFromAbove(0.4f, 0f, GizmoPointerButton.Primary)));
        Assert.Equal(2, gizmo.ActiveAxis);
        gizmo.OnPointerMoved(RayFromAbove(0f, 0.4f, GizmoPointerButton.None));

        Assert.Equal(90f, gizmo.CurrentTransform.X, 2);

        // Dragging back the other way past the start gives a negative angle.
        gizmo.OnPointerMoved(RayFromAbove(0f, -0.4f, GizmoPointerButton.None));
        Assert.Equal(-90f, gizmo.CurrentTransform.X, 2);
    }

    [Fact]
    public void RotateAngleStaysAtZeroWithoutDragMovement()
    {
        var gizmo = new TransformGizmo(Vector3.Zero);
        gizmo.SetMode(TransformGizmo.TransformMode.Rotate);

        gizmo.OnPointerPressed(RayFromAbove(0.4f, 0f, GizmoPointerButton.Primary));
        gizmo.OnPointerMoved(RayFromAbove(0.4f, 0f, GizmoPointerButton.None));
        gizmo.OnPointerMoved(RayFromAbove(0.4f, 0f, GizmoPointerButton.None));

        Assert.Equal(0f, gizmo.CurrentTransform.X, 3);
    }

    [Fact]
    public void ScaleDragUsesRatioOfPointerDistanceToCenter()
    {
        var gizmo = new TransformGizmo(Vector3.Zero);
        gizmo.SetMode(TransformGizmo.TransformMode.Scale);

        Assert.Equal(1f, gizmo.CurrentTransform.X, 3);
        Assert.True(gizmo.OnPointerPressed(RayFromAbove(0.1f, 0f, GizmoPointerButton.Primary)));
        gizmo.OnPointerMoved(RayFromAbove(0.2f, 0f, GizmoPointerButton.None));

        Assert.Equal(2f, gizmo.CurrentTransform.X, 3);
        Assert.Equal(2f, gizmo.CurrentTransform.Y, 3);
        Assert.Equal(2f, gizmo.CurrentTransform.Z, 3);
    }

    [Fact]
    public void ChangingModeResetsTransformToTheModesIdentity()
    {
        var gizmo = new TransformGizmo(Vector3.Zero);
        gizmo.SetMode(TransformGizmo.TransformMode.Move);
        gizmo.OnPointerPressed(RayFromAbove(0.5f, 0f, GizmoPointerButton.Primary));
        gizmo.OnPointerMoved(RayFromAbove(1.5f, 0f, GizmoPointerButton.None));
        gizmo.OnPointerReleased(RayFromAbove(1.5f, 0f, GizmoPointerButton.Primary));
        Assert.True(gizmo.HasTransform);

        gizmo.SetMode(TransformGizmo.TransformMode.Scale);
        Assert.False(gizmo.HasTransform);
        Assert.Equal(Vector3.One, gizmo.CurrentTransform);

        gizmo.SetMode(TransformGizmo.TransformMode.Move);
        Assert.Equal(Vector3.Zero, gizmo.CurrentTransform);
    }

    [Fact]
    public void TransformChangedIsRaisedWhileDragging()
    {
        var gizmo = new TransformGizmo(Vector3.Zero);
        gizmo.SetMode(TransformGizmo.TransformMode.Move);

        int raised = 0;
        gizmo.TransformChanged += (_, _) => raised++;

        gizmo.OnPointerPressed(RayFromAbove(0.5f, 0f, GizmoPointerButton.Primary));
        gizmo.OnPointerMoved(RayFromAbove(1f, 0f, GizmoPointerButton.None));
        gizmo.OnPointerMoved(RayFromAbove(1.5f, 0f, GizmoPointerButton.None));

        Assert.Equal(3, raised);
    }
}
