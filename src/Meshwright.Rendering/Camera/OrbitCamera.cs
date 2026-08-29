using System;
using System.Numerics;

namespace Meshwright.Rendering.Camera;

/// <summary>
/// Arcball/orbit camera for CAD-style mesh viewing: rotates around a target point,
/// pans the target, and zooms distance. All math uses single-precision System.Numerics.
/// </summary>
public sealed class OrbitCamera
{
    private const float MinPitch = -MathF.PI / 2f + 0.01f;
    private const float MaxPitch = MathF.PI / 2f - 0.01f;

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
        Yaw = MathF.PI / 4f;
        Pitch = MathF.PI / 6f;
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
                MathF.Sin(Pitch),
                cosPitch * MathF.Cos(Yaw));
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
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        Vector3 up = Vector3.Normalize(Vector3.Cross(right, forward));

        float scale = Distance;
        Target += (-deltaX * right + deltaY * up) * scale;
    }

    public void Zoom(float delta)
    {
        Distance = Math.Clamp(Distance + delta, MinDistance, MaxDistance);
    }

    public Matrix4x4 GetViewMatrix()
    {
        return Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitY);
    }

    public Matrix4x4 GetProjectionMatrix(float aspectRatio)
    {
        return Matrix4x4.CreatePerspectiveFieldOfView(FovRadians, aspectRatio, NearPlane, FarPlane);
    }
}
