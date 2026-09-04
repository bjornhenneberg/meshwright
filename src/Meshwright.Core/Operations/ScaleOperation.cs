using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Scales the mesh isotropically around a center point.
/// </summary>
public sealed class ScaleOperation : MeshOperationBase
{
    private readonly double _scale;
    private readonly Vector3d _center;

    public ScaleOperation(double scale, Vector3d center)
    {
        _scale = scale;
        _center = center;
    }

    public override string Name => "Scale";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        Transform.ScaleMesh(mesh, _scale, _center);
        return new OperationResult(
            Changed: true,
            Summary: $"Scaled by {_scale:0.##}x around ({_center.x:0.##}, {_center.y:0.##}, {_center.z:0.##})");
    }
}
