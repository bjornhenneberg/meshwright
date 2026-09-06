using g3;
using Meshwright.Geometry.Repair;

namespace Meshwright.Geometry.Edit;

/// <summary>Result of a plane cut operation: the meshes on each side and cap triangles.</summary>
public sealed record PlaneCutResult(
    DMesh3 PositiveSideMesh,
    DMesh3? NegativeSideMesh,
    int CapTrianglesAdded,
    int TrianglesBefore,
    int TrianglesAfter,
    bool MeshWasModified,
    RegistrationPinResult? Pin = null);

/// <summary>
/// Plane cut operation (SPECIFICATION.md §5.1): split a mesh along a plane defined by a point
/// and normal vector, with three modes (Keep/Discard/Split) and optional flat-cap generation.
/// </summary>
public sealed class PlaneCut
{
    /// <summary>
    /// Cuts <paramref name="mesh"/> along a plane and returns the result(s) according to
    /// <paramref name="mode"/>. The mesh is not mutated; instead, new mesh(es) are returned.
    /// </summary>
    /// <param name="mesh">Input mesh (not mutated).</param>
    /// <param name="planePoint">A point on the cutting plane.</param>
    /// <param name="planeNormal">Normal vector of the cutting plane (must be normalized).</param>
    /// <param name="mode">CutMode.Keep keeps positive side + cap; Discard keeps negative side + cap; Split returns both + caps.</param>
    /// <param name="capMode">HoleFillMode for the cap (Flat, Planar, or Smooth).</param>
    /// <param name="pin">
    /// When set, a peg-and-socket registration pin pair is generated on the two mating faces so the
    /// halves align when they are put back together. Only <see cref="CutMode.Split"/> with a cap can
    /// carry one — there is no mating face otherwise. If the pin cannot be built, <b>nothing</b> is:
    /// the cut is refused too, the mesh is returned unchanged and <see cref="PlaneCutResult.Pin"/>
    /// explains why. Splitting anyway would hand back two halves that silently do not align, which
    /// is the failure §4 ("never silently destroy the model") and §11's refusal rule exist to stop.
    /// </param>
    /// <param name="addCap">
    /// When false, the cross-section is left open: no cap triangles are generated and the result
    /// mesh(es) carry a boundary loop where the cut passed through the surface. SPECIFICATION.md
    /// §5.1 calls the cap "optional" — this is the knob that makes it so.
    /// </param>
    /// <returns>
    /// PlaneCutResult with PositiveSideMesh (always present), NegativeSideMesh (only for Split mode),
    /// and cap triangle count. If the plane passes through no geometry, PositiveSideMesh is a copy
    /// of the input and MeshWasModified is false.
    /// </returns>
    public PlaneCutResult Cut(DMesh3 mesh, Vector3d planePoint, Vector3d planeNormal, CutMode mode, HoleFillMode capMode, bool addCap = true, RegistrationPinOptions? pin = null)
    {
        if (planeNormal.LengthSquared < 0.99) // Rough normalization check
        {
            throw new ArgumentException("Plane normal must be normalized.", nameof(planeNormal));
        }

        int trianglesBefore = mesh.TriangleCount;

        // Classify triangles and identify cut edges
        var triangleClassification = ClassifyTriangles(mesh, planePoint, planeNormal);
        var cutEdges = IdentifyCutEdges(mesh, planePoint, planeNormal, triangleClassification);

        // If no triangles are cut, return the mesh unchanged
        if (cutEdges.Count == 0 && !triangleClassification.Values.Any(c => c == TriangleClassification.Mixed))
        {
            var meshCopy = new DMesh3(mesh, bCompact: false);
            return new PlaneCutResult(
                PositiveSideMesh: meshCopy,
                NegativeSideMesh: null,
                CapTrianglesAdded: 0,
                TrianglesBefore: trianglesBefore,
                TrianglesAfter: trianglesBefore,
                MeshWasModified: false);
        }

        // Split triangles that cross the plane
        var splitMesh = SplitCutTriangles(mesh, planePoint, planeNormal, triangleClassification, cutEdges);

        // Recover the cut cross-section from the segments the split triangles left behind. A cut
        // through anything with a hole in it crosses several separate boundary loops at once, so
        // this is a set of loops, some of them holes inside others, not one loop.
        var basis = PlaneBasis.Create(planePoint, planeNormal);
        List<List<int>> capLoops = addCap ? CutCrossSection.ExtractLoops(splitMesh.CutSegments) : [];

        // A registration pin is planned before anything is built, because a pin that cannot be built
        // refuses the whole cut rather than quietly producing halves that do not align.
        RegistrationPinPlan? pinPlan = null;
        if (pin is not null)
        {
            if (mode != CutMode.Split)
            {
                return Unchanged(mesh, trianglesBefore, RefusePin(pin, "A registration pin needs both halves of the cut, so it can only be added in Split mode. Mesh left unchanged."));
            }

            if (!addCap)
            {
                return Unchanged(mesh, trianglesBefore, RefusePin(pin, "A registration pin needs a capped cut — an open cross-section has no mating face to put one on. Mesh left unchanged."));
            }

            pinPlan = RegistrationPinBuilder.Plan(mesh, splitMesh.SplitMesh, capLoops, basis, pin, out RegistrationPinResult refusal);
            if (pinPlan is null)
            {
                return Unchanged(mesh, trianglesBefore, refusal);
            }
        }

        // Without a pin both halves share one triangulation, wound opposite ways. With one, each half
        // gets its own: the peg half's cap is punched out at the peg diameter and the socket half's
        // at the socket diameter, which is what the clearance between them is made of.
        bool flatFan = capMode == HoleFillMode.Flat;
        List<Index3i> capTriangles = capLoops.Count == 0
            ? []
            : CutCrossSection.Triangulate(capLoops, splitMesh.SplitMesh, basis, flatFan: flatFan);
        List<Index3i> positiveCapTriangles = capTriangles;
        List<Index3i> negativeCapTriangles = capTriangles;
        if (pinPlan is not null)
        {
            positiveCapTriangles = CutCrossSection.Triangulate(
                WithExtraLoop(capLoops, pinPlan.PegLoop), splitMesh.SplitMesh, basis, flatFan: flatFan);
            negativeCapTriangles = CutCrossSection.Triangulate(
                WithExtraLoop(capLoops, pinPlan.SocketLoop), splitMesh.SplitMesh, basis, flatFan: flatFan);
            capTriangles = positiveCapTriangles;
        }

        int capTrianglesAdded = 0;

        // Build the result mesh(es)
        DMesh3 positiveSide = new DMesh3();
        // Built for Discard as well as Split: Discard's whole job is to return the negative side,
        // so skipping it there left the mode with nothing to return but the positive side —
        // making Discard behave identically to Keep.
        DMesh3? negativeSide = mode is CutMode.Split or CutMode.Discard ? new DMesh3() : null;
        RegistrationPinResult? pinResult = null;

        var vertexMap = new Dictionary<int, (int posMeshId, int? negMeshId)>();

        // Copy positive-side geometry and build vertex map
        foreach (int tid in splitMesh.PositiveSideTriangles)
        {
            Index3i tri = splitMesh.SplitMesh.GetTriangle(tid);
            for (int i = 0; i < 3; i++)
            {
                int vid = tri[i];
                if (!vertexMap.ContainsKey(vid))
                {
                    Vector3d pos = splitMesh.SplitMesh.GetVertex(vid);
                    int posId = positiveSide.AppendVertex(pos);
                    vertexMap[vid] = (posId, null);
                }
            }
            int pv0 = vertexMap[tri.a].posMeshId;
            int pv1 = vertexMap[tri.b].posMeshId;
            int pv2 = vertexMap[tri.c].posMeshId;
            positiveSide.AppendTriangle(pv0, pv1, pv2);
        }

        // Add the cap to the positive side. The cap triangles come back wound counter-clockwise
        // in the plane basis, i.e. facing along the plane normal; the positive side's solid sits
        // on that same side, so its cap has to face the other way to point out of the solid.
        // The loops are expressed in the split mesh's vertex ids, so each cap vertex has to be
        // translated into the mesh being filled first — passing split-mesh ids straight through
        // had the cap stitching together whichever unrelated vertices happened to hold those
        // indices, which is what left cut results in disconnected pieces instead of one shell.
        int? PositiveLookup(int vid) => vertexMap.TryGetValue(vid, out (int posMeshId, int? negMeshId) ids) ? ids.posMeshId : null;
        void RememberPositive(int vid, int mappedId) =>
            vertexMap[vid] = (mappedId, vertexMap.TryGetValue(vid, out (int posMeshId, int? negMeshId) e) ? e.negMeshId : null);

        if (positiveCapTriangles.Count > 0)
        {
            capTrianglesAdded = AppendCap(
                positiveSide,
                positiveCapTriangles,
                splitMesh.SplitMesh,
                PositiveLookup,
                RememberPositive,
                reverseWinding: true);
        }

        // The peg grows out of the hole the cap triangulation just left at the pin circle, sharing
        // its boundary vertices, so the half stays one closed shell rather than gaining a floating
        // cylinder next to a hole.
        (int[] PegRing, int PegEnd)? peg = null;
        if (pinPlan is not null)
        {
            int[] pegBoundary = TranslateRing(positiveSide, splitMesh.SplitMesh, pinPlan.PegLoop, PositiveLookup, RememberPositive);
            peg = RegistrationPinBuilder.AppendPeg(positiveSide, pegBoundary, planeNormal.Normalized, pinPlan.PegDepth);
        }

        // Copy negative-side geometry whenever it is going to be returned (Split or Discard)
        if (negativeSide != null)
        {
            var negVertexMap = new Dictionary<int, int>();
            foreach (int tid in splitMesh.NegativeSideTriangles)
            {
                Index3i tri = splitMesh.SplitMesh.GetTriangle(tid);
                for (int i = 0; i < 3; i++)
                {
                    int vid = tri[i];
                    if (!negVertexMap.ContainsKey(vid))
                    {
                        Vector3d pos = splitMesh.SplitMesh.GetVertex(vid);
                        negVertexMap[vid] = negativeSide.AppendVertex(pos);
                    }
                }
                int nv0 = negVertexMap[tri.a];
                int nv1 = negVertexMap[tri.b];
                int nv2 = negVertexMap[tri.c];
                negativeSide.AppendTriangle(nv0, nv1, nv2);
            }

            // The negative half's solid sits below the plane, so its cap faces along the plane
            // normal and keeps the triangulation's own winding. Capping both halves the same way
            // round leaves one of them inside out — its faces point into the solid and its volume
            // comes back with the wrong sign.
            int? NegativeLookup(int vid) => negVertexMap.TryGetValue(vid, out int id) ? id : null;
            void RememberNegative(int vid, int mappedId) => negVertexMap[vid] = mappedId;

            if (negativeCapTriangles.Count > 0)
            {
                capTrianglesAdded += AppendCap(
                    negativeSide,
                    negativeCapTriangles,
                    splitMesh.SplitMesh,
                    NegativeLookup,
                    RememberNegative,
                    reverseWinding: false);
            }

            if (pinPlan is not null)
            {
                int[] socketBoundary = TranslateRing(negativeSide, splitMesh.SplitMesh, pinPlan.SocketLoop, NegativeLookup, RememberNegative);
                (int[] socketRing, int socketEnd) = RegistrationPinBuilder.AppendSocket(negativeSide, socketBoundary, planeNormal.Normalized, pinPlan.SocketDepth);

                (int[] pegRing, int pegEnd) = peg!.Value;
                Vector3d axis = planeNormal.Normalized;
                double pegDiameter = 2.0 * RegistrationPinBuilder.MeasureRingRadius(positiveSide, pegRing, pinPlan.Center, axis);
                double socketDiameter = 2.0 * RegistrationPinBuilder.MeasureRingRadius(negativeSide, socketRing, pinPlan.Center, axis);
                double pegDepth = RegistrationPinBuilder.MeasureAxialDepth(positiveSide, pegEnd, pinPlan.Center, axis);
                double socketDepthMeasured = RegistrationPinBuilder.MeasureAxialDepth(negativeSide, socketEnd, pinPlan.Center, axis);

                pinResult = new RegistrationPinResult(
                    PinPlaced: true,
                    DiameterRequested: pinPlan.Options.Diameter,
                    PegDiameterAchieved: pegDiameter,
                    SocketDiameterAchieved: socketDiameter,
                    ClearanceRequested: pinPlan.Options.Clearance,
                    ClearanceAchieved: (socketDiameter - pegDiameter) / 2.0,
                    DepthRequested: pinPlan.PegDepth,
                    DepthAchieved: pegDepth,
                    Center: pinPlan.Center,
                    Axis: axis,
                    LargestDiameterThatFits: pinPlan.LargestDiameterThatFits,
                    Message: string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Registration pin: Ø{0:0.###} mm peg {1:0.###} mm long on the positive half, Ø{2:0.###} mm socket {3:0.###} mm deep on the negative half ({4:0.###} mm clearance).",
                        pegDiameter,
                        pegDepth,
                        socketDiameter,
                        socketDepthMeasured,
                        (socketDiameter - pegDiameter) / 2.0));
            }
        }

        // PositiveSideMesh carries the mesh the caller keeps, which for Discard is the negative
        // side; NegativeSideMesh is the extra half only Split asks for.
        return new PlaneCutResult(
            PositiveSideMesh: mode == CutMode.Discard ? negativeSide! : positiveSide,
            NegativeSideMesh: mode == CutMode.Split ? negativeSide : null,
            CapTrianglesAdded: capTrianglesAdded,
            TrianglesBefore: trianglesBefore,
            TrianglesAfter: positiveSide.TriangleCount + (negativeSide?.TriangleCount ?? 0),
            MeshWasModified: true,
            Pin: pinResult);
    }

    /// <summary>The cross-section loops plus one more — the pin circle, which the cap triangulation's
    /// parity nesting then treats as a hole in the cap like any other enclosed loop.</summary>
    private static List<IReadOnlyList<int>> WithExtraLoop(List<List<int>> loops, List<int> extra)
    {
        var combined = new List<IReadOnlyList<int>>(loops.Count + 1);
        foreach (List<int> loop in loops)
        {
            combined.Add(loop);
        }

        combined.Add(extra);
        return combined;
    }

    /// <summary>A refusal that leaves the input mesh exactly as it was.</summary>
    private static PlaneCutResult Unchanged(DMesh3 mesh, int trianglesBefore, RegistrationPinResult pin) =>
        new(
            PositiveSideMesh: new DMesh3(mesh, bCompact: false),
            NegativeSideMesh: null,
            CapTrianglesAdded: 0,
            TrianglesBefore: trianglesBefore,
            TrianglesAfter: trianglesBefore,
            MeshWasModified: false,
            Pin: pin);

    private static RegistrationPinResult RefusePin(RegistrationPinOptions options, string message) =>
        new(
            PinPlaced: false,
            DiameterRequested: options.Diameter,
            PegDiameterAchieved: 0.0,
            SocketDiameterAchieved: 0.0,
            ClearanceRequested: options.Clearance,
            ClearanceAchieved: 0.0,
            DepthRequested: options.Depth ?? options.Diameter,
            DepthAchieved: 0.0,
            Center: Vector3d.Zero,
            Axis: Vector3d.Zero,
            LargestDiameterThatFits: 0.0,
            Message: message);

    /// <summary>
    /// Maps a loop of split-mesh vertex ids into <paramref name="target"/>, appending any the cap did
    /// not already need, and preserving the loop's order — the pin's cylinder is wound against it.
    /// </summary>
    private static int[] TranslateRing(
        DMesh3 target,
        DMesh3 splitMesh,
        IReadOnlyList<int> ring,
        Func<int, int?> lookup,
        Action<int, int> remember)
    {
        var ids = new int[ring.Count];
        for (int i = 0; i < ring.Count; i++)
        {
            int? mapped = lookup(ring[i]);
            if (mapped is null)
            {
                int appended = target.AppendVertex(splitMesh.GetVertex(ring[i]));
                remember(ring[i], appended);
                mapped = appended;
            }

            ids[i] = mapped.Value;
        }

        return ids;
    }

    private enum TriangleClassification
    {
        Positive,
        Negative,
        Mixed,  // Triangle crosses the plane
        OnPlane // All vertices on the plane (rare edge case)
    }

    private Dictionary<int, TriangleClassification> ClassifyTriangles(DMesh3 mesh, Vector3d planePoint, Vector3d planeNormal)
    {
        var result = new Dictionary<int, TriangleClassification>();

        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d v0 = mesh.GetVertex(tri.a);
            Vector3d v1 = mesh.GetVertex(tri.b);
            Vector3d v2 = mesh.GetVertex(tri.c);

            double d0 = SignedDistance(v0, planePoint, planeNormal);
            double d1 = SignedDistance(v1, planePoint, planeNormal);
            double d2 = SignedDistance(v2, planePoint, planeNormal);

            const double tolerance = 1e-10;
            bool v0Pos = d0 > tolerance;
            bool v1Pos = d1 > tolerance;
            bool v2Pos = d2 > tolerance;
            bool v0Neg = d0 < -tolerance;
            bool v1Neg = d1 < -tolerance;
            bool v2Neg = d2 < -tolerance;

            if ((v0Pos && v1Pos && v2Pos) || (!v0Neg && !v1Neg && !v2Neg && !v0Pos && !v1Pos && !v2Pos && Math.Abs(d0) < tolerance && Math.Abs(d1) < tolerance && Math.Abs(d2) < tolerance))
            {
                if (Math.Abs(d0) < tolerance && Math.Abs(d1) < tolerance && Math.Abs(d2) < tolerance)
                {
                    result[tid] = TriangleClassification.OnPlane;
                }
                else if (v0Pos || v1Pos || v2Pos)
                {
                    result[tid] = TriangleClassification.Positive;
                }
                else
                {
                    result[tid] = TriangleClassification.Positive; // Default to positive if on plane
                }
            }
            else if (v0Neg && v1Neg && v2Neg)
            {
                result[tid] = TriangleClassification.Negative;
            }
            else
            {
                result[tid] = TriangleClassification.Mixed;
            }
        }

        return result;
    }

    private double SignedDistance(Vector3d point, Vector3d planePoint, Vector3d planeNormal)
    {
        return (point - planePoint).Dot(planeNormal);
    }

    private struct CutEdge
    {
        public int VertexAId;
        public int VertexBId;
        public Vector3d IntersectionPoint;
    }

    private List<CutEdge> IdentifyCutEdges(DMesh3 mesh, Vector3d planePoint, Vector3d planeNormal, Dictionary<int, TriangleClassification> classification)
    {
        var result = new List<CutEdge>();
        var processedEdges = new HashSet<(int, int)>();

        foreach (int tid in mesh.TriangleIndices())
        {
            if (classification[tid] != TriangleClassification.Mixed)
            {
                continue;
            }

            Index3i tri = mesh.GetTriangle(tid);
            var vertices = new[] { tri.a, tri.b, tri.c };

            for (int i = 0; i < 3; i++)
            {
                int v0 = vertices[i];
                int v1 = vertices[(i + 1) % 3];

                double d0 = SignedDistance(mesh.GetVertex(v0), planePoint, planeNormal);
                double d1 = SignedDistance(mesh.GetVertex(v1), planePoint, planeNormal);

                // Check if edge crosses the plane
                if ((d0 > 1e-10 && d1 < -1e-10) || (d0 < -1e-10 && d1 > 1e-10))
                {
                    var edgeKey = (Math.Min(v0, v1), Math.Max(v0, v1));
                    if (!processedEdges.Contains(edgeKey))
                    {
                        processedEdges.Add(edgeKey);

                        // Compute intersection point
                        Vector3d p0 = mesh.GetVertex(v0);
                        Vector3d p1 = mesh.GetVertex(v1);
                        double t = -d0 / (d1 - d0);
                        Vector3d intersection = p0 + t * (p1 - p0);

                        result.Add(new CutEdge
                        {
                            VertexAId = v0,
                            VertexBId = v1,
                            IntersectionPoint = intersection
                        });
                    }
                }
            }
        }

        return result;
    }

    private struct SplitMeshResult
    {
        public DMesh3 SplitMesh;
        public HashSet<int> PositiveSideTriangles;
        public HashSet<int> NegativeSideTriangles;

        /// <summary>
        /// The cut cross-section's edges, as pairs of split-mesh vertex ids: one segment per
        /// triangle that straddles the plane, joining the two points where that triangle meets it.
        /// This is the connectivity a loop walk needs. The cut vertices alone carry none — every
        /// intersection point on its own is just a point in a plane, and ordering points by angle
        /// can only ever describe one loop.
        /// </summary>
        public List<(int A, int B)> CutSegments;
    }

    private SplitMeshResult SplitCutTriangles(DMesh3 mesh, Vector3d planePoint, Vector3d planeNormal, Dictionary<int, TriangleClassification> classification, List<CutEdge> cutEdges)
    {
        var splitMesh = new DMesh3();
        var vertexMap = new Dictionary<int, int>();
        var positiveSideTriangles = new HashSet<int>();
        var negativeSideTriangles = new HashSet<int>();
        var cutSegments = new List<(int A, int B)>();

        // Create intersection vertices in split mesh
        var intersectionVertices = new Dictionary<(int, int), int>();
        foreach (var edge in cutEdges)
        {
            var key = (Math.Min(edge.VertexAId, edge.VertexBId), Math.Max(edge.VertexAId, edge.VertexBId));

            // An edge shared by two cut triangles can be listed once per triangle; appending
            // unconditionally would leave an orphan vertex behind each time the key is overwritten.
            if (!intersectionVertices.TryGetValue(key, out int intersectionVertexId))
            {
                intersectionVertexId = splitMesh.AppendVertex(edge.IntersectionPoint);
                intersectionVertices[key] = intersectionVertexId;
            }
        }

        // Copy vertices and triangles, splitting mixed triangles
        foreach (int vid in mesh.VertexIndices())
        {
            vertexMap[vid] = splitMesh.AppendVertex(mesh.GetVertex(vid));
        }

        foreach (int tid in mesh.TriangleIndices())
        {
            TriangleClassification classif = classification[tid];
            if (classif == TriangleClassification.OnPlane)
            {
                continue; // Skip triangles on the plane
            }

            Index3i tri = mesh.GetTriangle(tid);
            if (classif == TriangleClassification.Positive)
            {
                int newTid = splitMesh.AppendTriangle(vertexMap[tri.a], vertexMap[tri.b], vertexMap[tri.c]);
                if (newTid >= 0)
                {
                    positiveSideTriangles.Add(newTid);
                }
            }
            else if (classif == TriangleClassification.Negative)
            {
                int newTid = splitMesh.AppendTriangle(vertexMap[tri.a], vertexMap[tri.b], vertexMap[tri.c]);
                if (newTid >= 0)
                {
                    negativeSideTriangles.Add(newTid);
                }
            }
            else // Mixed
            {
                SplitMixedTriangle(splitMesh, mesh, tri, planePoint, planeNormal, vertexMap, intersectionVertices, positiveSideTriangles, negativeSideTriangles, cutSegments);
            }
        }

        return new SplitMeshResult
        {
            SplitMesh = splitMesh,
            PositiveSideTriangles = positiveSideTriangles,
            NegativeSideTriangles = negativeSideTriangles,
            CutSegments = cutSegments
        };
    }

    private void SplitMixedTriangle(DMesh3 splitMesh, DMesh3 originalMesh, Index3i originalTri, Vector3d planePoint, Vector3d planeNormal,
        Dictionary<int, int> vertexMap, Dictionary<(int, int), int> intersectionVertices, HashSet<int> positiveSide, HashSet<int> negativeSide,
        List<(int A, int B)> cutSegments)
    {
        var vertices = new[] { originalTri.a, originalTri.b, originalTri.c };
        var distances = new double[3];
        var signs = new int[3]; // 1 for positive, -1 for negative, 0 for on plane

        for (int i = 0; i < 3; i++)
        {
            distances[i] = SignedDistance(originalMesh.GetVertex(vertices[i]), planePoint, planeNormal);
            if (Math.Abs(distances[i]) < 1e-10)
            {
                signs[i] = 0;
            }
            else if (distances[i] > 0)
            {
                signs[i] = 1;
            }
            else
            {
                signs[i] = -1;
            }
        }

        // Find intersection points with the plane
        var intersectionPoints = new int?[3];
        for (int i = 0; i < 3; i++)
        {
            int j = (i + 1) % 3;
            if (signs[i] * signs[j] < 0) // Different sides
            {
                // One vertex per cut *edge*, shared by both triangles either side of it. Appending
                // a fresh vertex per triangle instead splits the surface along the whole cut: the
                // halves come back as a shell per face, and a later "remove small shells" pass
                // reads those fragments as debris and deletes real geometry.
                (int, int) edgeKey = (Math.Min(vertices[i], vertices[j]), Math.Max(vertices[i], vertices[j]));
                if (!intersectionVertices.TryGetValue(edgeKey, out int intersectionVertexId))
                {
                    double t = distances[i] / (distances[i] - distances[j]);
                    Vector3d p0 = originalMesh.GetVertex(vertices[i]);
                    Vector3d p1 = originalMesh.GetVertex(vertices[j]);
                    intersectionVertexId = splitMesh.AppendVertex(p0 + t * (p1 - p0));
                    intersectionVertices[edgeKey] = intersectionVertexId;
                }

                intersectionPoints[i] = intersectionVertexId;
            }
        }

        // Emit sub-triangles.
        //
        // A triangle straddling the plane leaves one vertex alone on its side and the opposite
        // edge's worth of quad on the other, so it re-triangulates into one corner triangle plus
        // two for the quad. Emitting only where both of a vertex's edges are cut fires solely for
        // that lone corner, which dropped the quad half of every straddling triangle: a strip of
        // surface went missing along the entire cut, leaving each half in fragments that a later
        // "remove small shells" pass then deleted as if it were debris.
        void Emit(HashSet<int> side, int v0, int v1, int v2)
        {
            int tid = splitMesh.AppendTriangle(v0, v1, v2);
            if (tid >= 0)
            {
                side.Add(tid);
            }
        }

        int onPlane = Array.IndexOf(signs, 0);
        if (onPlane >= 0)
        {
            // One vertex sits on the plane, so only the opposite edge crosses and the triangle
            // splits in two through that vertex.
            int after = (onPlane + 1) % 3;
            int before = (onPlane + 2) % 3;
            if (intersectionPoints[after] is not int crossing)
            {
                return;
            }

            int pivot = vertexMap[vertices[onPlane]];
            int afterId = vertexMap[vertices[after]];
            int beforeId = vertexMap[vertices[before]];

            Emit(signs[after] > 0 ? positiveSide : negativeSide, pivot, afterId, crossing);
            Emit(signs[after] > 0 ? negativeSide : positiveSide, pivot, crossing, beforeId);

            // This triangle meets the plane along the segment from the on-plane vertex to the
            // crossing on the opposite edge.
            cutSegments.Add((pivot, crossing));
            return;
        }

        // No vertex on the plane: exactly one is alone on its side of it.
        int lone = -1;
        for (int i = 0; i < 3; i++)
        {
            if (signs[i] != signs[(i + 1) % 3] && signs[i] != signs[(i + 2) % 3])
            {
                lone = i;
                break;
            }
        }

        if (lone < 0)
        {
            return;
        }

        int nextIndex = (lone + 1) % 3;
        int prevIndex = (lone + 2) % 3;

        // The crossings bracketing the lone vertex: on the edge leaving it, and the one entering.
        if (intersectionPoints[lone] is not int leaving || intersectionPoints[prevIndex] is not int entering)
        {
            return;
        }

        int loneId = vertexMap[vertices[lone]];
        int nextId = vertexMap[vertices[nextIndex]];
        int prevId = vertexMap[vertices[prevIndex]];

        HashSet<int> loneSide = signs[lone] > 0 ? positiveSide : negativeSide;
        HashSet<int> farSide = signs[lone] > 0 ? negativeSide : positiveSide;

        // Winding follows the original triangle in both halves.
        Emit(loneSide, loneId, leaving, entering);
        Emit(farSide, leaving, nextId, prevId);
        Emit(farSide, leaving, prevId, entering);

        // This triangle meets the plane along the segment between its two crossings — the edge of
        // the cut cross-section that this triangle contributes.
        cutSegments.Add((leaving, entering));
    }

    /// <summary>
    /// Appends <paramref name="capTriangles"/> — expressed in the split mesh's vertex ids — to
    /// <paramref name="targetMesh"/>, translating each vertex into that mesh's own ids and
    /// appending any the side did not already own, so the cap closes against the real boundary
    /// rather than an arbitrary index.
    /// </summary>
    /// <param name="reverseWinding">
    /// True for the half whose solid sits on the positive side of the plane: the triangulation is
    /// wound to face along the plane normal, which points into that half rather than out of it.
    /// </param>
    private static int AppendCap(
        DMesh3 targetMesh,
        List<Index3i> capTriangles,
        DMesh3 splitMesh,
        Func<int, int?> lookup,
        Action<int, int> remember,
        bool reverseWinding)
    {
        int added = 0;
        foreach (Index3i tri in capTriangles)
        {
            int a = Translate(tri.a);
            int b = Translate(tri.b);
            int c = Translate(tri.c);

            int tid = reverseWinding
                ? targetMesh.AppendTriangle(a, c, b)
                : targetMesh.AppendTriangle(a, b, c);

            if (tid >= 0)
            {
                added++;
            }
        }

        return added;

        int Translate(int vid)
        {
            int? mapped = lookup(vid);
            if (mapped is not null)
            {
                return mapped.Value;
            }

            int appended = targetMesh.AppendVertex(splitMesh.GetVertex(vid));
            remember(vid, appended);
            return appended;
        }
    }
}

/// <summary>Mode for plane cut operation.</summary>
public enum CutMode
{
    /// <summary>Keep only the positive side (discard negative), with flat cap.</summary>
    Keep,

    /// <summary>Keep only the negative side (discard positive), with flat cap.</summary>
    Discard,

    /// <summary>Split into separate positive and negative meshes, each with flat cap.</summary>
    Split,
}
