using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Translates the mesh by a 3D offset (X, Y, Z in mm).
/// </summary>
public sealed class TranslateOperation : MeshOperationBase
{
    private readonly Vector3d _offset;

    public TranslateOperation(Vector3d offset)
    {
        _offset = offset;
    }

    public override string Name => "Translate";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        Transform.TranslateMesh(mesh, _offset);
        return new OperationResult(
            Changed: true,
            Summary: $"Translated by ({_offset.x:0.##}, {_offset.y:0.##}, {_offset.z:0.##}) mm");
    }
}
