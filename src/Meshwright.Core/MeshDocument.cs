using System.Threading;
using System.Threading.Tasks;
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

    private CancellationTokenSource? _currentOperationCts;
    private bool _currentOperationCancellable;

    /// <summary>
    /// Raised after any change to <see cref="Mesh"/> or <see cref="Report"/> — load, apply, undo,
    /// or redo. The UI can't observe an <see cref="Apply"/> otherwise: operations mutate the mesh
    /// in place, so the viewport keeps rendering its already-uploaded copy and the diagnostics
    /// panel keeps showing the pre-operation report unless something tells them to re-read.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised whenever <see cref="IsBusy"/> flips, i.e. right before an <see cref="ApplyAsync"/>
    /// call starts its background work and right after it finishes (success, cancellation, or
    /// fault alike). The one place the UI needs to hook to keep a second operation from starting,
    /// grey out Undo/Redo while a mutation is in flight, and show/hide a busy indicator — mirrors
    /// how <see cref="Changed"/> is the one place the UI refreshes from (§11, 2026-09-05).
    /// </summary>
    public event EventHandler? BusyChanged;

    /// <summary>
    /// Raised from inside a running <see cref="ApplyAsync"/> for operations that can report real
    /// progress (see <see cref="IProgressReportingMeshOperation"/>). Always raised on whatever
    /// context <see cref="ApplyAsync"/> was called from — safe to touch UI controls from a
    /// handler. Operations that cannot honestly report progress never raise this at all; the UI
    /// should default to an indeterminate indicator while <see cref="IsBusy"/> and only switch to
    /// a determinate one if this actually fires.
    /// </summary>
    public event EventHandler<OperationProgress>? Progress;

    /// <summary>What caused the most recent <see cref="Changed"/> — an operation name, "Loaded",
    /// "Undo", or "Redo" — so the UI can say what just happened without tracking it separately.</summary>
    public string? LastChangeDescription { get; private set; }

    public DMesh3? Mesh { get; private set; }

    public MeshDiagnosticsReport? Report { get; private set; }

    /// <summary>True while an <see cref="ApplyAsync"/> call is running its operation on a
    /// background thread. <see cref="Apply"/>, <see cref="Undo"/> and <see cref="Redo"/> all
    /// refuse to run while this is true — the mesh is being mutated by another thread, so
    /// touching it here would be a data race, not just a UX inconvenience.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Name of the operation currently running, or null when idle. Set before
    /// <see cref="BusyChanged"/> announces the start, so a busy indicator can say which operation
    /// it is waiting on. Deliberately not <see cref="LastChangeDescription"/>, which describes the
    /// *previous* completed change — reading that while busy labels the running operation with the
    /// name of the last finished one ("Working: Loaded...").</summary>
    public string? CurrentOperationName { get; private set; }

    /// <summary>Whether the operation currently running (if any) is one that honours cooperative
    /// cancellation — see <see cref="IProgressReportingMeshOperation"/>. Most operations are one
    /// opaque call with no safe place to stop, so a Cancel button should stay disabled unless
    /// this is true; offering cancellation that silently does nothing would violate §4.</summary>
    public bool CanCancelCurrentOperation => IsBusy && _currentOperationCancellable;

    public bool CanUndo => !IsBusy && _undoStack.CanUndo;

    public bool CanRedo => !IsBusy && _undoStack.CanRedo;

    /// <summary>Sets <paramref name="mesh"/> as current, clears undo history, and recomputes <see cref="Report"/>.</summary>
    public void Load(DMesh3 mesh)
    {
        Mesh = mesh;
        _undoStack.Clear();
        RefreshReport("Loaded");
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

        if (IsBusy)
        {
            throw new InvalidOperationException("Another operation is already running.");
        }

        _undoStack.RecordBeforeApply(Mesh);
        OperationResult result = operation.Apply(Mesh);
        RefreshReport(operation.Name);
        return result;
    }

    /// <summary>
    /// Records an undo snapshot, then runs <paramref name="operation"/> against the current mesh
    /// on a background thread so the calling thread — the UI thread, in practice — is free to keep
    /// repainting and handling input while it runs (§6.3, backlog item 13). The undo snapshot and
    /// <see cref="IsBusy"/> flip to true happen synchronously before the background work starts,
    /// and <see cref="Report"/>/<see cref="Changed"/> are only touched after it finishes, back on
    /// whatever context called this — safe for Avalonia controls as long as this is awaited from
    /// the UI thread, exactly like any other awaited UI-thread call.
    /// </summary>
    public async Task<OperationResult> ApplyAsync(IMeshOperation operation)
    {
        if (Mesh is null)
        {
            throw new InvalidOperationException("No mesh is loaded.");
        }

        if (IsBusy)
        {
            throw new InvalidOperationException("Another operation is already running.");
        }

        DMesh3 mesh = Mesh;
        _undoStack.RecordBeforeApply(mesh);

        _currentOperationCancellable = operation is IProgressReportingMeshOperation;
        _currentOperationCts = new CancellationTokenSource();
        CancellationToken token = _currentOperationCts.Token;

        CurrentOperationName = operation.Name;
        IsBusy = true;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            // Constructed here, on the calling thread, so Progress<T>'s captured
            // SynchronizationContext is the caller's (the UI thread's) — its Report() calls made
            // from the background thread below marshal themselves back automatically.
            var progress = new Progress<OperationProgress>(p => Progress?.Invoke(this, p));

            OperationResult result = operation is IProgressReportingMeshOperation reporting
                ? await Task.Run(() => reporting.Apply(mesh, progress, token)).ConfigureAwait(true)
                : await Task.Run(() => operation.Apply(mesh)).ConfigureAwait(true);

            RefreshReport(operation.Name);
            return result;
        }
        finally
        {
            IsBusy = false;
            CurrentOperationName = null;
            _currentOperationCancellable = false;
            _currentOperationCts.Dispose();
            _currentOperationCts = null;
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Asks the in-flight operation to stop at its next safe checkpoint. A no-op unless
    /// <see cref="CanCancelCurrentOperation"/> is true — most operations have no such checkpoint
    /// and will simply run to completion regardless of this call.</summary>
    public void CancelCurrentOperation() => _currentOperationCts?.Cancel();

    /// <summary>Restores the mesh to its state before the last <see cref="Apply"/>, if any.</summary>
    public bool Undo()
    {
        if (Mesh is null || IsBusy)
        {
            return false;
        }

        DMesh3? restored = _undoStack.Undo(Mesh);
        if (restored is null)
        {
            return false;
        }

        Mesh = restored;
        RefreshReport("Undo");
        return true;
    }

    /// <summary>Re-applies the last undone operation's result, if any.</summary>
    public bool Redo()
    {
        if (Mesh is null || IsBusy)
        {
            return false;
        }

        DMesh3? restored = _undoStack.Redo(Mesh);
        if (restored is null)
        {
            return false;
        }

        Mesh = restored;
        RefreshReport("Redo");
        return true;
    }

    private void RefreshReport(string reason)
    {
        Report = Mesh is null ? null : MeshDiagnosticsRunner.Run(Mesh, Detectors);
        LastChangeDescription = reason;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
