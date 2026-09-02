using g3;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// §5.1 "normal unification" repair: makes triangle winding consistent within each connected
/// shell and orients each shell outward. Thin wrapper - all the work is in
/// <see cref="NormalUnificationRepair"/>.
/// </summary>
public sealed class UnifyNormalsOperation : MeshOperationBase
{
    public override string Name => "Unify Normals";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        NormalUnificationRepair.Result result = NormalUnificationRepair.Apply(mesh);

        string shellNoun = result.ShellCount == 1 ? "shell" : "shells";
        string summary = result.FlippedTriangleCount == 0
            ? $"Unify normals: already consistent across {result.ShellCount} {shellNoun}, no triangles flipped."
            : $"Unified normals: flipped {result.FlippedTriangleCount} triangles across {result.ShellCount} {shellNoun}.";

        return new OperationResult(result.FlippedTriangleCount > 0, summary);
    }
}
