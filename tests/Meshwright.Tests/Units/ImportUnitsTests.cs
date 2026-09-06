using g3;
using Meshwright.IO.Units;
using Xunit;

namespace Meshwright.Tests.Units;

/// <summary>
/// The mm/inch guess, on its own.
///
/// <para>
/// Every one of these is about a <em>question</em>, never about a change: nothing in
/// <see cref="ImportUnits"/> touches a mesh. That is the whole design — an STL carries no units,
/// so this is a guess about intent, and a guess that acts on its own is the failure mode §11
/// exists to record.
/// </para>
/// </summary>
public class ImportUnitsTests
{
    [Fact]
    public void AnInchSizedModel_IsAsked_About()
    {
        // 2 units across: 50.8 mm if the file meant inches, which is an ordinary printed part.
        UnitScaleSuggestion? suggestion = ImportUnits.Suspect(2.0);

        Assert.NotNull(suggestion);
        Assert.Equal(2.0, suggestion!.Value.LargestDimensionAsLoaded, 6);
        Assert.Equal(50.8, suggestion.Value.LargestDimensionAsInches, 6);
    }

    [Fact]
    public void AnOrdinarySizedModel_IsNotAskedAbout()
    {
        Assert.Null(ImportUnits.Suspect(60.0));
        Assert.Null(ImportUnits.Suspect(220.0));
    }

    [Fact]
    public void TheThresholdIsExclusiveAtTheTop()
    {
        Assert.NotNull(ImportUnits.Suspect(ImportUnits.SuspiciouslySmallMm - 0.001));
        Assert.Null(ImportUnits.Suspect(ImportUnits.SuspiciouslySmallMm));
    }

    [Fact]
    public void SomethingTooSmallToBeInchesEither_IsNotAskedAbout()
    {
        // 0.05 units is 1.27 mm read as inches - still too small to be a part, so x25.4 would
        // just be a different wrong answer. Offering it would train the user to ignore the bar.
        Assert.Null(ImportUnits.Suspect(0.05));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-3.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ANonsenseMeasurement_IsNotAskedAbout(double largest)
    {
        Assert.Null(ImportUnits.Suspect(largest));
    }

    [Fact]
    public void NoMesh_AndAnEmptyMesh_AreNotAskedAbout()
    {
        Assert.Null(ImportUnits.Suspect((DMesh3?)null));
        Assert.Null(ImportUnits.Suspect(new DMesh3()));
    }

    [Fact]
    public void TheMeasurementIsTheLargestDimension_NotTheFirst()
    {
        // A 200 x 1 x 1 model is not an inch file; taking X, or the diagonal, or the volume would
        // each get a different case wrong. The largest dimension is the one that decides whether
        // this could plausibly be a printed part already.
        var flat = MakeBox(200, 1, 1);
        var small = MakeBox(1, 1, 3);

        Assert.Null(ImportUnits.Suspect(flat));
        Assert.NotNull(ImportUnits.Suspect(small));
        Assert.Equal(3.0, ImportUnits.Suspect(small)!.Value.LargestDimensionAsLoaded, 6);
    }

    [Fact]
    public void TheMessageGivesBothReadingsAndTheRuleBehindTheQuestion()
    {
        // A guess that shows its working can be judged; one that just says "scale?" cannot.
        UnitScaleSuggestion suggestion = ImportUnits.Suspect(2.0)!.Value;

        Assert.Contains("2 mm", suggestion.Message);
        Assert.Contains("50.8", suggestion.Message);
        Assert.Contains("inches", suggestion.Message);
        Assert.Contains("25.4", suggestion.AcceptLabel);
    }

    private static DMesh3 MakeBox(double width, double depth, double height)
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(width, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, depth, 0));
        int d = mesh.AppendVertex(new Vector3d(0, 0, height));
        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(a, b, d);
        mesh.AppendTriangle(a, c, d);
        mesh.AppendTriangle(b, c, d);
        return mesh;
    }
}
