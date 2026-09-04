using g3;
using Meshwright.Geometry.Edit;

namespace Meshwright.Core.Operations;

/// <summary>
/// Rotates the mesh by an angle (in degrees) around an axis through a center point.
/// </summary>
public sealed class RotateOperation : MeshOperationBase
{
    private readonly double _angleDegrees;
    private readonly Vector3d _axis;
    private readonly Vector3d _center;

    public RotateOperation(double angleDegrees, Vector3d axis, Vector3d center)
    {
        _angleDegrees = angleDegrees;
        _axis = axis;
        _center = center;
    }

    public override string Name => "Rotate";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        Transform.RotateMesh(mesh, _angleDegrees, _axis, _center);

        string axisName = GetAxisName(_axis);
        return new OperationResult(
            Changed: true,
            Summary: $"Rotated {_angleDegrees:0.##}° around {axisName}-axis through ({_center.x:0.##}, {_center.y:0.##}, {_center.z:0.##})");
    }

    private static string GetAxisName(Vector3d axis)
    {
        axis.Normalize();
        if (Math.Abs(axis.x - 1.0) < 0.01) return "X";
        if (Math.Abs(axis.y - 1.0) < 0.01) return "Y";
        if (Math.Abs(axis.z - 1.0) < 0.01) return "Z";
        return "custom";
    }
}
