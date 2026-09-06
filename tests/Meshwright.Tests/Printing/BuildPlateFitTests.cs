using g3;
using Meshwright.Geometry.Printing;
using Xunit;

namespace Meshwright.Tests.Printing;

/// <summary>
/// The out-of-bounds test, which has to be right in both directions. A warning that is always on
/// is as useless as one that never fires, so every case here states which way the verdict must go:
/// a model comfortably inside is silent, a model exactly on the edge is silent, a model over the
/// edge names the side and the amount, and the same unmoved model changes verdict when the
/// configured bed changes size.
/// </summary>
public class BuildPlateFitTests
{
    private static readonly BuildVolume Ender = new("Ender", 220, 220, 250);

    [Fact]
    public void AModelComfortablyInside_ProducesNoWarningAtAll()
    {
        BuildPlateFitResult fit = BuildPlateFit.Evaluate(BoxBounds(-20, -20, 0, 20, 20, 40), Ender);

        Assert.True(fit.Fits);
        Assert.Empty(fit.Overhangs);
        Assert.Null(fit.Message);
    }

    [Fact]
    public void AModelExactlyOnEveryEdge_Fits()
    {
        // The bed is inclusive of its own boundary: a 220 x 220 x 250 part on a 220 x 220 x 250
        // printer is the part that printer is sold to make.
        BuildPlateFitResult fit = BuildPlateFit.Evaluate(BoxBounds(-110, -110, 0, 110, 110, 250), Ender);

        Assert.True(fit.Fits, fit.Message);
        Assert.Null(fit.Message);
    }

    [Fact]
    public void AModelStraddlingOneEdge_NamesThatSideAndHowFarPast()
    {
        // Shifted 15 mm right: past the right edge by 15, and nowhere else.
        BuildPlateFitResult fit = BuildPlateFit.Evaluate(BoxBounds(-95, -110, 0, 125, 110, 100), Ender);

        Assert.False(fit.Fits);
        BuildVolumeOverhang overhang = Assert.Single(fit.Overhangs);
        Assert.Equal(BuildVolumeSide.Right, overhang.Side);
        Assert.Equal(15.0, overhang.OverhangMm, 6);
        Assert.Contains("15 mm past the right edge", fit.Message);
    }

    [Fact]
    public void AModelSunkBelowTheBed_IsOutOfBounds()
    {
        // Z=0 is the bed (§11, 2026-09-05) and is exactly what AlignToBed/DropToZ0 move a model
        // onto; geometry below it is not printable and must not be reported as fitting.
        BuildPlateFitResult fit = BuildPlateFit.Evaluate(BoxBounds(-10, -10, -3, 10, 10, 20), Ender);

        Assert.False(fit.Fits);
        Assert.Equal(BuildVolumeSide.Below, Assert.Single(fit.Overhangs).Side);
        Assert.Contains("below the bed", fit.Message);
    }

    [Fact]
    public void AModelTallerThanTheBuildHeight_IsOutOfBounds()
    {
        BuildPlateFitResult fit = BuildPlateFit.Evaluate(BoxBounds(-10, -10, 0, 10, 10, 260), Ender);

        Assert.False(fit.Fits);
        BuildVolumeOverhang overhang = Assert.Single(fit.Overhangs);
        Assert.Equal(BuildVolumeSide.Above, overhang.Side);
        Assert.Equal(10.0, overhang.OverhangMm, 6);
    }

    [Fact]
    public void ChangingTheBedSize_ChangesTheVerdictOnAModelThatHasNotMoved()
    {
        // The single most important property: the warning is about the bed as much as the model.
        AxisAlignedBox3d bounds = BoxBounds(-120, -120, 0, 120, 120, 200);

        BuildPlateFitResult onLargeBed = BuildPlateFit.Evaluate(bounds, new BuildVolume("Large", 350, 350, 400));
        BuildPlateFitResult onSmallBed = BuildPlateFit.Evaluate(bounds, Ender);

        Assert.True(onLargeBed.Fits, onLargeBed.Message);
        Assert.False(onSmallBed.Fits);
        Assert.Equal(4, onSmallBed.Overhangs.Count);
    }

    [Fact]
    public void EveryViolatedSide_IsListed_WorstFirst()
    {
        // 40 mm past the back, 10 mm past the right: the bigger problem is the one that decides
        // whether the part can be printed, so it leads.
        BuildPlateFitResult fit = BuildPlateFit.Evaluate(BoxBounds(-100, -100, 0, 120, 150, 10), Ender);

        Assert.False(fit.Fits);
        Assert.Equal(2, fit.Overhangs.Count);
        Assert.Equal("Outside the build volume: 40 mm past the back edge, 10 mm past the right edge.", fit.Message);
    }

    [Fact]
    public void TheVerdictComesFromTheMeshsRealBounds_NotFromTheOrigin()
    {
        // A mesh whose vertices are all far off the bed but whose centre would be irrelevant to a
        // radius-based test. Evaluated through the DMesh3 overload, so CachedBounds is what is read.
        DMesh3 mesh = BoxMesh(new Vector3d(300, 300, 10), new Vector3d(320, 320, 30));

        BuildPlateFitResult fit = BuildPlateFit.Evaluate(mesh, Ender);

        Assert.False(fit.Fits);
        Assert.Contains(fit.Overhangs, o => o.Side == BuildVolumeSide.Right);
        Assert.Contains(fit.Overhangs, o => o.Side == BuildVolumeSide.Back);

        // The same mesh, moved onto the bed, is silent - proving the verdict tracks position.
        DMesh3 moved = BoxMesh(new Vector3d(-10, -10, 0), new Vector3d(10, 10, 20));
        Assert.True(BuildPlateFit.Evaluate(moved, Ender).Fits);
    }

    [Fact]
    public void AnEmptyDocument_Fits()
    {
        Assert.True(BuildPlateFit.Evaluate((DMesh3?)null, Ender).Fits);
        Assert.True(BuildPlateFit.Evaluate(new DMesh3(), Ender).Fits);
    }

    [Fact]
    public void ABuildVolumesBox_IsCentredInXY_AndRisesFromZ0()
    {
        AxisAlignedBox3d box = Ender.Box;

        Assert.Equal(-110, box.Min.x, 9);
        Assert.Equal(110, box.Max.x, 9);
        Assert.Equal(-110, box.Min.y, 9);
        Assert.Equal(110, box.Max.y, 9);
        Assert.Equal(0, box.Min.z, 9);
        Assert.Equal(250, box.Max.z, 9);
    }

    private static AxisAlignedBox3d BoxBounds(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        => new(new Vector3d(minX, minY, minZ), new Vector3d(maxX, maxY, maxZ));

    /// <summary>A closed axis-aligned box, so <see cref="DMesh3.CachedBounds"/> is exactly min/max.</summary>
    private static DMesh3 BoxMesh(Vector3d min, Vector3d max)
    {
        var mesh = new DMesh3();
        var ids = new int[8];
        int i = 0;
        foreach (double x in new[] { min.x, max.x })
        {
            foreach (double y in new[] { min.y, max.y })
            {
                foreach (double z in new[] { min.z, max.z })
                {
                    ids[i++] = mesh.AppendVertex(new Vector3d(x, y, z));
                }
            }
        }

        int[][] faces =
        {
            new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 },
            new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 },
            new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 },
        };

        foreach (int[] face in faces)
        {
            mesh.AppendTriangle(ids[face[0]], ids[face[1]], ids[face[2]]);
            mesh.AppendTriangle(ids[face[0]], ids[face[2]], ids[face[3]]);
        }

        return mesh;
    }
}
