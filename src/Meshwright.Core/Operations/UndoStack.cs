using g3;

namespace Meshwright.Core.Operations;

/// <summary>
/// Snapshot-based undo: clones the mesh before every apply rather than requiring each
/// <see cref="IMeshOperation"/> to implement its own inverse. Simpler and safer for whole-mesh
/// repair ops than element-wise undo, at the cost of an extra mesh copy per step — acceptable
/// per §4's "never silently destroy the model".
/// </summary>
public sealed class UndoStack
{
    private readonly Stack<DMesh3> _undo = new();
    private readonly Stack<DMesh3> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Records a snapshot of <paramref name="meshBeforeApply"/> and clears the redo stack.</summary>
    public void RecordBeforeApply(DMesh3 meshBeforeApply) => Commit(Capture(meshBeforeApply));

    /// <summary>
    /// Copies <paramref name="mesh"/> as an apply would need to, <em>without</em> putting it in the
    /// history. Operations mutate in place, so the copy has to be taken before the operation runs —
    /// but whether it becomes an undo step is only known after, when the operation says whether it
    /// changed anything. Pair with <see cref="Commit"/>; a snapshot that is never committed simply
    /// goes away, leaving both stacks exactly as they were.
    /// </summary>
    public DMesh3 Capture(DMesh3 mesh) => new(mesh, bCompact: false);

    /// <summary>Makes a snapshot from <see cref="Capture"/> the new undo step and clears redo.</summary>
    public void Commit(DMesh3 snapshot)
    {
        _undo.Push(snapshot);
        _redo.Clear();
    }

    /// <summary>
    /// Pops the last recorded snapshot and returns it, pushing <paramref name="currentMesh"/> onto
    /// the redo stack. Returns null if there is nothing to undo.
    /// </summary>
    public DMesh3? Undo(DMesh3 currentMesh)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        _redo.Push(new DMesh3(currentMesh, bCompact: false));
        return _undo.Pop();
    }

    /// <summary>
    /// Pops the last undone snapshot and returns it, pushing <paramref name="currentMesh"/> back
    /// onto the undo stack. Returns null if there is nothing to redo.
    /// </summary>
    public DMesh3? Redo(DMesh3 currentMesh)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.Push(new DMesh3(currentMesh, bCompact: false));
        return _redo.Pop();
    }

    /// <summary>Discards all recorded history. Used when a new mesh is loaded.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
