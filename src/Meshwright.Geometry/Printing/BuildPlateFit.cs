using System.Globalization;
using g3;

namespace Meshwright.Geometry.Printing;

/// <summary>The side of the build volume a model sticks out of.</summary>
public enum BuildVolumeSide
{
    Left,
    Right,
    Front,
    Back,
    Below,
    Above,
}

/// <summary>How far a model reaches past one face of the build volume.</summary>
/// <param name="Side">Which face.</param>
/// <param name="OverhangMm">How far past it, always positive.</param>
public readonly record struct BuildVolumeOverhang(BuildVolumeSide Side, double OverhangMm);

/// <summary>
/// Whether a model fits the build volume, and if not, by how much and in which directions.
/// <paramref name="Message"/> is null exactly when <paramref name="Fits"/> is true, so a caller
/// cannot show a warning that says nothing.
/// </summary>
public sealed record BuildPlateFitResult(bool Fits, IReadOnlyList<BuildVolumeOverhang> Overhangs, string? Message)
{
    public static BuildPlateFitResult Fitting { get; } = new(true, Array.Empty<BuildVolumeOverhang>(), null);
}

/// <summary>
/// Tests a model's bounding box against a <see cref="BuildVolume"/> and reports the result in
/// plain language (§4: "tell the user exactly what is wrong ... not just an error count").
///
/// <para>
/// The test is on the model's <b>real world-space bounds</b>, in bed coordinates. A warning that
/// is always on is as useless as one that never fires, so the two things that must both hold are
/// pinned by tests: a model comfortably inside produces no warning at all, and the same, unmoved
/// model changes verdict when the configured bed changes size.
/// </para>
/// </summary>
public static class BuildPlateFit
{
    /// <summary>
    /// Floating-point slack only, not a fudge factor: a model whose bounds land exactly on a bed
    /// edge fits, and must not be reported as overhanging by the last bit of a double.
    /// </summary>
    private const double ToleranceMm = 1e-6;

    public static BuildPlateFitResult Evaluate(DMesh3? mesh, BuildVolume volume)
    {
        if (mesh is null || mesh.TriangleCount == 0)
        {
            return BuildPlateFitResult.Fitting;
        }

        return Evaluate(mesh.CachedBounds, volume);
    }

    public static BuildPlateFitResult Evaluate(AxisAlignedBox3d bounds, BuildVolume volume)
    {
        AxisAlignedBox3d bed = volume.Box;
        var overhangs = new List<BuildVolumeOverhang>();

        Add(overhangs, BuildVolumeSide.Left, bed.Min.x - bounds.Min.x);
        Add(overhangs, BuildVolumeSide.Right, bounds.Max.x - bed.Max.x);
        Add(overhangs, BuildVolumeSide.Front, bed.Min.y - bounds.Min.y);
        Add(overhangs, BuildVolumeSide.Back, bounds.Max.y - bed.Max.y);
        Add(overhangs, BuildVolumeSide.Below, bed.Min.z - bounds.Min.z);
        Add(overhangs, BuildVolumeSide.Above, bounds.Max.z - bed.Max.z);

        if (overhangs.Count == 0)
        {
            return BuildPlateFitResult.Fitting;
        }

        return new BuildPlateFitResult(false, overhangs, Describe(overhangs));
    }

    private static void Add(List<BuildVolumeOverhang> overhangs, BuildVolumeSide side, double overhang)
    {
        if (overhang > ToleranceMm)
        {
            overhangs.Add(new BuildVolumeOverhang(side, overhang));
        }
    }

    /// <summary>
    /// "Outside the build volume: 18.4 mm past the right edge, 12 mm above the maximum height."
    /// The worst overhang leads, because that is the one that decides whether the part can be
    /// printed at all.
    /// </summary>
    private static string Describe(List<BuildVolumeOverhang> overhangs)
    {
        IEnumerable<string> parts = overhangs
            .OrderByDescending(o => o.OverhangMm)
            .Select(o => string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.##} mm {1}",
                o.OverhangMm,
                SideText(o.Side)));

        return "Outside the build volume: " + string.Join(", ", parts) + ".";
    }

    private static string SideText(BuildVolumeSide side) => side switch
    {
        BuildVolumeSide.Left => "past the left edge",
        BuildVolumeSide.Right => "past the right edge",
        BuildVolumeSide.Front => "past the front edge",
        BuildVolumeSide.Back => "past the back edge",
        BuildVolumeSide.Below => "below the bed",
        BuildVolumeSide.Above => "above the maximum height",
        _ => "outside",
    };
}
