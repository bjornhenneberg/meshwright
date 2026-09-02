using g3;

namespace Meshwright.Core.Operations;

/// <summary>
/// A single user-facing mesh action with its parameters baked into the instance (§6.3): mutate
/// via <see cref="Apply"/>, or inspect the effect without mutating via <see cref="Preview"/>.
/// Undo/redo, the Auto Repair pipeline, batch mode and the CLI all compose from this one
/// abstraction rather than one-off code paths per operation.
/// </summary>
public interface IMeshOperation
{
    /// <summary>Human-readable name for reporting and undo-stack labeling.</summary>
    string Name { get; }

    /// <summary>Computes what this operation would do to <paramref name="mesh"/> without mutating it.</summary>
    OperationResult Preview(DMesh3 mesh);

    /// <summary>Mutates <paramref name="mesh"/> in place and returns what changed.</summary>
    OperationResult Apply(DMesh3 mesh);
}
