using System.Numerics;
using Xunit;

namespace Meshwright.Tests.Gizmos;

/// <summary>
/// Guards the model-matrix composition order used by the sphere-marker gizmos
/// (<c>SimpleMarkerGizmo</c>, <c>DrainHoleGizmo</c>). System.Numerics composes left-to-right for
/// row vectors, so <c>CreateScale * CreateTranslation</c> scales the unit sphere and then moves it;
/// the reverse order scales the translation as well and drags the marker toward the world origin.
/// </summary>
public class MarkerModelMatrixTests
{
    [Fact]
    public void ScaleThenTranslate_PlacesMarkerCentreAtRequestedPosition()
    {
        var position = new Vector3(10f, -4f, 2.5f);
        const float radius = 0.3f;

        Matrix4x4 model = Matrix4x4.CreateScale(radius) * Matrix4x4.CreateTranslation(position);

        // The unit sphere's centre must land exactly on the requested position...
        Vector3 centre = Vector3.Transform(Vector3.Zero, model);
        Assert.Equal(position.X, centre.X, 5);
        Assert.Equal(position.Y, centre.Y, 5);
        Assert.Equal(position.Z, centre.Z, 5);

        // ...and its +X pole must sit exactly `radius` away, i.e. the scale still applies.
        Vector3 pole = Vector3.Transform(Vector3.UnitX, model);
        Assert.Equal(radius, (pole - centre).Length(), 5);
    }

    [Fact]
    public void TranslateThenScale_IsTheWrongOrder_AndMisplacesTheMarker()
    {
        var position = new Vector3(10f, 0f, 0f);
        const float radius = 0.3f;

        Matrix4x4 reversed = Matrix4x4.CreateTranslation(position) * Matrix4x4.CreateScale(radius);

        // Documents the failure mode the gizmos used to have: the marker for a hole at x=10
        // rendered at x=3, scaled toward the origin along with everything else.
        Vector3 centre = Vector3.Transform(Vector3.Zero, reversed);
        Assert.Equal(3f, centre.X, 5);
    }
}
