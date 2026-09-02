using g3;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Closes every open boundary loop (hole) in the mesh (SPECIFICATION.md §5.1's "hole filling
/// (flat / smooth / planar fill)"), delegating the triangulation to <see cref="HoleFillRepair"/>.
/// </summary>
public sealed class FillHolesOperation : MeshOperationBase
{
    private readonly HoleFillMode _mode;

    /// <param name="mode">
    /// Triangulation strategy. Defaults to <see cref="HoleFillMode.Planar"/>: it handles
    /// non-convex holes correctly and doesn't add vertices <see cref="HoleFillMode.Flat"/> would,
    /// making it the reasonable general-purpose choice when the caller doesn't need to pick.
    /// </param>
    public FillHolesOperation(HoleFillMode mode = HoleFillMode.Planar)
    {
        _mode = mode;
    }

    public override string Name => "Fill Holes";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        HoleFillResult result = HoleFillRepair.Fill(mesh, _mode);

        if (result.HolesFilled == 0)
        {
            return new OperationResult(Changed: false, Summary: "No holes found.");
        }

        string modeLabel = _mode switch
        {
            HoleFillMode.Flat => "flat",
            HoleFillMode.Smooth => "smooth",
            _ => "planar",
        };
        string holeNoun = result.HolesFilled == 1 ? "hole" : "holes";

        return new OperationResult(
            Changed: true,
            Summary: $"Filled {result.HolesFilled} {holeNoun} ({modeLabel}), adding {result.TrianglesAdded} triangles.");
    }
}
