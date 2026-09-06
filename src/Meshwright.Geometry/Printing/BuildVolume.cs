using g3;

namespace Meshwright.Geometry.Printing;

/// <summary>
/// A printer's usable build volume, in millimetres.
///
/// <para>
/// The bed is <b>centred on the world origin in X and Y</b> and rises from Z=0: the volume is
/// X in [-Width/2, +Width/2], Y in [-Depth/2, +Depth/2], Z in [0, Height]. Z=0 as the bed is
/// already the app's convention (§11, 2026-09-05) and is what <c>AlignToBedOperation</c> and
/// <c>DropToZ0Operation</c> move a model onto; centring X/Y follows from the same place, since
/// nothing in the app translates a model to a bed corner and the sample meshes are all modelled
/// about the origin. A corner origin would put every freshly opened file outside the bed.
/// </para>
/// </summary>
/// <param name="Name">Display name, e.g. "Ender 3 (220 x 220 x 250)".</param>
/// <param name="WidthMm">Bed size along X.</param>
/// <param name="DepthMm">Bed size along Y.</param>
/// <param name="HeightMm">Maximum print height along Z.</param>
public sealed record BuildVolume(string Name, double WidthMm, double DepthMm, double HeightMm)
{
    /// <summary>The build volume as an axis-aligned box in world coordinates.</summary>
    public AxisAlignedBox3d Box => new(
        new Vector3d(-WidthMm / 2.0, -DepthMm / 2.0, 0.0),
        new Vector3d(WidthMm / 2.0, DepthMm / 2.0, HeightMm));

    /// <summary>The larger of the two bed dimensions, used to pick a grid spacing.</summary>
    public double LargestBedDimensionMm => Math.Max(WidthMm, DepthMm);

    /// <summary>
    /// Common consumer printers, offered in the View menu. Bed size is "configurable" in this
    /// slice by picking one of these; a free-form custom size lands with the settings file that
    /// recent files needs (§11, 2026-09-06), rather than growing a settings subsystem here.
    /// </summary>
    public static IReadOnlyList<BuildVolume> Presets { get; } = new[]
    {
        new BuildVolume("Prusa MINI (180 x 180 x 180)", 180, 180, 180),
        new BuildVolume("Ender 3 (220 x 220 x 250)", 220, 220, 250),
        new BuildVolume("Prusa MK4 (250 x 210 x 220)", 250, 210, 220),
        new BuildVolume("Bambu X1C (256 x 256 x 256)", 256, 256, 256),
        new BuildVolume("Large format (350 x 350 x 400)", 350, 350, 400),
    };

    /// <summary>The bed shown until the user picks another: the most common consumer size.</summary>
    public static BuildVolume Default => Presets[1];
}
