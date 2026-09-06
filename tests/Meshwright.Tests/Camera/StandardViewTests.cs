using System;
using System.Numerics;
using Meshwright.Rendering.Camera;
using Xunit;

namespace Meshwright.Tests.Camera;

/// <summary>
/// Each view preset is checked by what it shows, not by the enum it was handed: the direction the
/// camera looks, and which world axes come out right and up on screen. Two failure modes this
/// pins directly:
/// <list type="bullet">
/// <item>A top view built as "look down -Z with up = +Z" — a degenerate look-at whose matrix is
/// all NaN, drawing nothing at all while every flag says Top.</item>
/// <item>A preset that writes some of the camera's orientation and not the rest, the way
/// <c>Frame()</c> once reset Target and Distance but not Yaw/Pitch and made Reset View look like
/// a no-op (§11, 2026-09-05).</item>
/// </list>
/// </summary>
public class StandardViewTests
{
    private const float Aspect = 16f / 9f;
    private static readonly Vector3 Center = new(2f, -1f, 3f);
    private const float Radius = 10f;

    private static OrbitCamera Framed()
    {
        var camera = new OrbitCamera();
        camera.Frame(Center, Radius);
        return camera;
    }

    /// <summary>The world direction the camera looks along.</summary>
    private static Vector3 Forward(OrbitCamera camera) => Vector3.Normalize(camera.Target - camera.Position);

    /// <summary>Screen right and up in world space, read off the view matrix that is actually rendered.</summary>
    private static (Vector3 Right, Vector3 Up) ScreenAxes(OrbitCamera camera)
    {
        Matrix4x4 view = camera.GetViewMatrix();
        return (new Vector3(view.M11, view.M21, view.M31), new Vector3(view.M12, view.M22, view.M32));
    }

    /// <summary>view direction, screen right, screen up — in the Z-up print-bed frame.</summary>
    public static TheoryData<StandardView, Vector3, Vector3, Vector3> Orientations() => new()
    {
        { StandardView.Front, new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1) },
        { StandardView.Back, new Vector3(0, -1, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, 1) },
        { StandardView.Right, new Vector3(-1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) },
        { StandardView.Left, new Vector3(1, 0, 0), new Vector3(0, -1, 0), new Vector3(0, 0, 1) },
        { StandardView.Top, new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(0, 1, 0) },
        { StandardView.Bottom, new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, -1, 0) },
    };

    [Theory]
    [MemberData(nameof(Orientations))]
    public void Preset_LooksAlongTheAxisItClaims_WithTheExpectedScreenAxes(
        StandardView view, Vector3 expectedForward, Vector3 expectedRight, Vector3 expectedUp)
    {
        var camera = Framed();

        camera.SetStandardView(view);

        (Vector3 right, Vector3 up) = ScreenAxes(camera);
        AssertDirection(expectedForward, Forward(camera), $"{view} view direction");
        AssertDirection(expectedRight, right, $"{view} screen right");
        AssertDirection(expectedUp, up, $"{view} screen up");
    }

    [Theory]
    [MemberData(nameof(Orientations))]
    public void Preset_ProducesANonDegenerateViewMatrix(
        StandardView view, Vector3 unusedForward, Vector3 unusedRight, Vector3 unusedUp)
    {
        _ = (unusedForward, unusedRight, unusedUp);

        var camera = Framed();

        camera.SetStandardView(view);
        Matrix4x4 viewMatrix = camera.GetViewMatrix();

        Assert.All(MatrixElements(viewMatrix), element =>
        {
            Assert.False(float.IsNaN(element), $"{view} produced a NaN in the view matrix (degenerate up vector).");
            Assert.False(float.IsInfinity(element));
        });
        Assert.True(Matrix4x4.Invert(viewMatrix, out _), $"{view}'s view matrix is not invertible.");

        (Vector3 right, Vector3 up) = ScreenAxes(camera);
        Assert.Equal(1f, right.Length(), 4);
        Assert.Equal(1f, up.Length(), 4);
        Assert.True(MathF.Abs(Vector3.Dot(Vector3.Normalize(up), Forward(camera))) < 1e-4f,
            $"{view}'s up vector is parallel to the view direction.");
    }

    [Fact]
    public void Isometric_ShowsAllThreeAxesAtEqualScreenLength()
    {
        // The point of an isometric view: no axis is foreshortened relative to the others.
        var camera = Framed();
        camera.SetStandardView(StandardView.Isometric);
        camera.ProjectionMode = ProjectionMode.Orthographic;

        Matrix4x4 viewProjection = camera.GetViewMatrix() * camera.GetProjectionMatrix(Aspect);

        float x = ScreenLengthOfUnitAxis(camera, viewProjection, Vector3.UnitX);
        float y = ScreenLengthOfUnitAxis(camera, viewProjection, Vector3.UnitY);
        float z = ScreenLengthOfUnitAxis(camera, viewProjection, Vector3.UnitZ);

        Assert.Equal(x, y, 4);
        Assert.Equal(x, z, 4);
        Assert.True(x > 0f);
    }

    [Fact]
    public void Isometric_MatchesTheOrientationResetViewRestores()
    {
        // Two menu entries that differed by a few degrees would be a distinction no user could
        // name; Isometric is defined as the orientation Frame() produces.
        var framed = Framed();
        var preset = Framed();

        preset.Orbit(1.2f, -0.4f);
        preset.SetStandardView(StandardView.Isometric);

        Assert.Equal(framed.Yaw, preset.Yaw, 5);
        Assert.Equal(framed.Pitch, preset.Pitch, 5);
    }

    [Theory]
    [MemberData(nameof(AllViews))]
    public void Preset_ThenResetView_ThenTheSamePreset_LandsInTheSamePose(StandardView view)
    {
        var camera = Framed();
        camera.SetStandardView(view);
        Vector3 firstPosition = camera.Position;
        Vector3 firstTarget = camera.Target;
        float firstDistance = camera.Distance;

        // Wander: orbit, pan and zoom, then Reset View (which re-frames on the same bounds), then
        // the same preset again.
        camera.Orbit(2.1f, 0.3f);
        camera.Pan(0.4f, -0.2f);
        camera.Zoom(camera.Distance * 0.3f);
        camera.Frame(Center, Radius);
        camera.SetStandardView(view);

        Assert.Equal(firstDistance, camera.Distance, 4);
        Assert.Equal(firstTarget.X, camera.Target.X, 4);
        Assert.Equal(firstTarget.Y, camera.Target.Y, 4);
        Assert.Equal(firstTarget.Z, camera.Target.Z, 4);
        Assert.Equal(firstPosition.X, camera.Position.X, 3);
        Assert.Equal(firstPosition.Y, camera.Position.Y, 3);
        Assert.Equal(firstPosition.Z, camera.Position.Z, 3);
    }

    [Theory]
    [MemberData(nameof(AllViews))]
    public void Preset_KeepsTheFramingItWasGiven(StandardView view)
    {
        // A preset changes orientation only: a user who has zoomed in on a detail and asks for the
        // top view expects to still be looking at that detail, from above.
        var camera = Framed();
        camera.Zoom(-camera.Distance * 0.5f);
        camera.Pan(0.3f, 0.2f);
        Vector3 target = camera.Target;
        float distance = camera.Distance;

        camera.SetStandardView(view);

        Assert.Equal(target, camera.Target);
        Assert.Equal(distance, camera.Distance);
        Assert.Equal(distance, Vector3.Distance(camera.Position, camera.Target), 3);
    }

    [Theory]
    [MemberData(nameof(AllViews))]
    public void Preset_ThenOrbit_StaysNonDegenerate(StandardView view)
    {
        // The poles are exactly reachable by a preset but clamped away from by Orbit, so the first
        // drag after a top view must produce a usable camera rather than a flipped or NaN one.
        var camera = Framed();
        camera.SetStandardView(view);

        camera.Orbit(0.05f, 0.05f);

        Assert.InRange(camera.Pitch, -MathF.PI / 2f, MathF.PI / 2f);
        Assert.All(MatrixElements(camera.GetViewMatrix()), element => Assert.False(float.IsNaN(element)));
    }

    public static TheoryData<StandardView> AllViews()
    {
        var data = new TheoryData<StandardView>();
        foreach (StandardView view in Enum.GetValues<StandardView>())
        {
            data.Add(view);
        }

        return data;
    }

    private static float ScreenLengthOfUnitAxis(OrbitCamera camera, Matrix4x4 viewProjection, Vector3 axis)
    {
        Vector2 a = Ndc(viewProjection, camera.Target);
        Vector2 b = Ndc(viewProjection, camera.Target + axis);

        // Undo the aspect stretch so the three axes are compared in a square screen space.
        var delta = new Vector2((b.X - a.X) * Aspect, b.Y - a.Y);
        return delta.Length();
    }

    private static Vector2 Ndc(Matrix4x4 viewProjection, Vector3 world)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        return new Vector2(clip.X / clip.W, clip.Y / clip.W);
    }

    private static void AssertDirection(Vector3 expected, Vector3 actual, string what)
    {
        Vector3 normalized = Vector3.Normalize(actual);
        Assert.True(Vector3.Distance(expected, normalized) < 1e-4f, $"{what}: expected {expected}, got {normalized}.");
    }

    private static float[] MatrixElements(Matrix4x4 m) => new[]
    {
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44,
    };
}
