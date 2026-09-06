using System.Numerics;
using g3;
using Meshwright.Rendering.GL;
using Xunit;

namespace Meshwright.Tests.GL;

/// <summary>
/// What the section plane claims to hide, checked against the exact expression the shaders
/// evaluate. <see cref="CrossSectionPlane.HiddenHalfSpace"/> is the only description of the plane
/// the GPU ever sees, so a sign error there hides the half the user asked to keep - and it would
/// look like a working feature, just inverted, which is precisely the class of bug §11 collects.
/// </summary>
public class CrossSectionPlaneTests
{
    [Theory]
    [InlineData(CrossSectionAxis.X)]
    [InlineData(CrossSectionAxis.Y)]
    [InlineData(CrossSectionAxis.Z)]
    public void UnflippedHidesTheHighSide_FlippedHidesTheLowSide(CrossSectionAxis axis)
    {
        var unflipped = new CrossSectionPlane(axis, 10f, Flipped: false);
        var flipped = new CrossSectionPlane(axis, 10f, Flipped: true);

        Vector3 below = PointAt(axis, 4f);
        Vector3 above = PointAt(axis, 16f);

        Assert.False(unflipped.Hides(below));
        Assert.True(unflipped.Hides(above));

        Assert.True(flipped.Hides(below));
        Assert.False(flipped.Hides(above));
    }

    [Fact]
    public void TheOtherTwoAxesDoNotAffectWhatIsHidden()
    {
        // A plane whose normal leaked a component of another axis would still open the model, and
        // would still look plausible from most camera angles - it would just cut at a slant.
        var plane = new CrossSectionPlane(CrossSectionAxis.Z, 0f, Flipped: false);

        Assert.False(plane.Hides(new Vector3(-1000f, 1000f, -0.001f)));
        Assert.True(plane.Hides(new Vector3(1000f, -1000f, 0.001f)));
    }

    [Theory]
    [InlineData(CrossSectionAxis.X)]
    [InlineData(CrossSectionAxis.Y)]
    [InlineData(CrossSectionAxis.Z)]
    public void HidesAgreesWithTheHalfSpaceHandedToTheShader(CrossSectionAxis axis)
    {
        // Hides() is what the tests above reason about; HiddenHalfSpace is what actually reaches
        // the GPU. They are only worth anything if they are the same predicate.
        var plane = new CrossSectionPlane(axis, -3.5f, Flipped: true);
        Vector4 half = plane.HiddenHalfSpace;

        foreach (float t in new[] { -10f, -3.6f, -3.4f, 0f, 10f })
        {
            Vector3 point = PointAt(axis, t);
            float evaluated = (half.X * point.X) + (half.Y * point.Y) + (half.Z * point.Z) + half.W;
            Assert.Equal(plane.Hides(point), evaluated > 0f);
        }
    }

    [Fact]
    public void TheNormalIsUnitLength_SoTheOffsetIsInMillimetres()
    {
        // The shader compares dot(n, p) + d against zero, which only carries the intended
        // millimetre meaning while n is normalised.
        foreach (CrossSectionAxis axis in new[] { CrossSectionAxis.X, CrossSectionAxis.Y, CrossSectionAxis.Z })
        {
            foreach (bool flipped in new[] { false, true })
            {
                Assert.Equal(1f, new CrossSectionPlane(axis, 7f, flipped).HiddenNormal.Length(), 6);
            }
        }
    }

    [Fact]
    public void TheSliderTravelIsTheModelsOwnExtentAlongTheChosenAxis()
    {
        // Deliberately a box with three different extents at three different offsets: a range read
        // from the wrong component still produces a plausible-looking slider.
        var bounds = new AxisAlignedBox3d(new Vector3d(-1, 20, 300), new Vector3d(2, 40, 700));

        Assert.Equal((-1.0, 2.0), CrossSectionRange.Along(bounds, CrossSectionAxis.X));
        Assert.Equal((20.0, 40.0), CrossSectionRange.Along(bounds, CrossSectionAxis.Y));
        Assert.Equal((300.0, 700.0), CrossSectionRange.Along(bounds, CrossSectionAxis.Z));
    }

    [Fact]
    public void AtEitherEndOfItsTravel_ThePlaneHidesEverythingOrNothing()
    {
        // Both ends of the slider have to be reachable and mean what they look like, or the
        // control is smaller than it appears.
        var bounds = new AxisAlignedBox3d(new Vector3d(-5, -5, -5), new Vector3d(5, 5, 5));
        (double min, double max) = CrossSectionRange.Along(bounds, CrossSectionAxis.Z);

        var atTop = new CrossSectionPlane(CrossSectionAxis.Z, (float)max, Flipped: false);
        var atBottom = new CrossSectionPlane(CrossSectionAxis.Z, (float)min, Flipped: false);

        Assert.False(atTop.Hides(new Vector3(0, 0, 0)));
        Assert.False(atTop.Hides(new Vector3(0, 0, -5)));
        Assert.True(atBottom.Hides(new Vector3(0, 0, 0)));
        Assert.True(atBottom.Hides(new Vector3(0, 0, 5)));
    }

    private static Vector3 PointAt(CrossSectionAxis axis, float t) => axis switch
    {
        CrossSectionAxis.X => new Vector3(t, 0f, 0f),
        CrossSectionAxis.Y => new Vector3(0f, t, 0f),
        _ => new Vector3(0f, 0f, t),
    };
}
