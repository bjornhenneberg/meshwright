using g3;

namespace Meshwright.Core.Operations;

/// <summary>
/// Optional capability for an <see cref="IMeshOperation"/> that is composed of discrete,
/// independently-completable steps, so it can report real progress between them and can stop
/// between steps when asked. <see cref="AutoRepairPipeline"/> is the only current implementer:
/// its five repair steps are genuine checkpoints. Every other operation is one opaque call into
/// vendored g3Sharp code or native Manifold interop with no safe place to pause or measure
/// partial completion, so <see cref="MeshDocument.ApplyAsync"/> falls back to an honest
/// indeterminate spinner and no cancellation for those, per §4.
/// </summary>
public interface IProgressReportingMeshOperation
{
    /// <summary>Mutates <paramref name="mesh"/> in place, reporting progress after each completed
    /// step and stopping before the next one if <paramref name="cancellationToken"/> has been
    /// signalled. A stop mid-run still returns a valid <see cref="OperationResult"/> describing
    /// whatever steps did complete — it is not an error, and steps already applied stay applied.</summary>
    OperationResult Apply(DMesh3 mesh, IProgress<OperationProgress> progress, CancellationToken cancellationToken);
}
