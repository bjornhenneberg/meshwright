using g3;

namespace Meshwright.Geometry.Repair;

/// <summary>Triangulation strategy for <see cref="HoleFillRepair"/>.</summary>
public enum HoleFillMode
{
    /// <summary>Fan every hole from its centroid. Cheap, but can overlap or look faceted on large or non-convex holes.</summary>
    Flat,

    /// <summary>Fit a best-fit plane and ear-clip triangulate in it. Correct for non-convex holes; adds no new vertices.</summary>
    Planar,

    /// <summary>
    /// Planar fill, refined with extra interior vertices and relaxed so the patch continues the
    /// mean curvature of the surrounding surface across the hole, instead of sheeting it flat.
    /// </summary>
    Smooth,
}

/// <summary>Outcome of <see cref="HoleFillRepair.Fill"/>: how many holes were closed and how many triangles that took.</summary>
public sealed record HoleFillResult(int HolesFilled, int TrianglesAdded);

/// <summary>
/// Closes open boundary loops (holes) in a mesh by triangulating them (SPECIFICATION.md §5.1).
/// The repair counterpart to <see cref="Diagnostics.BoundaryHoleDetector"/>: that finds the
/// loops via <see cref="MeshBoundaryLoops"/>, this fills them the same way.
/// </summary>
public static class HoleFillRepair
{
    /// <summary>Finds every boundary loop in <paramref name="mesh"/> and fills each one in place.</summary>
    public static HoleFillResult Fill(DMesh3 mesh, HoleFillMode mode)
    {
        var boundaryLoops = new MeshBoundaryLoops(mesh);
        int trianglesAdded = 0;

        foreach (EdgeLoop loop in boundaryLoops.Loops)
        {
            trianglesAdded += mode switch
            {
                HoleFillMode.Flat => FillFlat(mesh, loop),
                HoleFillMode.Smooth => FillSmooth(mesh, loop),
                _ => FillPlanar(mesh, loop),
            };
        }

        return new HoleFillResult(boundaryLoops.Loops.Count, trianglesAdded);
    }

    /// <summary>
    /// Appends a mesh triangle from three vertices given in "forward" order — i.e. the order in
    /// which they walk around the hole boundary (or a fan/split derived from it). A boundary
    /// edge's two vertices are visited by <see cref="EdgeLoop.Vertices"/> in the same direction as
    /// they appear in the single existing triangle already attached to that edge (see
    /// <see cref="DMesh3.GetOrientedBoundaryEdgeV"/>, which is what <see cref="MeshBoundaryLoops"/>
    /// walks). For the mesh to stay consistently oriented, every edge must be traversed in
    /// *opposite* directions by its two adjacent triangles — so any fill triangle built from
    /// forward-ordered vertices must have its winding reversed before being added. Swapping the
    /// last two vertices does that (any single transposition reverses a triangle's winding).
    /// </summary>
    private static int AppendFillTriangle(DMesh3 mesh, int forward0, int forward1, int forward2)
    {
        int tid = mesh.AppendTriangle(forward0, forward2, forward1);
        return tid >= 0 ? 1 : 0;
    }

    private static int FillFlat(DMesh3 mesh, EdgeLoop loop)
    {
        int n = loop.Vertices.Length;
        if (n < 3)
        {
            return 0;
        }

        Vector3d centroid = LoopCentroid(mesh, loop.Vertices);
        int centroidId = mesh.AppendVertex(centroid);

        int added = 0;
        for (int i = 0; i < n; i++)
        {
            int a = loop.Vertices[i];
            int b = loop.Vertices[(i + 1) % n];
            added += AppendFillTriangle(mesh, a, b, centroidId);
        }
        return added;
    }

    private static int FillPlanar(DMesh3 mesh, EdgeLoop loop)
    {
        int n = loop.Vertices.Length;
        if (n < 3)
        {
            return 0;
        }

        Vector2d[] points2d = ProjectLoop(mesh, loop.Vertices, out _, out _, out _);
        List<(int A, int B, int C)> ears = EarClipTriangulate(points2d);

        int added = 0;
        foreach ((int a, int b, int c) in ears)
        {
            added += AppendFillTriangle(mesh, loop.Vertices[a], loop.Vertices[b], loop.Vertices[c]);
        }
        return added;
    }

    /// <summary>
    /// Planar fill, refined with genuinely new interior vertices, then bulged with an analytic
    /// spherical-cap correction so the patch continues the surrounding surface's curvature instead
    /// of sheeting the hole flat.
    ///
    /// The key input is the discrete mean-curvature normal at every boundary loop vertex — how far
    /// it sits from the average of its <em>real, pre-fill</em> one-ring neighbours, normalized by
    /// the local edge-length squared so it stays meaningful once the interior is refined to a finer
    /// edge length (<see cref="ComputeBoundaryCurvature"/>). Averaging that over the whole loop
    /// gives a single effective curvature for the patch, hence an effective radius
    /// <c>R = 1 / (2 * |averageCurvature|)</c> (the discrete-Laplacian-to-mean-curvature relation
    /// for a uniformly sampled sphere), and a bulge direction. From there the fill is exact
    /// spherical-cap geometry, not an iterative approximation: every interior vertex is displaced,
    /// along that direction, by the sagitta difference between its own and the loop's own distance
    /// from the patch's center axis — <c>sqrt(R^2 - rho^2) - sqrt(R^2 - r1^2)</c>, the same formula
    /// that gives a spherical cap's height above the disk spanning its rim. That is zero at the rim
    /// (matching the boundary that's already fixed) and largest at the patch's center, tapering
    /// smoothly regardless of how unevenly <see cref="RefinePatch"/> happened to refine the
    /// triangulation — unlike an iterative per-vertex relaxation, which was tried first and had two
    /// failure modes: folding the curvature correction into the same fixed-point loop as Laplacian
    /// averaging creates positive feedback (each ring's bulge stretches its neighbours' edge
    /// length, which enlarges the next ring's correction in turn, overshooting the sphere by 10%+
    /// in testing), and tapering by mesh-graph hop count from the boundary instead of Euclidean
    /// distance produced a visibly lumpy patch, since ear-clipping plus longest-edge refinement
    /// gives very uneven graph connectivity even where the geometry is smooth.
    ///
    /// Flat, planar boundary loops (curvature ~0, as after <see cref="Meshwright.Geometry.Edit.PlaneCut"/>
    /// or a hole in a flat face) skip the bulge entirely rather than divide by a near-zero
    /// curvature, leaving the pure Laplacian membrane below — which for a loop already lying in a
    /// plane settles to that same plane, exactly matching what <see cref="FillPlanar"/> gives.
    /// </summary>
    private static int FillSmooth(DMesh3 mesh, EdgeLoop loop)
    {
        int n = loop.Vertices.Length;
        if (n < 3)
        {
            return 0;
        }

        // Capture curvature from the *real* mesh before any fill triangle changes vertices'
        // one-rings.
        Dictionary<int, Vector3d> boundaryCurvature = ComputeBoundaryCurvature(mesh, loop.Vertices);
        Vector3d loopCentroid = LoopCentroid(mesh, loop.Vertices);
        double capRadius = AverageDistanceToCentroid(mesh, loop.Vertices, loopCentroid);

        Vector3d averageCurvature = Vector3d.Zero;
        foreach (Vector3d c in boundaryCurvature.Values)
        {
            averageCurvature += c;
        }
        averageCurvature /= Math.Max(1, boundaryCurvature.Count);

        double curvatureMagnitude = averageCurvature.Length;
        bool hasCurvature = curvatureMagnitude > 1e-9 && capRadius > 1e-9;
        double effectiveRadius = hasCurvature ? 1.0 / (2.0 * curvatureMagnitude) : 0.0;
        Vector3d bulgeDirection = hasCurvature ? averageCurvature / curvatureMagnitude : Vector3d.Zero;
        // A cap wider than its own effective radius (a very shallow curvature estimate against a
        // large hole) would make the sagitta formula's sqrt argument go negative; treat that the
        // same as "no reliable curvature estimate" rather than reconstructing spurious geometry.
        if (hasCurvature && effectiveRadius <= capRadius)
        {
            hasCurvature = false;
        }

        Vector2d[] points2d = ProjectLoop(mesh, loop.Vertices, out _, out _, out _);
        List<(int A, int B, int C)> ears = EarClipTriangulate(points2d);

        var patchTriangles = new HashSet<int>();
        foreach ((int a, int b, int c) in ears)
        {
            int tid = mesh.AppendTriangle(loop.Vertices[a], loop.Vertices[c], loop.Vertices[b]);
            if (tid >= 0)
            {
                patchTriangles.Add(tid);
            }
        }

        double targetEdgeLength = AverageLoopEdgeLength(mesh, loop.Vertices);
        List<int> interiorVertices = RefinePatch(mesh, patchTriangles, targetEdgeLength);

        const int membraneIterations = 60;
        for (int iter = 0; iter < membraneIterations; iter++)
        {
            foreach (int v in interiorVertices)
            {
                Vector3d sum = Vector3d.Zero;
                int count = 0;
                foreach (int nbr in mesh.VtxVerticesItr(v))
                {
                    sum += mesh.GetVertex(nbr);
                    count++;
                }
                if (count > 0)
                {
                    mesh.SetVertex(v, sum / count);
                }
            }
        }

        if (hasCurvature)
        {
            double rimSag = Math.Sqrt(Math.Max(0.0, (effectiveRadius * effectiveRadius) - (capRadius * capRadius)));
            foreach (int v in interiorVertices)
            {
                double rho = mesh.GetVertex(v).Distance(loopCentroid);
                rho = Math.Min(rho, capRadius); // interior vertices can stray slightly past capRadius after relaxation
                double sag = Math.Sqrt(Math.Max(0.0, (effectiveRadius * effectiveRadius) - (rho * rho))) - rimSag;
                mesh.SetVertex(v, mesh.GetVertex(v) + (bulgeDirection * sag));
            }
        }

        return patchTriangles.Count;
    }

    private static double AverageDistanceToCentroid(DMesh3 mesh, int[] loopVertices, Vector3d centroid)
    {
        double sum = 0;
        foreach (int vid in loopVertices)
        {
            sum += mesh.GetVertex(vid).Distance(centroid);
        }
        return loopVertices.Length > 0 ? sum / loopVertices.Length : 0;
    }

    /// <summary>
    /// A scale-free estimate of the surrounding surface's mean-curvature normal near each boundary
    /// loop vertex, divided by the local edge-length squared (a discrete Laplacian's magnitude
    /// scales with h^2 for a fixed curvature, so dividing it out gives a quantity that stays
    /// meaningful at any mesh density — see <see cref="FillSmooth"/>). Must run before any fill
    /// triangle is added, since afterward a boundary vertex's one-ring would also include patch
    /// neighbours and this would stop measuring the real surface.
    ///
    /// Deliberately measured one ring <em>in</em> from the loop, not at the loop vertices
    /// themselves: a boundary vertex's one-ring is missing every neighbour on the hole side by
    /// definition, so "how far it sits from the average of its neighbours" is systematically
    /// inflated by that missing half — measured directly, it estimated a sphere test case's radius
    /// at roughly a fifth of its true value. A real interior neighbour just inside the loop still
    /// has a complete, unbiased one-ring.
    /// </summary>
    private static Dictionary<int, Vector3d> ComputeBoundaryCurvature(DMesh3 mesh, int[] loopVertices)
    {
        var loopSet = new HashSet<int>(loopVertices);
        var curvature = new Dictionary<int, Vector3d>(loopVertices.Length);
        foreach (int vid in loopVertices)
        {
            int? collarVid = null;
            foreach (int nbr in mesh.VtxVerticesItr(vid))
            {
                if (!loopSet.Contains(nbr))
                {
                    collarVid = nbr;
                    break;
                }
            }

            curvature[vid] = collarVid.HasValue ? VertexCurvature(mesh, collarVid.Value) : Vector3d.Zero;
        }
        return curvature;
    }

    /// <summary>Discrete mean-curvature normal at <paramref name="vid"/>, normalized by local edge-length squared (see <see cref="ComputeBoundaryCurvature"/>).</summary>
    private static Vector3d VertexCurvature(DMesh3 mesh, int vid)
    {
        Vector3d vPos = mesh.GetVertex(vid);
        Vector3d sum = Vector3d.Zero;
        double edgeLenSum = 0;
        int count = 0;
        foreach (int nbr in mesh.VtxVerticesItr(vid))
        {
            Vector3d nbrPos = mesh.GetVertex(nbr);
            sum += nbrPos;
            edgeLenSum += vPos.Distance(nbrPos);
            count++;
        }

        if (count == 0)
        {
            return Vector3d.Zero;
        }

        Vector3d average = sum / count;
        double localEdgeLength = edgeLenSum / count;
        Vector3d delta = vPos - average;
        return localEdgeLength > 1e-12 ? delta / (localEdgeLength * localEdgeLength) : Vector3d.Zero;
    }

    private static double AverageLoopEdgeLength(DMesh3 mesh, int[] loopVertices)
    {
        int n = loopVertices.Length;
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            sum += mesh.GetVertex(loopVertices[i]).Distance(mesh.GetVertex(loopVertices[(i + 1) % n]));
        }
        return n > 0 ? sum / n : 0;
    }

    /// <summary>
    /// Adds interior degrees of freedom to an ear-clipped patch by repeatedly splitting its
    /// longest strictly-interior edge (both adjacent triangles in <paramref name="patchTriangles"/>)
    /// until edges are close to <paramref name="targetEdgeLength"/>. Never touches an edge along
    /// the original hole boundary — those already have one adjacent triangle outside the patch,
    /// and must stay exactly as <see cref="MeshBoundaryLoops"/> found them.
    /// </summary>
    private static List<int> RefinePatch(DMesh3 mesh, HashSet<int> patchTriangles, double targetEdgeLength)
    {
        var interiorVertices = new List<int>();
        if (targetEdgeLength <= 0)
        {
            return interiorVertices;
        }

        double splitThreshold = targetEdgeLength * 1.5;
        int maxNewVertices = Math.Max(8, patchTriangles.Count * 4);
        int guard = 0;
        int maxGuard = maxNewVertices * 4 + 16;

        while (interiorVertices.Count < maxNewVertices && guard++ < maxGuard)
        {
            int bestEdge = FindLongestInteriorEdge(mesh, patchTriangles, out double bestLength);
            if (bestEdge < 0 || bestLength <= splitThreshold)
            {
                break;
            }

            if (mesh.SplitEdge(bestEdge, out DMesh3.EdgeSplitInfo split) != MeshResult.Ok)
            {
                break;
            }

            interiorVertices.Add(split.vNew);
            patchTriangles.Add(split.eNewT2);
            if (split.eNewT3 != DMesh3.InvalidID)
            {
                patchTriangles.Add(split.eNewT3);
            }
        }

        return interiorVertices;
    }

    private static int FindLongestInteriorEdge(DMesh3 mesh, HashSet<int> patchTriangles, out double bestLength)
    {
        int bestEdge = -1;
        bestLength = 0;
        var seen = new HashSet<int>();

        foreach (int tid in patchTriangles)
        {
            Index3i triEdges = mesh.GetTriEdges(tid);
            for (int k = 0; k < 3; k++)
            {
                int eid = triEdges[k];
                if (!seen.Add(eid))
                {
                    continue;
                }

                Index2i edgeTris = mesh.GetEdgeT(eid);
                bool bothInPatch = edgeTris.a != DMesh3.InvalidID && edgeTris.b != DMesh3.InvalidID
                    && patchTriangles.Contains(edgeTris.a) && patchTriangles.Contains(edgeTris.b);
                if (!bothInPatch)
                {
                    // Either a genuine mesh boundary edge, or an edge shared with a triangle
                    // outside the patch (i.e. the hole's own outline) — never split it.
                    continue;
                }

                // A diagonal directly between two boundary loop vertices is still safe to split —
                // the bothInPatch check above already guarantees it only touches patch triangles,
                // so it cannot be the hole's own outline (each outline edge has exactly one
                // adjacent triangle outside the patch by construction).
                Index2i ev = mesh.GetEdgeV(eid);
                double length = mesh.GetVertex(ev.a).Distance(mesh.GetVertex(ev.b));
                if (length > bestLength)
                {
                    bestLength = length;
                    bestEdge = eid;
                }
            }
        }

        return bestEdge;
    }

    private static Vector3d LoopCentroid(DMesh3 mesh, int[] loopVertices)
    {
        Vector3d sum = Vector3d.Zero;
        foreach (int vid in loopVertices)
        {
            sum += mesh.GetVertex(vid);
        }
        return sum / loopVertices.Length;
    }

    /// <summary>
    /// Best-fit normal for a (possibly non-planar) vertex loop, via Newell's method: robust to
    /// small non-planarity and, importantly, chirality-correct — it agrees with the winding
    /// implied by <paramref name="loopVertices"/>'s own order, which is what lets
    /// <see cref="ProjectLoop"/> hand ear-clipping a 2D polygon in a known orientation.
    /// </summary>
    private static Vector3d NewellNormal(DMesh3 mesh, int[] loopVertices)
    {
        Vector3d normal = Vector3d.Zero;
        int n = loopVertices.Length;
        for (int i = 0; i < n; i++)
        {
            Vector3d p = mesh.GetVertex(loopVertices[i]);
            Vector3d q = mesh.GetVertex(loopVertices[(i + 1) % n]);
            normal.x += (p.y - q.y) * (p.z + q.z);
            normal.y += (p.z - q.z) * (p.x + q.x);
            normal.z += (p.x - q.x) * (p.y + q.y);
        }
        return normal.Normalized;
    }

    /// <summary>
    /// Projects the loop into a 2D (right, up) basis of the best-fit plane, chosen so that
    /// right x up == the Newell normal — which makes the loop's own vertex order appear
    /// counter-clockwise in the returned 2D points, exactly what <see cref="EarClipTriangulate"/>
    /// assumes of its input.
    /// </summary>
    private static Vector2d[] ProjectLoop(DMesh3 mesh, int[] loopVertices, out Vector3d origin, out Vector3d right, out Vector3d up)
    {
        Vector3d normal = NewellNormal(mesh, loopVertices);
        Vector3d helper = Math.Abs(normal.x) < 0.9 ? Vector3d.AxisX : Vector3d.AxisY;
        right = Vector3d.Cross(helper, normal).Normalized;
        up = Vector3d.Cross(normal, right);
        origin = mesh.GetVertex(loopVertices[0]);

        var points2d = new Vector2d[loopVertices.Length];
        for (int i = 0; i < loopVertices.Length; i++)
        {
            Vector3d local = mesh.GetVertex(loopVertices[i]) - origin;
            points2d[i] = new Vector2d(local.Dot(right), local.Dot(up));
        }
        return points2d;
    }

    private static double Cross(Vector2d a, Vector2d b, Vector2d c) => (b - a).Cross(c - a);

    /// <summary>Signed-area / same-side test: true iff <paramref name="p"/> lies inside or on triangle (a, b, c).</summary>
    private static bool PointInTriangle(Vector2d p, Vector2d a, Vector2d b, Vector2d c)
    {
        double d1 = Cross(a, b, p);
        double d2 = Cross(b, c, p);
        double d3 = Cross(c, a, p);
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    /// <summary>
    /// Standard O(n^2) ear clipping. Correctly handles non-convex polygons, unlike a naive fan;
    /// hole boundaries are small (a few dozen edges at most) so the quadratic cost doesn't matter.
    /// Assumes <paramref name="polygon"/> winds counter-clockwise (see <see cref="ProjectLoop"/>);
    /// returns triangles as index triples into <paramref name="polygon"/>, in that same forward
    /// winding (callers must reverse via <see cref="AppendFillTriangle"/> before adding to the mesh).
    /// </summary>
    private static List<(int A, int B, int C)> EarClipTriangulate(IReadOnlyList<Vector2d> polygon)
    {
        int n = polygon.Count;
        var result = new List<(int, int, int)>(Math.Max(0, n - 2));
        if (n < 3)
        {
            return result;
        }

        var indices = new List<int>(n);
        for (int i = 0; i < n; i++)
        {
            indices.Add(i);
        }

        int guard = 0;
        int maxGuard = (n * n) + 8;
        while (indices.Count > 3 && guard++ < maxGuard)
        {
            bool clipped = false;
            int m = indices.Count;
            for (int k = 0; k < m; k++)
            {
                int prev = indices[(k - 1 + m) % m];
                int cur = indices[k];
                int next = indices[(k + 1) % m];

                if (IsEar(polygon, indices, prev, cur, next))
                {
                    result.Add((prev, cur, next));
                    indices.RemoveAt(k);
                    clipped = true;
                    break;
                }
            }

            if (!clipped)
            {
                // Numerically degenerate polygon (near-zero area, colinear run, ...) defeated the
                // convexity/containment tests before every vertex was clipped. Fan the remainder
                // from the first survivor instead of looping forever; this can't happen for a
                // simple, reasonably-shaped hole boundary.
                break;
            }
        }

        for (int k = 1; k + 1 < indices.Count; k++)
        {
            result.Add((indices[0], indices[k], indices[k + 1]));
        }

        return result;
    }

    private static bool IsEar(IReadOnlyList<Vector2d> polygon, List<int> indices, int prev, int cur, int next)
    {
        Vector2d a = polygon[prev];
        Vector2d b = polygon[cur];
        Vector2d c = polygon[next];

        if (Cross(a, b, c) <= 0)
        {
            return false; // reflex or degenerate corner: not a valid ear for a CCW polygon
        }

        foreach (int idx in indices)
        {
            if (idx == prev || idx == cur || idx == next)
            {
                continue;
            }
            if (PointInTriangle(polygon[idx], a, b, c))
            {
                return false; // another loop vertex sits inside the candidate ear
            }
        }
        return true;
    }

    /// <summary>
    /// Which ear-clip triangle contains <paramref name="point"/>. Always returns a valid index —
    /// falling back to the best-scoring (least-outside) triangle if none contains it exactly,
    /// which can happen at floating-point edges or for a centroid that lands just outside a very
    /// non-convex loop.
    /// </summary>
    private static int FindContainingTriangle(Vector2d[] points2d, List<(int A, int B, int C)> triangles, Vector2d point)
    {
        int best = 0;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < triangles.Count; i++)
        {
            (int a, int b, int c) = triangles[i];
            double d1 = Cross(points2d[a], points2d[b], point);
            double d2 = Cross(points2d[b], points2d[c], point);
            double d3 = Cross(points2d[c], points2d[a], point);
            double score = Math.Min(d1, Math.Min(d2, d3));
            if (score >= 0)
            {
                return i;
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }
}
