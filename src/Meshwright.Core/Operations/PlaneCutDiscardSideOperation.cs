using System.Globalization;
using g3;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Plane cut discarding the positive side (SPECIFICATION.md §5.1 "Edit"). Wraps <see cref="PlaneCut"/>
/// to cut along a plane and keep only the negative side, discarding the positive side and sealing
/// with a flat cap.
/// </summary>
public sealed class PlaneCutDiscardSideOperation : MeshOperationBase
{
    private readonly Vector3d _planePoint;
    private readonly Vector3d _planeNormal;
    private readonly HoleFillMode _capMode;
    private readonly PlaneCut _planeCut = new();

    public PlaneCutDiscardSideOperation(Vector3d planePoint, Vector3d planeNormal, HoleFillMode capMode = HoleFillMode.Planar)
    {
        if (planeNormal.LengthSquared < 0.99)
        {
            throw new ArgumentException("Plane normal must be normalized.", nameof(planeNormal));
        }

        _planePoint = planePoint;
        _planeNormal = planeNormal;
        _capMode = capMode;
    }

    public override string Name => "Plane Cut (Discard Positive Side)";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        PlaneCutResult result = _planeCut.Cut(mesh, _planePoint, _planeNormal, CutMode.Discard, _capMode);

        if (!result.MeshWasModified)
        {
            return new OperationResult(
                Changed: false,
                Summary: "Plane passed through no geometry — mesh left unchanged.");
        }

        // For discard mode, get the positive side mesh from the cut result
        // (the Cut method returns both sides in PositiveSideMesh/NegativeSideMesh based on mode)
        DMesh3 resultMesh = result.PositiveSideMesh;

        // Replace the mesh's contents with the kept side. Appending instead would leave the
        // half this cut was asked to discard sitting in the mesh alongside the result.
        mesh.Copy(resultMesh);
        mesh.CompactInPlace();

        return new OperationResult(
            Changed: true,
            Summary: string.Format(
                CultureInfo.InvariantCulture,
                "Cut plane discarded positive side with {0} cap triangles ({1} -> {2} triangles).",
                result.CapTrianglesAdded,
                result.TrianglesBefore,
                result.TrianglesAfter));
    }
}
