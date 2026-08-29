using System;
using System.Numerics;
using Meshwright.Rendering.Camera;
using Xunit;

namespace Meshwright.Tests.Camera;

public class OrbitCameraTests
{
    [Fact]
    public void Orbit_ChangesPosition_KeepsDistanceConstant()
    {
        var camera = new OrbitCamera();
        float distanceBefore = camera.Distance;
        Vector3 positionBefore = camera.Position;

        camera.Orbit(0.5f, 0.1f);

        Assert.NotEqual(positionBefore, camera.Position);
        Assert.Equal(distanceBefore, camera.Distance);
        Assert.Equal(camera.Distance, Vector3.Distance(camera.Position, camera.Target), 4);
    }

    [Fact]
    public void Orbit_ClampsPitch_AtNorthPole()
    {
        var camera = new OrbitCamera();

        camera.Orbit(0f, MathF.PI * 10f);

        Assert.True(camera.Pitch < MathF.PI / 2f);
        Assert.True(camera.Pitch > 0f);
    }

    [Fact]
    public void Orbit_ClampsPitch_AtSouthPole()
    {
        var camera = new OrbitCamera();

        camera.Orbit(0f, -MathF.PI * 10f);

        Assert.True(camera.Pitch > -MathF.PI / 2f);
        Assert.True(camera.Pitch < 0f);
    }

    [Fact]
    public void Zoom_ChangesDistance_WithinBounds()
    {
        var camera = new OrbitCamera { MinDistance = 1f, MaxDistance = 10f };
        camera.Zoom(2f);
        Assert.True(camera.Distance > 5f);
    }

    [Fact]
    public void Zoom_ClampsToMinimum()
    {
        var camera = new OrbitCamera { MinDistance = 1f, MaxDistance = 10f };

        camera.Zoom(-1000f);

        Assert.Equal(1f, camera.Distance);
    }

    [Fact]
    public void Zoom_ClampsToMaximum()
    {
        var camera = new OrbitCamera { MinDistance = 1f, MaxDistance = 10f };

        camera.Zoom(1000f);

        Assert.Equal(10f, camera.Distance);
    }

    [Fact]
    public void Zoom_ChangesPosition()
    {
        var camera = new OrbitCamera();
        Vector3 positionBefore = camera.Position;

        camera.Zoom(1f);

        Assert.NotEqual(positionBefore, camera.Position);
    }

    [Fact]
    public void Pan_MovesTargetAndPosition_KeepingRelativeOffset()
    {
        var camera = new OrbitCamera();
        Vector3 targetBefore = camera.Target;
        Vector3 positionBefore = camera.Position;
        Vector3 offsetBefore = positionBefore - targetBefore;

        camera.Pan(0.5f, -0.25f);

        Assert.NotEqual(targetBefore, camera.Target);
        Vector3 offsetAfter = camera.Position - camera.Target;

        Assert.Equal(offsetBefore.X, offsetAfter.X, 4);
        Assert.Equal(offsetBefore.Y, offsetAfter.Y, 4);
        Assert.Equal(offsetBefore.Z, offsetAfter.Z, 4);
    }

    [Fact]
    public void GetViewMatrix_IsInvertible_AndNotIdentity()
    {
        var camera = new OrbitCamera();

        Matrix4x4 view = camera.GetViewMatrix();

        Assert.NotEqual(Matrix4x4.Identity, view);
        bool invertible = Matrix4x4.Invert(view, out _);
        Assert.True(invertible);
    }

    [Fact]
    public void GetProjectionMatrix_IsInvertible_AndNotIdentity()
    {
        var camera = new OrbitCamera();

        Matrix4x4 projection = camera.GetProjectionMatrix(16f / 9f);

        Assert.NotEqual(Matrix4x4.Identity, projection);
        bool invertible = Matrix4x4.Invert(projection, out _);
        Assert.True(invertible);
    }

    [Fact]
    public void DefaultConstructor_HasSensibleDefaults()
    {
        var camera = new OrbitCamera();

        Assert.Equal(Vector3.Zero, camera.Target);
        Assert.True(camera.Distance > 0f);
        Assert.True(camera.FovRadians > 0f);
        Assert.True(camera.NearPlane > 0f);
        Assert.True(camera.FarPlane > camera.NearPlane);
    }

    [Fact]
    public void Frame_SetsTargetToGivenCenter()
    {
        var camera = new OrbitCamera();
        var center = new Vector3(1f, 2f, 3f);

        camera.Frame(center, 5f);

        Assert.Equal(center, camera.Target);
    }

    [Fact]
    public void Frame_DistanceIsPositiveAndProportionalToRadius()
    {
        var camera = new OrbitCamera();

        camera.Frame(Vector3.Zero, 2f);
        float distanceSmall = camera.Distance;

        camera.Frame(Vector3.Zero, 20f);
        float distanceLarge = camera.Distance;

        Assert.True(distanceSmall > 0f);
        Assert.True(distanceLarge > 0f);
        Assert.True(distanceLarge > distanceSmall);
    }

    [Theory]
    [InlineData(10000f)]
    [InlineData(0.01f)]
    public void Frame_ExtremeRadii_ProduceValidNonDegenerateCamera(float radius)
    {
        var camera = new OrbitCamera();

        camera.Frame(Vector3.Zero, radius);

        Assert.False(float.IsNaN(camera.Distance));
        Assert.False(float.IsNaN(camera.NearPlane));
        Assert.False(float.IsNaN(camera.FarPlane));
        Assert.True(camera.Distance > 0f);
        Assert.True(camera.MinDistance < camera.Distance);
        Assert.True(camera.Distance < camera.MaxDistance);
        Assert.True(camera.NearPlane > 0f);
        Assert.True(camera.FarPlane > camera.NearPlane);
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(1f)]
    [InlineData(100f)]
    [InlineData(10000f)]
    public void Frame_ClipPlanes_NeverClipObjectAtDistanceBounds(float radius)
    {
        var camera = new OrbitCamera();

        camera.Frame(Vector3.Zero, radius);

        // FarPlane must clear the object even when fully zoomed out to MaxDistance.
        Assert.True(camera.FarPlane > camera.MaxDistance + radius);

        // NearPlane must stay comfortably inside MinDistance so zooming all the way in doesn't clip.
        Assert.True(camera.NearPlane < camera.MinDistance);
    }
}
