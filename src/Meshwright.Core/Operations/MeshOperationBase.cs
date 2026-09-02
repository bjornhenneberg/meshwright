using g3;

namespace Meshwright.Core.Operations;

/// <summary>
/// Implements <see cref="IMeshOperation.Preview"/> by running <see cref="Execute"/> against a
/// throwaway clone, so individual operations only need to implement the mutating path once and
/// can't accidentally let Preview leak mutations back into the caller's mesh.
/// </summary>
public abstract class MeshOperationBase : IMeshOperation
{
    public abstract string Name { get; }

    /// <summary>Mutates <paramref name="mesh"/> in place and returns what changed.</summary>
    protected abstract OperationResult Execute(DMesh3 mesh);

    public OperationResult Preview(DMesh3 mesh) => Execute(new DMesh3(mesh, bCompact: false));

    public OperationResult Apply(DMesh3 mesh) => Execute(mesh);
}
