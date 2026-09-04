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

        // Replace the input mesh with the result by appending its geometry
        var vertexMap = new Dictionary<int, int>();
        foreach (int vid in resultMesh.VertexIndices())
        {
            vertexMap[vid] = mesh.AppendVertex(resultMesh.GetVertex(vid));
        }
        foreach (int tid in resultMesh.TriangleIndices())
        {
            Index3i tri = resultMesh.GetTriangle(tid);
            mesh.AppendTriangle(vertexMap[tri.a], vertexMap[tri.b], vertexMap[tri.c]);
        }

        // Compact the mesh to remove old orphaned data
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
