using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Translates the mesh straight down (or up) so its lowest Z-coordinate lands on Z=0.
/// SPECIFICATION.md §5.1 lists "align to bed" and "drop to Z=0" as two distinct operations. Today
/// they perform the identical pure translation <see cref="AlignToBedOperation"/> does — the
/// difference print tooling usually implies ("align to bed" orients a face flat-down first, "drop
/// to Z=0" is a bare translation) is not implemented here; that is a scope decision left to the
/// dispatcher. This type exists so the button reports its own name instead of borrowing
/// AlignToBedOperation's.
/// </summary>
public sealed class DropToZ0Operation : MeshOperationBase
{
    public override string Name => "Drop to Z=0";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        var bounds = mesh.CachedBounds;
        double minZ = bounds.Min.z;

        if (Math.Abs(minZ) < 1e-10)
        {
            return new OperationResult(
                Changed: false,
                Summary: "Mesh is already at Z=0 (lowest point at Z=0).");
        }

        Transform.DropToZ0(mesh);

        // The mesh translates by -minZ: a mesh below the bed (minZ < 0) moves up, one floating
        // above it (minZ > 0) moves down.
        double displacement = -minZ;
        string direction = displacement >= 0 ? "up" : "down";
        return new OperationResult(
            Changed: true,
            Summary: $"Dropped to Z=0: moved {direction} by {Math.Abs(displacement):0.##} mm so lowest point is at Z=0.");
    }
}
