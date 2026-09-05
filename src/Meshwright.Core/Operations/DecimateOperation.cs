using System;
using System.Globalization;
using g3;

namespace Meshwright.Core.Operations;

/// <summary>How a <see cref="DecimateOperation"/>'s target triangle count is specified.</summary>
public enum DecimateTargetMode
{
    /// <summary>An absolute triangle count.</summary>
    TriangleCount,

    /// <summary>A fraction of the mesh's current triangle count (1.0 = 100%, 0.5 = 50%, ...).</summary>
    Percentage,
}

/// <summary>
/// Quadric edge-collapse decimation (SPECIFICATION.md §5.1 "Simplify"), wrapping the vendored g3Sharp
/// <see cref="Reducer"/>. Supports targeting either an absolute triangle count or a percentage of the
/// mesh's current count, per the spec's "targeting triangle count or percentage".
/// </summary>
/// <remarks>
/// <see cref="TargetTriangleCount"/> is deliberately exposed as a pure function of the current
/// triangle count, independent of <see cref="Execute"/>: it lets a caller (e.g. the standalone
/// decimate panel) compute and display the resolved target instantly as the user adjusts mode/value,
/// without re-running the actual (non-cheap) mesh decimation on every keystroke. Only <see cref="Apply"/>
/// (via <see cref="MeshOperationBase.Apply"/>) performs the real collapse pass.
/// </remarks>
public sealed class DecimateOperation : MeshOperationBase
{
    private readonly DecimateTargetMode _mode;
    private readonly int _targetTriangleCount;
    private readonly double _targetPercentage;

    private DecimateOperation(DecimateTargetMode mode, int targetTriangleCount, double targetPercentage)
    {
        _mode = mode;
        _targetTriangleCount = targetTriangleCount;
        _targetPercentage = targetPercentage;
    }

    /// <summary>Targets an absolute triangle count.</summary>
    public static DecimateOperation ToTriangleCount(int targetTriangleCount)
    {
        if (targetTriangleCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(targetTriangleCount), "Target triangle count must be at least 1.");
        }

        return new DecimateOperation(DecimateTargetMode.TriangleCount, targetTriangleCount, targetPercentage: 0.0);
    }

    /// <summary>Targets a fraction of the mesh's current triangle count (e.g. 0.5 for 50%).</summary>
    public static DecimateOperation ToPercentage(double targetFraction)
    {
        if (targetFraction <= 0.0 || double.IsNaN(targetFraction))
        {
            throw new ArgumentOutOfRangeException(nameof(targetFraction), "Target percentage must be greater than 0.");
        }

        return new DecimateOperation(DecimateTargetMode.Percentage, targetTriangleCount: 0, targetFraction);
    }

    public override string Name => "Decimate";

    /// <summary>Which mode this instance was constructed with — exposed for UI mode toggles.</summary>
    public DecimateTargetMode Mode => _mode;

    /// <summary>
    /// Resolves this operation's configured target against <paramref name="currentTriangleCount"/>,
    /// clamped to the valid range [1, currentTriangleCount]. Pure arithmetic — safe to call on every
    /// UI interaction for a live before/after readout without touching a mesh.
    /// </summary>
    public int TargetTriangleCount(int currentTriangleCount)
    {
        if (currentTriangleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentTriangleCount));
        }

        int rawTarget = _mode == DecimateTargetMode.TriangleCount
            ? _targetTriangleCount
            : (int)Math.Round(currentTriangleCount * _targetPercentage, MidpointRounding.AwayFromZero);

        return Math.Clamp(rawTarget, Math.Min(1, currentTriangleCount), currentTriangleCount);
    }

    protected override OperationResult Execute(DMesh3 mesh)
    {
        int before = mesh.TriangleCount;
        int target = TargetTriangleCount(before);

        if (target >= before)
        {
            return new OperationResult(
                Changed: false,
                Summary: $"Mesh already at or below the target of {target} triangles ({before} triangles).");
        }

        var reducer = new Reducer(mesh);
        reducer.ReduceToTriangleCount(target);

        int after = mesh.TriangleCount;
        if (after == before)
        {
            return new OperationResult(
                Changed: false,
                Summary: $"Could not reduce below {before} triangles without invalid collapses.");
        }

        double percentOfOriginal = before > 0 ? 100.0 * after / before : 0.0;
        string reduction = string.Format(
            CultureInfo.InvariantCulture,
            "Reduced from {0} to {1} triangles ({2:0.#}% of original).",
            before,
            after,
            percentOfOriginal);

        // Edge collapses that would create non-manifold geometry are refused, so on a mesh that is
        // already broken the reducer can stall far short of what was asked for. Reporting only the
        // before/after pair presents that as an unqualified success — the user asked for 100
        // triangles, got 55,000, and was told the operation worked.
        if (after > target)
        {
            return new OperationResult(
                Changed: true,
                Summary: string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} Short of the {1}-triangle target: further collapses would have created invalid geometry. Repairing the mesh first usually allows a deeper reduction.",
                    reduction,
                    target));
        }

        return new OperationResult(Changed: true, Summary: reduction);
    }
}
