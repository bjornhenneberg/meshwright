using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Places a single drain hole at a specified surface location (§5.1 "Edit — Drain holes").
/// A drain hole is a cylindrical void cut through the mesh surface, typically 2-4mm diameter,
/// used to allow trapped resin/filament to drain from hollowed prints.
///
/// The hole is cut to the requested diameter by <see cref="DrainHole"/>, which refines the local
/// surface and stitches a real circular opening rather than deleting whichever triangles happen to
/// be nearby. The opening is left open on purpose: that is what drains a hollowed print.
/// </summary>
public sealed class PlaceDrainHoleOperation : MeshOperationBase
{
    private readonly Vector3d _surfacePoint;
    private readonly Vector3d _surfaceNormal;
    private readonly double _diameter;
    private readonly double _countersinkDepth;

    /// <summary>
    /// Creates a drain hole operation with the given parameters.
    /// </summary>
    /// <param name="surfacePoint">Center of the hole on the mesh surface (world coordinates).</param>
    /// <param name="surfaceNormal">Surface normal at the hole location (should point outward).</param>
    /// <param name="diameter">Desired hole diameter in mesh units (mm). Must be positive.</param>
    /// <param name="countersinkDepth">Countersink chamfer depth in mesh units. 0 = no countersink.</param>
    public PlaceDrainHoleOperation(
        Vector3d surfacePoint,
        Vector3d surfaceNormal,
        double diameter,
        double countersinkDepth = 0.0)
    {
        if (diameter <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(diameter), "Diameter must be positive.");
        }

        if (countersinkDepth < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(countersinkDepth), "Countersink depth must be non-negative.");
        }

        _surfacePoint = surfacePoint;
        _surfaceNormal = surfaceNormal.Normalized;
        _diameter = diameter;
        _countersinkDepth = countersinkDepth;
    }

    public override string Name => "Place Drain Hole";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        DrainHoleResult result = DrainHole.PlaceDrainHole(
            mesh,
            _surfacePoint,
            _surfaceNormal,
            _diameter,
            _countersinkDepth);

        // The summary is the geometry layer's own measured account of what happened — it names the
        // measured opening, the measured countersink and the surface area actually removed, and says
        // why when it could not honour the request. Re-describing the request here would put the
        // requested numbers back into a line the user reads as a result (§11, 2026-09-06).
        if (!result.HolePlaced)
        {
            return new OperationResult(Changed: false, Summary: result.Message);
        }

        return new OperationResult(Changed: true, Summary: result.Message);
    }
}
