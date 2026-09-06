namespace Meshwright.Geometry.Printing;

/// <summary>
/// The line geometry of the build plate grid: positions on the Z=0 plane, one intensity per
/// vertex so major and minor lines can be drawn in a single pass, and the bed outline separately
/// so it can be recoloured when the model does not fit.
/// </summary>
/// <param name="Positions">Line-list vertex positions, 3 floats per vertex, 2 vertices per line.</param>
/// <param name="Intensities">One brightness per vertex, parallel to <paramref name="Positions"/>.</param>
/// <param name="OutlinePositions">The bed rectangle, as its own line list.</param>
/// <param name="MajorSpacingMm">Spacing of the bright lines.</param>
/// <param name="MinorSpacingMm">Spacing of the dim lines, or null when there are none.</param>
public sealed record BuildPlateGridLines(
    float[] Positions,
    float[] Intensities,
    float[] OutlinePositions,
    double MajorSpacingMm,
    double? MinorSpacingMm);

/// <summary>
/// Chooses the grid spacing for a <see cref="BuildVolume"/> and builds the lines to draw.
///
/// <para>
/// <b>The plate never scales to the model.</b> A bed that resized itself to whatever is loaded
/// would be decoration, not a measurement, and the one question it exists to answer — does this
/// part fit on my printer — would become unanswerable. What adapts is the line spacing, and it
/// adapts in two independent tiers:
/// </para>
/// <list type="bullet">
/// <item><b>Major</b> lines come from the bed alone, on a 1/2/5 ladder, so a 220 mm bed always
/// reads as a 220 mm bed with a 10 mm grid however small the part on it is.</item>
/// <item><b>Minor</b> lines come from the model's footprint, so a 2 mm part on a 220 mm bed still
/// stands on visible ground instead of in empty space between two major lines 10 mm apart. They
/// are drawn only when they are finer than the major grid, so a large part sees one grid, not
/// two.</item>
/// </list>
/// <para>
/// Every line is still at a whole multiple of a real millimetre spacing, so nothing here makes the
/// grid describe anything but true distance.
/// </para>
/// </summary>
public static class BuildPlateGrid
{
    /// <summary>Spacings a person reads off a grid without counting: 1, 2 and 5 per decade.</summary>
    private static readonly double[] Ladder =
    {
        0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000,
    };

    /// <summary>At most this many major divisions across the bed's longer side.</summary>
    private const int MaxMajorDivisions = 25;

    /// <summary>A model should span at least this many minor divisions to read as sitting on a grid.</summary>
    private const int MinModelDivisions = 4;

    /// <summary>
    /// Hard ceiling on divisions per axis, so a pathologically small model cannot ask for a
    /// million-line grid. Reached only below roughly a thousandth of the bed.
    /// </summary>
    public const int MaxDivisionsPerAxis = 1000;

    /// <summary>Brightness of the major lines, and of the minor ones, in the plate's own colour.</summary>
    public const float MajorIntensity = 1f;

    /// <summary>
    /// The dim tier's brightness. It has a floor, not just a taste: the viewport clears to
    /// (0.15, 0.15, 0.18) and the plate's colour is (0.42, 0.46, 0.52), so 0.35 lands on
    /// (0.147, 0.161, 0.182) - the background to within one 8-bit step. The Menger sponge on a
    /// 220 mm bed drew a minor grid that was mathematically present and literally invisible.
    /// </summary>
    public const float MinorIntensity = 0.65f;

    /// <summary>The bright grid spacing, from the bed size alone.</summary>
    public static double ChooseMajorSpacing(BuildVolume volume)
    {
        double bed = Math.Max(volume.LargestBedDimensionMm, 1e-6);
        foreach (double spacing in Ladder)
        {
            if (bed / spacing <= MaxMajorDivisions)
            {
                return spacing;
            }
        }

        return Ladder[^1];
    }

    /// <summary>
    /// The dim grid spacing, from the model's footprint, or null when it would be no finer than
    /// the major grid (in which case a second tier would draw the same lines twice).
    /// </summary>
    public static double? ChooseMinorSpacing(BuildVolume volume, double modelFootprintMm)
    {
        double major = ChooseMajorSpacing(volume);
        if (!(modelFootprintMm > 0) || double.IsNaN(modelFootprintMm) || double.IsInfinity(modelFootprintMm))
        {
            return null;
        }

        double target = modelFootprintMm / MinModelDivisions;
        double candidate = Ladder[0];
        foreach (double spacing in Ladder)
        {
            if (spacing <= target)
            {
                candidate = spacing;
            }
        }

        // Never finer than the division ceiling allows: below that the grid is a grey sheet and
        // the vertex buffer grows without bound.
        double bed = Math.Max(volume.LargestBedDimensionMm, 1e-6);
        foreach (double spacing in Ladder)
        {
            if (bed / spacing <= MaxDivisionsPerAxis)
            {
                candidate = Math.Max(candidate, spacing);
                break;
            }
        }

        return candidate < major ? candidate : null;
    }

    /// <summary>
    /// Builds the grid for a bed and a model footprint (the larger of the model's X and Y extents;
    /// pass 0 when nothing is loaded, which yields the major grid alone).
    /// </summary>
    public static BuildPlateGridLines Build(BuildVolume volume, double modelFootprintMm)
    {
        double major = ChooseMajorSpacing(volume);
        double? minor = ChooseMinorSpacing(volume, modelFootprintMm);

        float halfWidth = (float)(volume.WidthMm / 2.0);
        float halfDepth = (float)(volume.DepthMm / 2.0);

        var positions = new List<float>();
        var intensities = new List<float>();

        if (minor is { } minorSpacing)
        {
            AppendGrid(positions, intensities, halfWidth, halfDepth, minorSpacing, MinorIntensity, skipMultiplesOf: major);
        }

        AppendGrid(positions, intensities, halfWidth, halfDepth, major, MajorIntensity, skipMultiplesOf: null);

        float[] outline =
        {
            -halfWidth, -halfDepth, 0f, halfWidth, -halfDepth, 0f,
            halfWidth, -halfDepth, 0f, halfWidth, halfDepth, 0f,
            halfWidth, halfDepth, 0f, -halfWidth, halfDepth, 0f,
            -halfWidth, halfDepth, 0f, -halfWidth, -halfDepth, 0f,
        };

        return new BuildPlateGridLines(positions.ToArray(), intensities.ToArray(), outline, major, minor);
    }

    /// <summary>
    /// Appends the lines of one grid tier. Lines run edge to edge in both directions, and every
    /// vertex sits at Z=0 — the plate is the bed, not the underside of whatever is loaded, so it
    /// stays put when the model moves.
    /// </summary>
    private static void AppendGrid(
        List<float> positions,
        List<float> intensities,
        float halfWidth,
        float halfDepth,
        double spacing,
        float intensity,
        double? skipMultiplesOf)
    {
        int stepsX = (int)Math.Floor((halfWidth / spacing) + 1e-9);
        for (int i = -stepsX; i <= stepsX; i++)
        {
            double x = i * spacing;
            if (IsMultipleOf(x, skipMultiplesOf))
            {
                continue;
            }

            AppendLine(positions, intensities, (float)x, -halfDepth, (float)x, halfDepth, intensity);
        }

        int stepsY = (int)Math.Floor((halfDepth / spacing) + 1e-9);
        for (int i = -stepsY; i <= stepsY; i++)
        {
            double y = i * spacing;
            if (IsMultipleOf(y, skipMultiplesOf))
            {
                continue;
            }

            AppendLine(positions, intensities, -halfWidth, (float)y, halfWidth, (float)y, intensity);
        }
    }

    /// <summary>Whether a minor line coincides with a major one, so it is not drawn twice in two colours.</summary>
    private static bool IsMultipleOf(double value, double? spacing)
    {
        if (spacing is not { } step || step <= 0)
        {
            return false;
        }

        double remainder = Math.Abs(value / step);
        return Math.Abs(remainder - Math.Round(remainder)) < 1e-6;
    }

    private static void AppendLine(
        List<float> positions,
        List<float> intensities,
        float x0,
        float y0,
        float x1,
        float y1,
        float intensity)
    {
        positions.AddRange(new[] { x0, y0, 0f, x1, y1, 0f });
        intensities.Add(intensity);
        intensities.Add(intensity);
    }
}
