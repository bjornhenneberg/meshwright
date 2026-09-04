using System.Globalization;
using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Hollow: offset shell to a given wall thickness (§5.1 "Edit"). Thin wrapper around
/// <see cref="HollowShell"/> exposing wall thickness and SDF grid resolution as constructor
/// parameters, following the same shape as <see cref="VoxelRemeshOperation"/> (the other
/// operation built on the SDF/marching-cubes machinery). Note for the follow-on printing use
/// case: a hollowed mesh generally needs a drain hole to be printable (trapped air/resin) — that
/// is a separate M3 feature this operation does not attempt.
/// </summary>
public sealed class HollowOperation : MeshOperationBase
{
    private const int DefaultLongestAxisResolution = 128;

    private readonly double _wallThickness;
    private readonly int _longestAxisResolution;
    private readonly HollowShell _hollow = new();

    public HollowOperation(double wallThickness, int longestAxisResolution = DefaultLongestAxisResolution)
    {
        _wallThickness = wallThickness;
        _longestAxisResolution = longestAxisResolution;
    }

    public override string Name => "Hollow";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        HollowResult result = _hollow.Hollow(mesh, _wallThickness, _longestAxisResolution);

        if (!result.CavityAdded)
        {
            return new OperationResult(
                Changed: false,
                Summary: string.Format(
                    CultureInfo.InvariantCulture,
                    "Could not hollow to {0:0.###}mm wall thickness — the mesh has no interior that thick anywhere (too thin/small for this request). Mesh left unchanged.",
                    _wallThickness));
        }

        double removedFraction = result.VolumeBefore > 0.0
            ? (result.VolumeBefore - result.VolumeAfter) / result.VolumeBefore * 100.0
            : 0.0;

        return new OperationResult(
            Changed: true,
            Summary: string.Format(
                CultureInfo.InvariantCulture,
                "Hollowed to {0:0.###}mm wall thickness, removed {1:0.#}% of volume ({2} -> {3} triangles).",
                _wallThickness,
                removedFraction,
                result.TrianglesBefore,
                result.TrianglesAfter));
    }
}
