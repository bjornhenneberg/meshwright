using System.Globalization;
using g3;

namespace Meshwright.IO.Units;

/// <summary>
/// Whether a freshly imported mesh looks like it was authored in inches, and what to say about it.
///
/// <para>
/// <b>An STL carries no units.</b> Neither does an OBJ. The number 25.4 does not appear anywhere in
/// either file, and no amount of reading one can recover what its author meant — so "unit
/// detection" is a guess about intent made from a bounding box, and it can be wrong about a real
/// part that really is 2 mm across.
/// </para>
///
/// <para>
/// <b>The guess is therefore offered, never applied, and it shows its own reasoning.</b> §11 is
/// largely a catalogue of operations that reported success while being wrong, and silently
/// multiplying a user's model by 25.4 on open would be the most expensive entry in it: the model
/// would look identical (the camera frames whatever it is given), the triangle count would not
/// move, the diagnostics would be unchanged, and the only evidence would be an export measuring
/// twenty-five times too big. So the suspicion is surfaced as a sentence stating both readings
/// and the rule that produced it, next to a button; doing nothing is the default and costs the
/// user nothing but the sentence.
/// </para>
/// </summary>
public static class ImportUnits
{
    /// <summary>Exact, by definition of the international inch since 1959.</summary>
    public const double MillimetresPerInch = 25.4;

    /// <summary>
    /// Largest dimension, in millimetres, at or above which an inch reading is not offered.
    ///
    /// <para>
    /// Chosen from what the guess is for rather than from a survey: a file authored in inches and
    /// read as millimetres is 25.4× too small, so the question is which sizes are more likely to
    /// be that mistake than to be a genuinely tiny part. Below 12 mm the inch reading lands
    /// between roughly 25 mm and 300 mm — the size of most things people print — and a real part
    /// under 12 mm end to end is rare enough to be worth one dismissible sentence. Above it, the
    /// model is already a plausible size and there is nothing to suspect.
    /// </para>
    /// </summary>
    public const double SuspiciouslySmallMm = 12.0;

    /// <summary>
    /// The upper end: a file so small that reading it as inches still leaves something too small
    /// to print is not an inch file, it is a file in some other unit entirely (or a degenerate
    /// one). Offering ×25.4 there would just be a different wrong answer.
    /// </summary>
    public const double SmallestPlausiblePartMm = 5.0;

    /// <summary>
    /// Looks at <paramref name="mesh"/>'s bounding box and returns a suggestion to show the user,
    /// or null when there is nothing to suspect. Null is the common answer and the quiet one.
    /// </summary>
    public static UnitScaleSuggestion? Suspect(DMesh3? mesh)
    {
        if (mesh is null || mesh.TriangleCount == 0)
        {
            return null;
        }

        AxisAlignedBox3d bounds = mesh.CachedBounds;
        double largest = Math.Max(bounds.Width, Math.Max(bounds.Height, bounds.Depth));

        return Suspect(largest);
    }

    /// <summary>Same rule, expressed against a measurement, so the rule itself can be tested
    /// without building a mesh for each case.</summary>
    public static UnitScaleSuggestion? Suspect(double largestDimension)
    {
        if (!double.IsFinite(largestDimension) || largestDimension <= 0)
        {
            return null;
        }

        if (largestDimension >= SuspiciouslySmallMm)
        {
            return null;
        }

        double asInches = largestDimension * MillimetresPerInch;
        if (asInches < SmallestPlausiblePartMm)
        {
            return null;
        }

        return new UnitScaleSuggestion(largestDimension, asInches);
    }
}

/// <summary>
/// A suspicion that an import was authored in inches, and the sentence that states it.
/// </summary>
/// <param name="LargestDimensionAsLoaded">The model's largest dimension read as millimetres.</param>
/// <param name="LargestDimensionAsInches">The same dimension read as inches, in millimetres.</param>
public readonly record struct UnitScaleSuggestion(
    double LargestDimensionAsLoaded,
    double LargestDimensionAsInches)
{
    /// <summary>What the mesh would have to be multiplied by to reinterpret it as inches.</summary>
    public double ScaleFactor => ImportUnits.MillimetresPerInch;

    /// <summary>
    /// The offer, in plain language (§4). It gives both readings and the rule that produced the
    /// question, so a user whose part really is 4 mm across can see in one line why they are being
    /// asked and that the answer is "no".
    /// </summary>
    public string Message => string.Format(
        CultureInfo.CurrentCulture,
        "This model is {0:0.##} mm across at its widest — under {1:0.##} mm, so it may have been exported in inches. Read as inches it would be {2:0.##} mm.",
        LargestDimensionAsLoaded,
        ImportUnits.SuspiciouslySmallMm,
        LargestDimensionAsInches);

    /// <summary>Label for the button that accepts the suggestion, stating what it will do.</summary>
    public string AcceptLabel => string.Format(
        CultureInfo.CurrentCulture,
        "Scale ×{0:0.##} to mm",
        ImportUnits.MillimetresPerInch);
}
