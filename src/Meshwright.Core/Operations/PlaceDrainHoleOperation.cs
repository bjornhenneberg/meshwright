using System.Globalization;
using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Places a single drain hole at a specified surface location (§5.1 "Edit — Drain holes").
/// A drain hole is a cylindrical void cut through the mesh surface, typically 2-4mm diameter,
/// used to allow trapped resin/filament to drain from hollowed prints.
///
/// This operation removes triangles within the hole diameter to create the void. The hole walls
/// are the remaining mesh geometry — not perfectly cylindrical, but acceptable for v1.0.
/// Future versions may add boolean subtraction or explicit geometry for cleaner results.
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

        if (!result.HolePlaced)
        {
            return new OperationResult(
                Changed: false,
                Summary: "Could not place drain hole at the specified location (no triangles to remove, or point outside mesh).");
        }

        string countersinkMsg = _countersinkDepth > 0.0
            ? string.Format(CultureInfo.InvariantCulture, ", {0:0.##}mm countersink", _countersinkDepth)
            : "";

        return new OperationResult(
            Changed: true,
            Summary: string.Format(
                CultureInfo.InvariantCulture,
                "Placed drain hole (Ø{0:0.##}mm{1}, removed {2} triangles).",
                result.DiameterAchieved,
                countersinkMsg,
                result.TrianglesRemoved));
    }
}
