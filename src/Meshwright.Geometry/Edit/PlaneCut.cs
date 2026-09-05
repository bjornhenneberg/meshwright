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
    bool MeshWasModified);

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
    /// <returns>
    /// PlaneCutResult with PositiveSideMesh (always present), NegativeSideMesh (only for Split mode),
    /// and cap triangle count. If the plane passes through no geometry, PositiveSideMesh is a copy
    /// of the input and MeshWasModified is false.
    /// </returns>
    public PlaneCutResult Cut(DMesh3 mesh, Vector3d planePoint, Vector3d planeNormal, CutMode mode, HoleFillMode capMode)
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
        List<List<int>> capLoops = CutCrossSection.ExtractLoops(splitMesh.CutSegments);
        List<Index3i> capTriangles = capLoops.Count == 0
            ? []
            : CutCrossSection.Triangulate(capLoops, splitMesh.SplitMesh, basis, flatFan: capMode == HoleFillMode.Flat);
        int capTrianglesAdded = 0;

        // Build the result mesh(es)
        DMesh3 positiveSide = new DMesh3();
        // Built for Discard as well as Split: Discard's whole job is to return the negative side,
        // so skipping it there left the mode with nothing to return but the positive side —
        // making Discard behave identically to Keep.
        DMesh3? negativeSide = mode is CutMode.Split or CutMode.Discard ? new DMesh3() : null;

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
        if (capTriangles.Count > 0)
        {
            capTrianglesAdded = AppendCap(
                positiveSide,
                capTriangles,
                splitMesh.SplitMesh,
                vid => vertexMap.TryGetValue(vid, out (int posMeshId, int? negMeshId) ids) ? ids.posMeshId : null,
                (vid, mappedId) => vertexMap[vid] = (mappedId, vertexMap.TryGetValue(vid, out (int posMeshId, int? negMeshId) e) ? e.negMeshId : null),
                reverseWinding: true);
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
            if (capTriangles.Count > 0)
            {
                capTrianglesAdded += AppendCap(
                    negativeSide,
                    capTriangles,
                    splitMesh.SplitMesh,
                    vid => negVertexMap.TryGetValue(vid, out int id) ? id : null,
                    (vid, mappedId) => negVertexMap[vid] = mappedId,
                    reverseWinding: false);
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
            MeshWasModified: true);
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
