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
    public void RecordBeforeApply(DMesh3 meshBeforeApply)
    {
        _undo.Push(new DMesh3(meshBeforeApply, bCompact: false));
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
