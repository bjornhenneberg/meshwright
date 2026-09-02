using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;

namespace Meshwright.Core;

/// <summary>
/// Holds the currently loaded mesh plus the <see cref="MeshDiagnosticsReport"/> computed for it.
/// Running diagnostics is the one thing every mesh load needs (§5.1 "Inspect"); this type keeps
/// that pairing in one place rather than scattering detector calls across UI code. Also owns the
/// undo stack (§5.1 "Repair"/§6.3): every <see cref="IMeshOperation"/> applied through
/// <see cref="Apply"/> is undoable via <see cref="Undo"/>/<see cref="Redo"/>.
/// </summary>
public sealed class MeshDocument
{
    private static readonly IReadOnlyList<IMeshDetector> Detectors = new IMeshDetector[]
    {
        new NonManifoldDetector(),
        new BoundaryHoleDetector(),
        new SelfIntersectionDetector(),
        new InvertedNormalDetector(),
        new DegenerateTriangleDetector(),
        new DuplicateVertexDetector(),
        new DisconnectedShellDetector(),
    };

    private readonly UndoStack _undoStack = new();

    public DMesh3? Mesh { get; private set; }

    public MeshDiagnosticsReport? Report { get; private set; }

    public bool CanUndo => _undoStack.CanUndo;

    public bool CanRedo => _undoStack.CanRedo;

    /// <summary>Sets <paramref name="mesh"/> as current, clears undo history, and recomputes <see cref="Report"/>.</summary>
    public void Load(DMesh3 mesh)
    {
        Mesh = mesh;
        _undoStack.Clear();
        RefreshReport();
    }

    /// <summary>
    /// Records an undo snapshot, applies <paramref name="operation"/> to the current mesh, and
    /// recomputes <see cref="Report"/> so the diagnostics panel reflects the repair immediately.
    /// </summary>
    public OperationResult Apply(IMeshOperation operation)
    {
        if (Mesh is null)
        {
            throw new InvalidOperationException("No mesh is loaded.");
        }

        _undoStack.RecordBeforeApply(Mesh);
        OperationResult result = operation.Apply(Mesh);
        RefreshReport();
        return result;
    }

    /// <summary>Restores the mesh to its state before the last <see cref="Apply"/>, if any.</summary>
    public bool Undo()
    {
        if (Mesh is null)
        {
            return false;
        }

        DMesh3? restored = _undoStack.Undo(Mesh);
        if (restored is null)
        {
            return false;
        }

        Mesh = restored;
        RefreshReport();
        return true;
    }

    /// <summary>Re-applies the last undone operation's result, if any.</summary>
    public bool Redo()
    {
        if (Mesh is null)
        {
            return false;
        }

        DMesh3? restored = _undoStack.Redo(Mesh);
        if (restored is null)
        {
            return false;
        }

        Mesh = restored;
        RefreshReport();
        return true;
    }

    private void RefreshReport()
    {
        Report = Mesh is null ? null : MeshDiagnosticsRunner.Run(Mesh, Detectors);
    }
}
