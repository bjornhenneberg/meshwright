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

    private const float DefaultYaw = MathF.PI / 4f;
    private const float DefaultPitch = MathF.PI / 6f;

    public float MinDistance { get; set; } = 0.01f;
    public float MaxDistance { get; set; } = 1000f;

    public Vector3 Target { get; private set; }
    public float Distance { get; private set; }
    public float Yaw { get; private set; }
    public float Pitch { get; private set; }

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
            float cosPitch = MathF.Cos(Pitch);
            var offset = new Vector3(
                cosPitch * MathF.Sin(Yaw),
                cosPitch * MathF.Cos(Yaw),
                MathF.Sin(Pitch));
            return Target + offset * Distance;
        }
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

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitZ);
    }

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, aspectRatio, NearPlane, FarPlane);
    }
}
