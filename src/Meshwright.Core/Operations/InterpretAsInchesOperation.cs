using g3;
using Meshwright.Geometry.Edit;
using Meshwright.IO.Units;

namespace Meshwright.Core.Operations;

/// <summary>
/// Re-reads the mesh as though its file had been written in inches: every coordinate multiplied by
/// 25.4, about the world origin.
///
/// <para>
/// About the <b>origin</b>, not about the model's centre. Reinterpreting a unit is not a resize —
/// it changes what every number in the file meant, including how far the model sits from the
/// origin. Scaling about the centroid would leave a part that a CAD package placed 3 inches off
/// centre sitting 3 mm off centre, which is a different model from the one the file describes.
/// </para>
///
/// <para>
/// A separate operation from <see cref="ScaleOperation"/> only so that the undo entry and the
/// status line say what happened. "Scaled by 25.4x around (0, 0, 0)" is arithmetically the same
/// sentence and tells a user nothing about why their model just grew.
/// </para>
/// </summary>
public sealed class InterpretAsInchesOperation : MeshOperationBase
{
    public override string Name => "Interpret as inches";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        if (mesh.TriangleCount == 0)
        {
            return new OperationResult(Changed: false, Summary: "Nothing loaded to rescale.");
        }

        Transform.ScaleMesh(mesh, ImportUnits.MillimetresPerInch, Vector3d.Zero);

        AxisAlignedBox3d bounds = mesh.GetBounds();
        return new OperationResult(
            Changed: true,
            Summary: $"Read as inches: now {bounds.Width:0.##} x {bounds.Height:0.##} x {bounds.Depth:0.##} mm");
    }
}
