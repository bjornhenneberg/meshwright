using System.Globalization;
using g3;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Plane cut keeping both sides (SPECIFICATION.md §5.1 "Edit"). Unlike the Keep/Discard
/// operations, nothing is thrown away: the mesh ends up holding both halves, each capped and
/// each its own shell, which is what lets a model be split for printing and reassembled after.
///
/// <para>
/// The two halves are <b>moved apart</b> along the cut normal before they are merged into one
/// mesh (backlog item 26). Left where the cut leaves them, the two caps occupy the same plane and
/// the same area, so every cap triangle overlaps its opposite number: the Menger sponge sample
/// split at its centre reported 1,640 issues — self-intersections, non-manifold edges and
/// duplicate vertex locations — while each half measured on its own was closed, single-shell and
/// issue-free. Two solids sharing a face are also not two printable parts: a slicer handed them
/// would fuse them back into the model the user just cut up. The separation is a pure translation
/// along the plane normal, so putting the halves back together — and any registration pin that
/// makes them mate — is exactly undone by translating back.
/// </para>
/// </summary>
public sealed class PlaneCutSplitOperation : MeshOperationBase
{
    private readonly Vector3d _planePoint;
    private readonly Vector3d _planeNormal;
    private readonly HoleFillMode _capMode;
    private readonly bool _addCap;
    private readonly RegistrationPinOptions? _pin;
    private readonly PlaneCut _planeCut = new();

    /// <param name="pin">
    /// Optional peg-and-socket registration pin on the mating faces, so the two halves align when
    /// they are put back together (SPECIFICATION.md §5.1). If the pin cannot be built the whole
    /// operation refuses and the mesh is left unchanged — halves that silently do not align are
    /// worse than a refusal the user can act on.
    /// </param>
    public PlaneCutSplitOperation(Vector3d planePoint, Vector3d planeNormal, HoleFillMode capMode = HoleFillMode.Planar, bool addCap = true, RegistrationPinOptions? pin = null)
    {
        if (planeNormal.LengthSquared < 0.99)
        {
            throw new ArgumentException("Plane normal must be normalized.", nameof(planeNormal));
        }

        _planePoint = planePoint;
        _planeNormal = planeNormal;
        _capMode = capMode;
        _addCap = addCap;
        _pin = pin;
    }

    public override string Name => "Plane Cut (Split)";

    /// <summary>
    /// Floor on how far apart the halves are moved. A slicer only treats two solids as two objects
    /// if it can get a nozzle between them, and 0.8 mm is the widest nozzle in common use, so a
    /// millimetre clears the widest of them with room to spare. It is a floor rather than the whole
    /// rule because a millimetre is most of the 2 mm Menger sponge and invisible beside the 120 mm
    /// Eiffel tower; see <see cref="SeparationFraction"/>.
    /// </summary>
    private const double MinimumSeparationMm = 1.0;

    /// <summary>
    /// The scale-relative half of the separation rule: the halves are moved apart by this fraction
    /// of the model's own extent along the cut normal, so the gap reads the same on a model of any
    /// size, and never by less than <see cref="MinimumSeparationMm"/>.
    /// </summary>
    private const double SeparationFraction = 0.05;

    protected override OperationResult Execute(DMesh3 mesh)
    {
        AxisAlignedBox3d boundsBefore = mesh.GetBounds();

        PlaneCutResult result = _planeCut.Cut(mesh, _planePoint, _planeNormal, CutMode.Split, _capMode, _addCap, _pin);

        if (!result.MeshWasModified)
        {
            return new OperationResult(
                Changed: false,
                Summary: result.Pin is { PinPlaced: false } refused
                    ? refused.Message
                    : "Plane passed through no geometry — mesh left unchanged.");
        }

        mesh.Copy(result.PositiveSideMesh);

        double separation = SeparationDistance(boundsBefore, result);
        Vector3d offset = -_planeNormal * separation;

        int negativeTriangles = 0;
        if (result.NegativeSideMesh is { } negativeSide)
        {
            var vertexMap = new Dictionary<int, int>();
            foreach (int vid in negativeSide.VertexIndices())
            {
                vertexMap[vid] = mesh.AppendVertex(negativeSide.GetVertex(vid) + offset);
            }

            foreach (int tid in negativeSide.TriangleIndices())
            {
                Index3i tri = negativeSide.GetTriangle(tid);
                mesh.AppendTriangle(vertexMap[tri.a], vertexMap[tri.b], vertexMap[tri.c]);
                negativeTriangles++;
            }
        }

        mesh.CompactInPlace();

        string summary = _addCap
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Split into two shells with {0} cap triangles ({1} -> {2} triangles, {3} on the negative side).",
                result.CapTrianglesAdded,
                result.TrianglesBefore,
                mesh.TriangleCount,
                negativeTriangles)
            : string.Format(
                CultureInfo.InvariantCulture,
                "Split into two shells, left open ({0} -> {1} triangles, {2} on the negative side).",
                result.TrianglesBefore,
                mesh.TriangleCount,
                negativeTriangles);

        if (result.NegativeSideMesh is not null)
        {
            summary = string.Format(
                CultureInfo.InvariantCulture,
                "{0} Halves moved {1:0.##} mm apart along the cut normal so they do not touch.",
                summary,
                separation);
        }

        if (result.Pin is { PinPlaced: true } pin)
        {
            summary = summary + " " + pin.Message;
        }

        return new OperationResult(Changed: true, Summary: summary);
    }

    /// <summary>
    /// How far to move the negative half away from the positive one. Two terms, and the larger of
    /// the scale-relative one and the absolute floor wins, plus the peg's protrusion when there is
    /// a registration pin: a peg standing proud of its mating face is still inside the socket it
    /// was cut for until the halves are pulled apart by at least its own length, and halves that
    /// still interpenetrate are the bug this separation exists to fix.
    /// </summary>
    private double SeparationDistance(AxisAlignedBox3d boundsBefore, PlaneCutResult result)
    {
        Vector3d diagonal = boundsBefore.Diagonal;
        double extentAlongNormal =
            Math.Abs(_planeNormal.x) * diagonal.x +
            Math.Abs(_planeNormal.y) * diagonal.y +
            Math.Abs(_planeNormal.z) * diagonal.z;

        double pegProtrusion = result.Pin is { PinPlaced: true } placed ? placed.DepthAchieved : 0.0;

        return pegProtrusion + Math.Max(MinimumSeparationMm, SeparationFraction * extentAlongNormal);
    }
}
