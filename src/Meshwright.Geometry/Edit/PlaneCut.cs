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

        // Collect loop from cut edges and generate cap
        var capLoop = ExtractCapLoop(splitMesh.CutMeshEdges, planePoint, planeNormal);
        int capTrianglesAdded = 0;

        // Build the result mesh(es)
        DMesh3 positiveSide = new DMesh3();
        // Built for Discard as well as Split: Discard's whole job is to return the negative side,
        // so skipping it there left the mode with nothing to return but the positive side —
        // making Discard behave identically to Keep.
        DMesh3? negativeSide = mode is CutMode.Split or CutMode.Discard ? new DMesh3() : null;

        var vertexMap = new Dictionary<int, (int posMeshId, int? negMeshId)>();
        List<int> capVertexIndices = [];

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

        // Add cap to positive side (and negative side if in Split mode).
        // The loop comes from splitMesh, but every cap routine indexes the mesh it is filling, so
        // the loop has to be translated into that mesh's own vertex ids first. Passing splitMesh
        // ids straight through had the cap stitching together whichever unrelated vertices
        // happened to hold those indices, which is what left cut results in disconnected pieces
        // instead of one closed shell.
        if (capLoop.Count >= 3)
        {
            List<int> positiveCapLoop = TranslateLoop(
                capLoop,
                splitMesh.SplitMesh,
                positiveSide,
                vid => vertexMap.TryGetValue(vid, out (int posMeshId, int? negMeshId) ids) ? ids.posMeshId : null,
                (vid, mappedId) => vertexMap[vid] = (mappedId, vertexMap.TryGetValue(vid, out (int posMeshId, int? negMeshId) e) ? e.negMeshId : null));

            capTrianglesAdded = AddCapToMesh(positiveSide, positiveCapLoop, planeNormal, capMode);
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

            // Add cap to negative side with reversed normal
            if (capLoop.Count >= 3)
            {
                List<int> negativeCapLoop = TranslateLoop(
                    capLoop,
                    splitMesh.SplitMesh,
                    negativeSide,
                    vid => negVertexMap.TryGetValue(vid, out int id) ? id : null,
                    (vid, mappedId) => negVertexMap[vid] = mappedId);

                // The cap routines take their winding from the order of the loop, not from the
                // normal they are handed, so this half's cap has to be walked the other way round.
                // Capping both sides from the same loop leaves the negative half inside out — its
                // faces point into the solid, and its volume comes back wrong.
                negativeCapLoop.Reverse();

                capTrianglesAdded += AddCapToMesh(negativeSide, negativeCapLoop, -planeNormal, capMode);
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
        public List<CutEdge> CutMeshEdges;
    }

    private SplitMeshResult SplitCutTriangles(DMesh3 mesh, Vector3d planePoint, Vector3d planeNormal, Dictionary<int, TriangleClassification> classification, List<CutEdge> cutEdges)
    {
        var splitMesh = new DMesh3();
        var vertexMap = new Dictionary<int, int>();
        var positiveSideTriangles = new HashSet<int>();
        var negativeSideTriangles = new HashSet<int>();
        var cutMeshEdges = new List<CutEdge>();

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

            // Record edge for cap loop
            cutMeshEdges.Add(new CutEdge
            {
                VertexAId = intersectionVertexId,
                VertexBId = intersectionVertexId,
                IntersectionPoint = edge.IntersectionPoint
            });
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
                SplitMixedTriangle(splitMesh, mesh, tri, planePoint, planeNormal, vertexMap, intersectionVertices, positiveSideTriangles, negativeSideTriangles);
            }
        }

        return new SplitMeshResult
        {
            SplitMesh = splitMesh,
            PositiveSideTriangles = positiveSideTriangles,
            NegativeSideTriangles = negativeSideTriangles,
            CutMeshEdges = cutMeshEdges
        };
    }

    private void SplitMixedTriangle(DMesh3 splitMesh, DMesh3 originalMesh, Index3i originalTri, Vector3d planePoint, Vector3d planeNormal,
        Dictionary<int, int> vertexMap, Dictionary<(int, int), int> intersectionVertices, HashSet<int> positiveSide, HashSet<int> negativeSide)
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
    }

    private List<int> ExtractCapLoop(List<CutEdge> cutEdges, Vector3d planePoint, Vector3d planeNormal)
    {
        // Simple approach: collect all intersection vertices and order them around the plane
        var vertexSet = new HashSet<int>();
        var vertexPositions = new Dictionary<int, Vector3d>();

        foreach (var edge in cutEdges)
        {
            vertexSet.Add(edge.VertexAId);
            vertexPositions[edge.VertexAId] = edge.IntersectionPoint;
        }

        if (vertexSet.Count < 3)
        {
            return new List<int>();
        }

        // Project vertices onto plane and sort them radially
        var projectedVertices = new List<(int vid, Vector2d pos)>();
        Vector3d right = Math.Abs(planeNormal.x) < 0.9 ? Vector3d.Cross(Vector3d.AxisX, planeNormal).Normalized : Vector3d.Cross(Vector3d.AxisY, planeNormal).Normalized;
        Vector3d up = Vector3d.Cross(planeNormal, right);

        foreach (int vid in vertexSet)
        {
            Vector3d local = vertexPositions[vid] - planePoint;
            double x = local.Dot(right);
            double y = local.Dot(up);
            projectedVertices.Add((vid, new Vector2d(x, y)));
        }

        // Sort by angle around origin
        projectedVertices.Sort((a, b) =>
        {
            double angleA = Math.Atan2(a.pos.y, a.pos.x);
            double angleB = Math.Atan2(b.pos.y, b.pos.x);
            return angleA.CompareTo(angleB);
        });

        return projectedVertices.Select(p => p.vid).ToList();
    }

    /// <summary>
    /// Re-expresses a cut loop taken from the split mesh in <paramref name="targetMesh"/>'s own
    /// vertex ids, appending any loop vertex that side didn't already own so the cap closes
    /// against the real boundary rather than an arbitrary index.
    /// </summary>
    private static List<int> TranslateLoop(
        List<int> loop,
        DMesh3 splitMesh,
        DMesh3 targetMesh,
        Func<int, int?> lookup,
        Action<int, int> remember)
    {
        var translated = new List<int>(loop.Count);
        foreach (int vid in loop)
        {
            int? mapped = lookup(vid);
            if (mapped is null)
            {
                int appended = targetMesh.AppendVertex(splitMesh.GetVertex(vid));
                remember(vid, appended);
                mapped = appended;
            }

            translated.Add(mapped.Value);
        }

        return translated;
    }

    private int AddCapToMesh(DMesh3 targetMesh, List<int> capLoopVertices, Vector3d capNormal, HoleFillMode capMode)
    {
        if (capLoopVertices.Count < 3)
        {
            return 0;
        }

        return capMode switch
        {
            HoleFillMode.Flat => CapFlat(targetMesh, capLoopVertices),
            HoleFillMode.Smooth => CapSmooth(targetMesh, capLoopVertices),
            _ => CapPlanar(targetMesh, capLoopVertices),
        };
    }

    private int CapFlat(DMesh3 mesh, List<int> loopVertices)
    {
        if (loopVertices.Count < 3)
            return 0;

        Vector3d centroid = Vector3d.Zero;
        foreach (int vid in loopVertices)
        {
            centroid += mesh.GetVertex(vid);
        }
        centroid /= loopVertices.Count;

        int centroidId = mesh.AppendVertex(centroid);
        int added = 0;

        for (int i = 0; i < loopVertices.Count; i++)
        {
            int a = loopVertices[i];
            int b = loopVertices[(i + 1) % loopVertices.Count];
            int tid = mesh.AppendTriangle(a, b, centroidId);
            if (tid >= 0)
                added++;
        }

        return added;
    }

    private int CapPlanar(DMesh3 mesh, List<int> loopVertices)
    {
        if (loopVertices.Count < 3)
            return 0;

        // Simple ear-clipping triangulation (reusing the logic from HoleFillRepair)
        var indices = new List<int>(loopVertices);
        var triangles = new List<(int a, int b, int c)>();

        int guard = 0;
        int maxGuard = (indices.Count * indices.Count) + 8;
        while (indices.Count > 3 && guard++ < maxGuard)
        {
            bool clipped = false;
            for (int k = 0; k < indices.Count; k++)
            {
                int prev = indices[(k - 1 + indices.Count) % indices.Count];
                int cur = indices[k];
                int next = indices[(k + 1) % indices.Count];

                if (IsEarSimple(mesh, indices, prev, cur, next))
                {
                    triangles.Add((prev, cur, next));
                    indices.RemoveAt(k);
                    clipped = true;
                    break;
                }
            }

            if (!clipped)
                break;
        }

        for (int k = 1; k + 1 < indices.Count; k++)
        {
            triangles.Add((indices[0], indices[k], indices[k + 1]));
        }

        int added = 0;
        foreach ((int a, int b, int c) in triangles)
        {
            int tid = mesh.AppendTriangle(a, c, b); // Reverse for correct normal
            if (tid >= 0)
                added++;
        }

        return added;
    }

    private int CapSmooth(DMesh3 mesh, List<int> loopVertices)
    {
        // Falls back to the planar cap: a genuinely smoothed cap needs an interior vertex and a
        // relaxation pass, which isn't implemented. Callers get a correct cap, just a flat one.
        return CapPlanar(mesh, loopVertices);
    }

    private bool IsEarSimple(DMesh3 mesh, List<int> indices, int prev, int cur, int next)
    {
        Vector3d a = mesh.GetVertex(indices[indices.IndexOf(prev)]);
        Vector3d b = mesh.GetVertex(indices[indices.IndexOf(cur)]);
        Vector3d c = mesh.GetVertex(indices[indices.IndexOf(next)]);

        // Simple convexity test using cross product
        Vector3d ab = b - a;
        Vector3d bc = c - b;
        Vector3d cross = Vector3d.Cross(ab, bc);

        // If cross product points in positive normal direction, it's convex
        return cross.LengthSquared > 1e-10;
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
