using g3;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Resolves self-intersections (§5.1) by removing every triangle involved in one. Thin wrapper
/// around <see cref="SelfIntersectionRepair"/> - this is a best-effort local repair, not a
/// re-triangulation, so it can leave boundary holes behind; a later hole-filling operation in the
/// Auto Repair pipeline is expected to close them.
/// </summary>
public sealed class ResolveSelfIntersectionsOperation : MeshOperationBase
{
    private readonly SelfIntersectionRepair _repair = new();

    public override string Name => "Resolve Self-Intersections";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        SelfIntersectionRepair.Result result = _repair.Resolve(mesh);

        if (result.TrianglesRemoved == 0)
        {
            return new OperationResult(Changed: false, Summary: "No self-intersections found.");
        }

        string pairWord = result.PairsFound == 1 ? "self-intersection" : "self-intersections";
        string triangleWord = result.TrianglesRemoved == 1 ? "triangle" : "triangles";
        return new OperationResult(
            Changed: true,
            Summary: $"Resolved {result.PairsFound} {pairWord} by removing {result.TrianglesRemoved} {triangleWord} "
                + "(may leave holes — run hole filling next).");
    }
}
