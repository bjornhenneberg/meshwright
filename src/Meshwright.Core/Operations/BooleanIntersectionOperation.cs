using System.Globalization;
using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Boolean intersection: keeps only the region where both meshes overlap.
/// Thin wrapper around <see cref="BooleanOperations.Intersection"/>, following the same
/// pattern as other Edit operations in M3 (e.g., <see cref="HollowOperation"/>).
/// </summary>
public sealed class BooleanIntersectionOperation : MeshOperationBase
{
    private readonly DMesh3 _secondaryMesh;

    /// <summary>
    /// Create an intersection operation that will keep only the overlapping region
    /// of the primary and secondary meshes.
    /// </summary>
    /// <param name="secondaryMesh">The mesh to intersect with the primary (already loaded) mesh.</param>
    public BooleanIntersectionOperation(DMesh3 secondaryMesh)
    {
        _secondaryMesh = secondaryMesh ?? throw new ArgumentNullException(nameof(secondaryMesh));
    }

    public override string Name => "Boolean Intersection";

    protected override OperationResult Execute(DMesh3 primaryMesh)
    {
        try
        {
            int primaryTrisBefore = primaryMesh.TriangleCount;
            int secondaryTriCount = _secondaryMesh.TriangleCount;

            // Perform the intersection via Manifold
            DMesh3 resultMesh = BooleanOperations.Intersection(primaryMesh, _secondaryMesh);

            // Copy result back into the primary mesh (in place)
            primaryMesh.Copy(resultMesh);

            int resultTriCount = primaryMesh.TriangleCount;

            var stats = MeshStatistics.Compute(primaryMesh);

            return new OperationResult(
                Changed: true,
                Summary: string.Format(
                    CultureInfo.InvariantCulture,
                    "Intersection: primary ({0} triangles) intersected with secondary ({1} triangles) = {2} triangles. Volume: {3:0.###}",
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
                Summary: $"Boolean intersection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new OperationResult(
                Changed: false,
                Summary: $"Unexpected error during boolean intersection: {ex.Message}");
        }
    }
}
