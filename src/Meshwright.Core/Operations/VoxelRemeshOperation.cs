using g3;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Voxel remesh / solidify — the sledgehammer repair fallback for hopeless meshes (§5.1, §9). Thin
/// wrapper around <see cref="VoxelRemeshRepair"/> exposing the grid resolution as a constructor
/// parameter: it's inherently a quality/speed tradeoff (higher resolution preserves more detail but
/// costs roughly the cube of the value in memory and time), so there's no single correct default —
/// 128 cells along the mesh's longest bounding-box axis is a reasonable middle ground for typical
/// print-sized meshes. Always replaces the mesh, so <see cref="OperationResult.Changed"/> is always
/// true.
/// </summary>
public sealed class VoxelRemeshOperation : MeshOperationBase
{
    private const int DefaultLongestAxisResolution = 128;

    private readonly int _longestAxisResolution;
    private readonly VoxelRemeshRepair _repair = new();

    public VoxelRemeshOperation(int longestAxisResolution = DefaultLongestAxisResolution)
    {
        _longestAxisResolution = longestAxisResolution;
    }

    public override string Name => "Voxel Remesh";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        VoxelRemeshResult result = _repair.Remesh(mesh, _longestAxisResolution);

        return new OperationResult(
            Changed: true,
            Summary: $"Voxel remeshed: {result.TrianglesBefore} -> {result.TrianglesAfter} triangles "
                + $"(grid resolution {result.LongestAxisResolution}).");
    }
}
