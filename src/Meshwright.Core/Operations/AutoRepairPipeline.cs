using g3;

namespace Meshwright.Core.Operations;

/// <summary>
/// "One-click Auto Repair" (§5.1): runs a fixed sequence of the individually-runnable repair
/// operations and reports what each one did. Order matters — degenerate/duplicate cleanup and
/// small-shell removal run first so later steps see a tidier mesh; self-intersection resolution
/// runs before hole filling because it can leave holes behind for that step to close; normal
/// unification runs last since earlier steps can change which edges/shells exist.
///
/// Voxel remesh/solidify is deliberately NOT part of this default sequence: §5.1/§9 describe it as
/// the "sledgehammer fallback for hopeless meshes", not a step every repair should pay for — it
/// discards fine detail, which the other five steps don't. It stays available as its own
/// individually-runnable operation for meshes the rest of this pipeline can't fix.
///
/// Implements <see cref="IMeshOperation"/> itself (via <see cref="MeshOperationBase"/>) so it
/// composes with <see cref="Meshwright.Core.MeshDocument.Apply"/> exactly like any single
/// operation — undo/redo covers the whole pipeline run as one step, not five.
/// </summary>
public sealed class AutoRepairPipeline : MeshOperationBase
{
    public override string Name => "Auto Repair";

    private readonly IReadOnlyList<IMeshOperation> _steps;

    /// <summary>Runs the default repair sequence.</summary>
    public AutoRepairPipeline()
        : this(DefaultSteps())
    {
    }

    /// <summary>Runs a custom sequence — mainly so tests can compose a subset without needing a
    /// mesh that exercises all five real algorithms.</summary>
    public AutoRepairPipeline(IReadOnlyList<IMeshOperation> steps)
    {
        _steps = steps;
    }

    private static IReadOnlyList<IMeshOperation> DefaultSteps() => new IMeshOperation[]
    {
        new RemoveDegenerateAndDuplicatesOperation(),
        new RemoveSmallShellsOperation(),
        new ResolveSelfIntersectionsOperation(),
        new FillHolesOperation(),
        new UnifyNormalsOperation(),
    };

    protected override OperationResult Execute(DMesh3 mesh)
    {
        var changedSummaries = new List<string>();
        bool anyChanged = false;

        foreach (IMeshOperation step in _steps)
        {
            OperationResult result = step.Apply(mesh);
            anyChanged |= result.Changed;
            if (result.Changed)
            {
                changedSummaries.Add(result.Summary);
            }
        }

        string summary = changedSummaries.Count == 0
            ? "No repairs needed."
            : string.Join(" ", changedSummaries);

        return new OperationResult(anyChanged, summary);
    }
}
