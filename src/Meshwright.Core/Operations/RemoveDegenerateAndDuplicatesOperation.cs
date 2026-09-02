using g3;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Core-layer wrapper for §5.1's "remove degenerate triangles and duplicate vertices" repair;
/// all algorithm logic lives in <see cref="RemoveDegenerateAndDuplicatesRepair"/>.
/// </summary>
public sealed class RemoveDegenerateAndDuplicatesOperation : MeshOperationBase
{
    public override string Name => "Remove Degenerate & Duplicates";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        RemoveDegenerateAndDuplicatesResult result = RemoveDegenerateAndDuplicatesRepair.Run(mesh);

        bool changed = result.MergedVertexCount > 0 || result.RemovedTriangleCount > 0;
        return new OperationResult(changed, BuildSummary(result.MergedVertexCount, result.RemovedTriangleCount));
    }

    private static string BuildSummary(int mergedVertexCount, int removedTriangleCount)
    {
        if (mergedVertexCount == 0 && removedTriangleCount == 0)
        {
            return "No duplicate vertices or degenerate triangles found.";
        }

        string vertexPart = $"Merged {mergedVertexCount} duplicate {(mergedVertexCount == 1 ? "vertex" : "vertices")}";
        string trianglePart = $"removed {removedTriangleCount} degenerate {(removedTriangleCount == 1 ? "triangle" : "triangles")}";

        if (mergedVertexCount == 0)
        {
            return $"Removed {removedTriangleCount} degenerate {(removedTriangleCount == 1 ? "triangle" : "triangles")}.";
        }

        if (removedTriangleCount == 0)
        {
            return $"{vertexPart}.";
        }

        return $"{vertexPart}, {trianglePart}.";
    }
}
