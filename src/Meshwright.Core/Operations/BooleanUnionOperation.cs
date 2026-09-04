using System.Globalization;
using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Boolean union: combines two meshes into one. The secondary mesh is unioned with
/// the primary (already loaded) mesh.
/// Thin wrapper around <see cref="BooleanOperations.Union"/>, following the same
/// pattern as other Edit operations in M3 (e.g., <see cref="HollowOperation"/>).
/// </summary>
public sealed class BooleanUnionOperation : MeshOperationBase
{
    private readonly DMesh3 _secondaryMesh;

    /// <summary>
    /// Create a union operation that will combine the primary mesh with the secondary mesh.
    /// </summary>
    /// <param name="secondaryMesh">The mesh to union with the primary (already loaded) mesh.</param>
    public BooleanUnionOperation(DMesh3 secondaryMesh)
    {
        _secondaryMesh = secondaryMesh ?? throw new ArgumentNullException(nameof(secondaryMesh));
    }

    public override string Name => "Boolean Union";

    protected override OperationResult Execute(DMesh3 primaryMesh)
    {
        try
        {
            int primaryTrisBefore = primaryMesh.TriangleCount;
            int secondaryTriCount = _secondaryMesh.TriangleCount;

            // Perform the union via Manifold
            DMesh3 resultMesh = BooleanOperations.Union(primaryMesh, _secondaryMesh);

            // Copy result back into the primary mesh (in place)
            primaryMesh.Copy(resultMesh);

            int resultTriCount = primaryMesh.TriangleCount;

            var stats = MeshStatistics.Compute(primaryMesh);

            return new OperationResult(
                Changed: true,
                Summary: string.Format(
                    CultureInfo.InvariantCulture,
                    "Union: combined primary ({0} triangles) with secondary ({1} triangles) = {2} triangles. Volume: {3:0.###}",
                    primaryTrisBefore,
                    secondaryTriCount,
                    resultTriCount,
                    stats.Volume));
        }
        catch (InvalidOperationException ex)
        {
            // Catch Manifold errors and report them clearly
            return new OperationResult(
                Changed: false,
                Summary: $"Boolean union failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new OperationResult(
                Changed: false,
                Summary: $"Unexpected error during boolean union: {ex.Message}");
        }
    }
}
