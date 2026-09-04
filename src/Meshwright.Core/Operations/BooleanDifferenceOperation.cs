using System.Globalization;
using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Boolean difference: subtracts the secondary mesh from the primary (loaded) mesh.
/// The operation is directional: primary - secondary.
/// Thin wrapper around <see cref="BooleanOperations.Difference"/>, following the same
/// pattern as other Edit operations in M3 (e.g., <see cref="HollowOperation"/>).
/// </summary>
public sealed class BooleanDifferenceOperation : MeshOperationBase
{
    private readonly DMesh3 _secondaryMesh;

    /// <summary>
    /// Create a difference operation that will subtract the secondary mesh from the primary.
    /// </summary>
    /// <param name="secondaryMesh">The mesh to subtract from the primary (already loaded) mesh.</param>
    public BooleanDifferenceOperation(DMesh3 secondaryMesh)
    {
        _secondaryMesh = secondaryMesh ?? throw new ArgumentNullException(nameof(secondaryMesh));
    }

    public override string Name => "Boolean Difference (Primary - Secondary)";

    protected override OperationResult Execute(DMesh3 primaryMesh)
    {
        try
        {
            int primaryTrisBefore = primaryMesh.TriangleCount;
            int secondaryTriCount = _secondaryMesh.TriangleCount;

            // Perform the difference via Manifold: primary - secondary
            DMesh3 resultMesh = BooleanOperations.Difference(primaryMesh, _secondaryMesh);

            // Copy result back into the primary mesh (in place)
            primaryMesh.Copy(resultMesh);

            int resultTriCount = primaryMesh.TriangleCount;

            var stats = MeshStatistics.Compute(primaryMesh);

            return new OperationResult(
                Changed: true,
                Summary: string.Format(
                    CultureInfo.InvariantCulture,
                    "Difference: subtracted secondary ({0} triangles) from primary ({1} triangles) = {2} triangles. Volume: {3:0.###}",
                    secondaryTriCount,
                    primaryTrisBefore,
                    resultTriCount,
                    stats.Volume));
        }
        catch (InvalidOperationException ex)
        {
            // Catch Manifold errors and report them clearly
            return new OperationResult(
                Changed: false,
                Summary: $"Boolean difference failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new OperationResult(
                Changed: false,
                Summary: $"Unexpected error during boolean difference: {ex.Message}");
        }
    }
}
