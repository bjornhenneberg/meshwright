using System;
using System.Numerics;

namespace Meshwright.Rendering.Camera;

/// <summary>
/// Arcball/orbit camera for CAD-style mesh viewing: rotates around a target point,
/// pans the target, and zooms distance. All math uses single-precision System.Numerics.
///
/// Z is up, following the STL/3MF and print-bed convention rather than the Y-up one common in
/// realtime graphics: a model authored for printing has its build direction along +Z, so a Y-up
/// camera shows practically every real file lying on its side.
/// <see cref="Pitch"/> is elevation above the XY ground plane and <see cref="Yaw"/> is rotation
/// about Z.
/// </summary>
public sealed class OrbitCamera
{
    private const float MinPitch = -MathF.PI / 2f + 0.01f;
    private const float MaxPitch = MathF.PI / 2f - 0.01f;

    /// <summary>
    /// The default (and <see cref="StandardView.Isometric"/>) orientation: 45 degrees of azimuth
    /// and the true isometric elevation, asin(1/sqrt 3) = 35.26 degrees, at which the three world
    /// axes project to equal screen lengths 120 degrees apart.
    /// </summary>
    private const float DefaultYaw = MathF.PI / 4f;
    private static readonly float DefaultPitch = MathF.Asin(1f / MathF.Sqrt(3f));

    /// <summary>
    /// Below this, <c>cos(Pitch)</c> is treated as exactly zero: the camera is straight over (or
    /// under) the target, the world Z axis is parallel to the view direction and cannot be the up
    /// vector, and the horizontal offset that survives in floating point is meaningless noise.
    /// </summary>
    private const float PoleEpsilon = 1e-6f;

    public float MinDistance { get; set; } = 0.01f;
    public float MaxDistance { get; set; } = 1000f;

    public Vector3 Target { get; private set; }
    public float Distance { get; private set; }
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

    /// <summary>
    /// Perspective or orthographic. Both modes share Target/Distance/Yaw/Pitch, and the
    /// orthographic frustum is sized so that geometry at the target plane keeps the on-screen size
    /// it had in perspective — switching modes reframes nothing.
    /// </summary>
    public ProjectionMode ProjectionMode { get; set; } = ProjectionMode.Perspective;

    public float FovRadians { get; set; }
    public float NearPlane { get; set; }
    public float FarPlane { get; set; }

    public OrbitCamera()
    {
        Target = Vector3.Zero;
        Distance = 5f;
        Yaw = DefaultYaw;
        Pitch = DefaultPitch;
        FovRadians = MathF.PI / 4f;
        NearPlane = 0.01f;
        FarPlane = 1000f;
    }

    /// <summary>Camera position, derived from Target/Distance/Yaw/Pitch (spherical coordinates).</summary>
    public Vector3 Position
    {
        get
        {
            float cosPitch = CosPitch;
            var offset = new Vector3(
                cosPitch * MathF.Sin(Yaw),
                cosPitch * MathF.Cos(Yaw),
                MathF.Sin(Pitch));
            return Target + offset * Distance;
        }
    }

    /// <summary>
    /// <c>cos(Pitch)</c>, snapped to exactly zero at the poles so a top or bottom view looks
    /// precisely down the Z axis instead of being off by the last few digits of float precision.
    /// </summary>
    private float CosPitch
    {
        get
        {
            float cosPitch = MathF.Cos(Pitch);
            return MathF.Abs(cosPitch) < PoleEpsilon ? 0f : cosPitch;
        }
    }

    /// <summary>
    /// Points the camera at one of the <see cref="StandardView"/> orientations, leaving
    /// <see cref="Target"/> and <see cref="Distance"/> — the framing the user has established —
    /// alone. Every component of the orientation is written, never some of them: <see cref="Frame"/>
    /// once reset Target and Distance but not Yaw/Pitch, which made Reset View look like a no-op
    /// after an orbit, and a preset that set only Pitch would fail exactly the same way.
    /// </summary>
    public void SetStandardView(StandardView view)
    {
        (Yaw, Pitch) = view switch
        {
            // Yaw is measured so that the camera offset is (cos P sin Y, cos P cos Y, sin P):
            // yaw 0 puts the camera on +Y, PI on -Y, PI/2 on +X.
            StandardView.Front => (MathF.PI, 0f),
            StandardView.Back => (0f, 0f),
            StandardView.Right => (MathF.PI / 2f, 0f),
            StandardView.Left => (-MathF.PI / 2f, 0f),

            // At the poles yaw no longer moves the camera, but it still decides which way is up on
            // screen (see GetViewMatrix). PI gives the top view +Y up and +X right, and the bottom
            // view its mirror — the orientations CAD and slicer tools show.
            StandardView.Top => (MathF.PI, MathF.PI / 2f),
            StandardView.Bottom => (MathF.PI, -MathF.PI / 2f),

            StandardView.Isometric => (DefaultYaw, DefaultPitch),
            _ => (DefaultYaw, DefaultPitch),
        };
    }

    public void Orbit(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch = Math.Clamp(Pitch + deltaPitch, MinPitch, MaxPitch);
    }

    /// <summary>Moves Target along the camera's local right/up axes, scaled by Distance so pan feels consistent at any zoom.</summary>
    public void Pan(float deltaX, float deltaY)
    {
        Vector3 forward = Vector3.Normalize(Target - Position);
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitZ));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

        float scale = Distance;
        Target += (-deltaX * right + deltaY * up) * scale;
    }

    public void Zoom(float delta)
    {
        Distance = Math.Clamp(Distance + delta, MinDistance, MaxDistance);
    }

    /// <summary>
    /// Frames the camera on a bounding sphere: centers the target, sets Distance so the sphere
    /// fits within the vertical FOV, resets orientation to the default yaw/pitch, and rescales
    /// the distance/clip-plane ranges to the mesh's scale so wildly different sized meshes
    /// (millimeters to meters) don't clip or feel wrong.
    ///
    /// Resetting Yaw/Pitch here (not just Target/Distance) matters: this is the method Reset View
    /// calls, and an orbit alone (no pan/zoom) leaves Target and the fitted Distance unchanged, so
    /// without resetting orientation too, Reset View would look like it did nothing.
    /// </summary>
    public void Frame(Vector3 center, float radius)
    {
        radius = MathF.Max(radius, 0.001f);
        Target = center;
        Yaw = DefaultYaw;
        Pitch = DefaultPitch;

        const float marginFactor = 1.25f;
        Distance = radius / MathF.Sin(FovRadians / 2f) * marginFactor;

        MinDistance = MathF.Max(radius * 0.02f, 0.0001f);
        MaxDistance = MathF.Max(radius * 200f, MinDistance * 10f);
        Distance = Math.Clamp(Distance, MinDistance, MaxDistance);

        NearPlane = MathF.Max(MinDistance * 0.1f, 0.0001f);
        FarPlane = (MaxDistance + radius) * 1.5f;
    }

    /// <summary>
    /// The up vector handed to <see cref="Matrix4x4.CreateLookAt"/>. World +Z everywhere except at
    /// the poles, where the view direction *is* the Z axis and a +Z up vector produces a degenerate
    /// (all-NaN) matrix — the classic "top view looking down -Z with up = +Z" failure. There it
    /// uses the limit of the +Z up vector as the pitch approaches the pole, so the on-screen
    /// orientation is continuous with the orbit that arrives there.
    /// </summary>
    private Vector3 UpVector
    {
        get
        {
            if (CosPitch != 0f)
            {
                return Vector3.UnitZ;
            }

            float sign = Pitch > 0f ? -1f : 1f;
            return new Vector3(sign * MathF.Sin(Yaw), sign * MathF.Cos(Yaw), 0f);
        }
    }

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, UpVector);
    }

    /// <summary>
    /// World-space height of the orthographic view volume: the height the perspective frustum has
    /// at the target plane, so that toggling <see cref="ProjectionMode"/> leaves geometry at the
    /// target the size it already was, and the mouse wheel keeps zooming in both modes.
    /// </summary>
    public float OrthographicHeight => 2f * Distance * MathF.Tan(FovRadians / 2f);

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        if (ProjectionMode == ProjectionMode.Perspective)
        {
            return Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, aspectRatio, NearPlane, FarPlane);
        }

        // An orthographic volume has no eye point to be in front of, so its near plane is placed
        // *behind* the camera rather than at NearPlane. Zooming an orthographic camera shrinks the
        // view height while the eye keeps travelling toward the target, and a near plane at the eye
        // would slice the model in half on the way past. Depth stays linear across the range, so
        // the extra span costs precision measured in millionths of the model.
        float height = MathF.Max(OrthographicHeight, 1e-6f);
        return Matrix4x4.CreateOrthographic(height * aspectRatio, height, -FarPlane, FarPlane);
    }
}
