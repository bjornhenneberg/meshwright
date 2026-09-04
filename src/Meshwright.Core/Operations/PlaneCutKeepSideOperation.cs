using System.Globalization;
using g3;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Plane cut keeping the positive side (SPECIFICATION.md §5.1 "Edit"). Wraps <see cref="PlaneCut"/>
/// to cut along a plane and keep only the positive side, discarding the negative side and sealing
/// with a flat cap.
/// </summary>
public sealed class PlaneCutKeepSideOperation : MeshOperationBase
{
    private readonly Vector3d _planePoint;
    private readonly Vector3d _planeNormal;
    private readonly HoleFillMode _capMode;
    private readonly PlaneCut _planeCut = new();

    public PlaneCutKeepSideOperation(Vector3d planePoint, Vector3d planeNormal, HoleFillMode capMode = HoleFillMode.Planar)
    {
        if (planeNormal.LengthSquared < 0.99)
        {
            throw new ArgumentException("Plane normal must be normalized.", nameof(planeNormal));
        }

        _planePoint = planePoint;
        _planeNormal = planeNormal;
        _capMode = capMode;
    }

    public override string Name => "Plane Cut (Keep Positive Side)";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        PlaneCutResult result = _planeCut.Cut(mesh, _planePoint, _planeNormal, CutMode.Keep, _capMode);

        if (!result.MeshWasModified)
        {
            return new OperationResult(
                Changed: false,
                Summary: "Plane passed through no geometry — mesh left unchanged.");
        }

        // Replace the input mesh with the positive side result by swapping internal data
        // Get all vertices and triangles from result and rebuild mesh
        var vertexMap = new Dictionary<int, int>();
        var newTriangles = new List<Index3i>();

        // Collect vertices and triangles from result
        foreach (int vid in result.PositiveSideMesh.VertexIndices())
        {
            vertexMap[vid] = mesh.AppendVertex(result.PositiveSideMesh.GetVertex(vid));
        }
        foreach (int tid in result.PositiveSideMesh.TriangleIndices())
        {
            Index3i tri = result.PositiveSideMesh.GetTriangle(tid);
            mesh.AppendTriangle(vertexMap[tri.a], vertexMap[tri.b], vertexMap[tri.c]);
        }

        // Compact the mesh to remove old orphaned data
        mesh.CompactInPlace();

        return new OperationResult(
            Changed: true,
            Summary: string.Format(
                CultureInfo.InvariantCulture,
                "Cut plane kept positive side with {0} cap triangles ({1} -> {2} triangles).",
                result.CapTrianglesAdded,
                result.TrianglesBefore,
                result.TrianglesAfter));
    }
}
