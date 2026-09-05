using System.Globalization;
using g3;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Plane cut keeping both sides (SPECIFICATION.md §5.1 "Edit"). Unlike the Keep/Discard
/// operations, nothing is thrown away: the mesh ends up holding both halves, each capped and
/// each its own shell, which is what lets a model be split for printing and reassembled after.
/// </summary>
public sealed class PlaneCutSplitOperation : MeshOperationBase
{
    private readonly Vector3d _planePoint;
    private readonly Vector3d _planeNormal;
    private readonly HoleFillMode _capMode;
    private readonly PlaneCut _planeCut = new();

    public PlaneCutSplitOperation(Vector3d planePoint, Vector3d planeNormal, HoleFillMode capMode = HoleFillMode.Planar)
    {
        if (planeNormal.LengthSquared < 0.99)
        {
            throw new ArgumentException("Plane normal must be normalized.", nameof(planeNormal));
        }

        _planePoint = planePoint;
        _planeNormal = planeNormal;
        _capMode = capMode;
    }

    public override string Name => "Plane Cut (Split)";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        PlaneCutResult result = _planeCut.Cut(mesh, _planePoint, _planeNormal, CutMode.Split, _capMode);

        if (!result.MeshWasModified)
        {
            return new OperationResult(
                Changed: false,
                Summary: "Plane passed through no geometry — mesh left unchanged.");
        }

        mesh.Copy(result.PositiveSideMesh);

        int negativeTriangles = 0;
        if (result.NegativeSideMesh is { } negativeSide)
        {
            var vertexMap = new Dictionary<int, int>();
            foreach (int vid in negativeSide.VertexIndices())
            {
                vertexMap[vid] = mesh.AppendVertex(negativeSide.GetVertex(vid));
            }

            foreach (int tid in negativeSide.TriangleIndices())
            {
                Index3i tri = negativeSide.GetTriangle(tid);
                mesh.AppendTriangle(vertexMap[tri.a], vertexMap[tri.b], vertexMap[tri.c]);
                negativeTriangles++;
            }
        }

        mesh.CompactInPlace();

        return new OperationResult(
            Changed: true,
            Summary: string.Format(
                CultureInfo.InvariantCulture,
                "Split into two shells with {0} cap triangles ({1} -> {2} triangles, {3} on the negative side).",
                result.CapTrianglesAdded,
                result.TrianglesBefore,
                mesh.TriangleCount,
                negativeTriangles));
    }
}
