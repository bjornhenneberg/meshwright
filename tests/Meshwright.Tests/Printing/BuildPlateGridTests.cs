using Meshwright.Geometry.Printing;
using Xunit;

namespace Meshwright.Tests.Printing;

/// <summary>
/// The grid's spacing rule and the lines it produces.
///
/// <para>
/// The design question this pins is the contrast between the two corpus meshes: a 120 mm Eiffel
/// tower and a 2 x 2 x 2 mm Menger sponge on the same 220 mm bed. The plate must not resize to the
/// model - a bed that fits itself to whatever is loaded cannot answer "does this fit my printer" -
/// so the bed stays at true size and the <em>spacing</em> adapts, with a minor tier fine enough
/// that a tiny part still stands on visible ground.
/// </para>
/// </summary>
public class BuildPlateGridTests
{
    private static readonly BuildVolume Ender = new("Ender", 220, 220, 250);

    public static TheoryData<double> BedSizes() => new() { 20, 100, 180, 220, 250, 256, 350, 1000 };

    [Theory]
    [MemberData(nameof(BedSizes))]
    public void TheMajorGrid_IsReadableOnAnyBed(double bedSize)
    {
        var volume = new BuildVolume("Test", bedSize, bedSize, 200);

        double spacing = BuildPlateGrid.ChooseMajorSpacing(volume);
        double divisions = bedSize / spacing;

        Assert.InRange(divisions, 5, 25);
    }

    [Fact]
    public void TheMajorGrid_DependsOnTheBedAlone_NotOnTheModel()
    {
        // The Eiffel tower and the Menger sponge on the same printer must see the same bed drawn
        // the same way; only the minor tier is allowed to differ.
        double forTower = BuildPlateGrid.Build(Ender, modelFootprintMm: 55).MajorSpacingMm;
        double forSponge = BuildPlateGrid.Build(Ender, modelFootprintMm: 2).MajorSpacingMm;

        Assert.Equal(forTower, forSponge);
        Assert.Equal(10, forTower);
    }

    [Fact]
    public void ALargeModel_GetsNoMinorGrid()
    {
        // A 120 mm part already spans twelve 10 mm divisions; a second, finer tier would only be
        // noise.
        Assert.Null(BuildPlateGrid.ChooseMinorSpacing(Ender, modelFootprintMm: 120));
        Assert.Null(BuildPlateGrid.Build(Ender, 120).MinorSpacingMm);
    }

    [Fact]
    public void ATinyModel_GetsAGridFineEnoughToStandOn()
    {
        // The Menger sponge case: 2 mm across on a 220 mm bed, where the 10 mm major grid would
        // leave it in empty space between two lines.
        double? minor = BuildPlateGrid.ChooseMinorSpacing(Ender, modelFootprintMm: 2);

        Assert.NotNull(minor);
        Assert.True(minor < BuildPlateGrid.ChooseMajorSpacing(Ender));
        Assert.True(2.0 / minor!.Value >= 4, $"A 2 mm model spans only {2.0 / minor.Value:0.#} divisions of a {minor} mm grid.");
    }

    [Fact]
    public void TheMinorGrid_TracksTheModelSize()
    {
        double? tiny = BuildPlateGrid.ChooseMinorSpacing(Ender, 2);
        double? medium = BuildPlateGrid.ChooseMinorSpacing(Ender, 20);

        Assert.NotNull(tiny);
        Assert.NotNull(medium);
        Assert.True(tiny < medium, $"A smaller model must get a finer grid, not {tiny} vs {medium}.");
    }

    [Fact]
    public void EvenAnAbsurdlySmallModel_CannotAskForAnUnboundedGrid()
    {
        BuildPlateGridLines lines = BuildPlateGrid.Build(Ender, modelFootprintMm: 0.0005);

        double finest = lines.MinorSpacingMm ?? lines.MajorSpacingMm;
        Assert.True(
            Ender.LargestBedDimensionMm / finest <= BuildPlateGrid.MaxDivisionsPerAxis,
            $"{finest} mm spacing means {Ender.LargestBedDimensionMm / finest:0} divisions across the bed.");

        // Two axes, two vertices per line, three floats per vertex.
        Assert.True(lines.Positions.Length / 6 <= (BuildPlateGrid.MaxDivisionsPerAxis + 1) * 2 + 2);
    }

    [Fact]
    public void NoModelAtAll_StillDrawsTheBed()
    {
        BuildPlateGridLines lines = BuildPlateGrid.Build(Ender, modelFootprintMm: 0);

        Assert.Null(lines.MinorSpacingMm);
        Assert.True(lines.Positions.Length > 0);
        Assert.Equal(8 * 3, lines.OutlinePositions.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(120)]
    public void EveryLineLiesOnTheZ0PlaneInsideTheBed(double footprint)
    {
        // The plate is the bed, not the underside of the model: every vertex is at world Z=0, so
        // it stays put when the model moves. And no line runs off the bed it is describing.
        BuildPlateGridLines lines = BuildPlateGrid.Build(Ender, footprint);

        foreach (float[] buffer in new[] { lines.Positions, lines.OutlinePositions })
        {
            for (int i = 0; i + 2 < buffer.Length; i += 3)
            {
                Assert.Equal(0f, buffer[i + 2]);
                Assert.InRange(buffer[i], -110.001f, 110.001f);
                Assert.InRange(buffer[i + 1], -110.001f, 110.001f);
            }
        }
    }

    [Fact]
    public void MajorAndMinorLinesShareNoPosition()
    {
        // A minor line drawn on top of a major one would render whichever the driver rasterised
        // last, so the major grid would develop dim gaps at random.
        BuildPlateGridLines lines = BuildPlateGrid.Build(Ender, modelFootprintMm: 2);

        var seen = new Dictionary<(float, float, float, float), float>();
        for (int line = 0; line * 6 + 5 < lines.Positions.Length; line++)
        {
            int i = line * 6;
            var key = (lines.Positions[i], lines.Positions[i + 1], lines.Positions[i + 3], lines.Positions[i + 4]);
            Assert.False(seen.ContainsKey(key), $"Line {key} is drawn twice, at intensity {seen.GetValueOrDefault(key)} and {lines.Intensities[line * 2]}.");
            seen[key] = lines.Intensities[line * 2];
        }
    }

    [Fact]
    public void TheOutlineIsTheBedRectangle()
    {
        BuildPlateGridLines lines = BuildPlateGrid.Build(new BuildVolume("Rect", 250, 210, 220), 0);

        var xs = new List<float>();
        var ys = new List<float>();
        for (int i = 0; i + 2 < lines.OutlinePositions.Length; i += 3)
        {
            xs.Add(lines.OutlinePositions[i]);
            ys.Add(lines.OutlinePositions[i + 1]);
        }

        Assert.Equal(-125f, xs.Min());
        Assert.Equal(125f, xs.Max());
        Assert.Equal(-105f, ys.Min());
        Assert.Equal(105f, ys.Max());
    }

    [Fact]
    public void IntensitiesAreOnePerVertex()
    {
        BuildPlateGridLines lines = BuildPlateGrid.Build(Ender, 2);

        Assert.Equal(lines.Positions.Length / 3, lines.Intensities.Length);
        Assert.Contains(BuildPlateGrid.MajorIntensity, lines.Intensities);
        Assert.Contains(BuildPlateGrid.MinorIntensity, lines.Intensities);
    }
}
