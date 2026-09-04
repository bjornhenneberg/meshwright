using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Mirrors (reflects) the mesh across a plane defined by a point and normal.
/// </summary>
public sealed class MirrorOperation : MeshOperationBase
{
    private readonly Vector3d _planePoint;
    private readonly Vector3d _planeNormal;

    public MirrorOperation(Vector3d planePoint, Vector3d planeNormal)
    {
        _planePoint = planePoint;
        _planeNormal = planeNormal;
    }

    public override string Name => "Mirror";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        Transform.MirrorMesh(mesh, _planePoint, _planeNormal);
        return new OperationResult(
            Changed: true,
            Summary: $"Mirrored across plane at ({_planePoint.x:0.##}, {_planePoint.y:0.##}, {_planePoint.z:0.##}) with normal ({_planeNormal.x:0.##}, {_planeNormal.y:0.##}, {_planeNormal.z:0.##})");
    }
}
