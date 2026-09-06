using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Aligns the mesh to the build bed by setting its lowest Z-coordinate to Z=0.
/// No parameters required — this is a one-click operation.
/// </summary>
public sealed class AlignToBedOperation : MeshOperationBase
{
    public override string Name => "Align to Bed";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        var bounds = mesh.CachedBounds;
        double minZ = bounds.Min.z;

        if (Math.Abs(minZ) < 1e-10)
        {
            return new OperationResult(
                Changed: false,
                Summary: "Mesh is already aligned to the bed (lowest point at Z=0).");
        }

        Transform.AlignToBed(mesh);

        // The mesh translates by -minZ. A mesh sitting below the bed (minZ < 0) moves up to reach
        // Z=0; a mesh floating above it (minZ > 0) moves down. Reporting "moved down by {minZ}"
        // unconditionally reads as "moved down by -1 mm" for the up-moving case.
        double displacement = -minZ;
        string direction = displacement >= 0 ? "up" : "down";
        return new OperationResult(
            Changed: true,
            Summary: $"Aligned to bed: moved {direction} by {Math.Abs(displacement):0.##} mm so lowest point is at Z=0.");
    }
}
