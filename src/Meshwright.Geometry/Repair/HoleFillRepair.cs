using g3;

namespace Meshwright.Geometry.Repair;

/// <summary>Triangulation strategy for <see cref="HoleFillRepair"/>.</summary>
public enum HoleFillMode
{
    /// <summary>Fan every hole from its centroid. Cheap, but can overlap or look faceted on large or non-convex holes.</summary>
    Flat,

    /// <summary>Fit a best-fit plane and ear-clip triangulate in it. Correct for non-convex holes; adds no new vertices.</summary>
    Planar,

    /// <summary>Planar fill, plus one smoothed interior vertex for a less faceted cap.</summary>
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
    /// Planar fill, plus one genuinely new interior vertex: the loop centroid, grafted into
    /// whichever ear-clip triangle contains it (splitting that one triangle into three) and then
    /// relaxed onto the average of its neighbours via <see cref="DMesh3.VtxOneRingCentroid"/>.
    /// Grafting into an existing ear-clip triangle (rather than fanning from the centroid
    /// directly, as <see cref="FillFlat"/> does) keeps this correct on non-convex loops, since
    /// ear-clipping has already proven that triangle lies inside the polygon. Because the new
    /// vertex's only neighbours are that triangle's three (fixed) boundary corners, Laplacian
    /// relaxation converges after one iteration; further iterations are no-ops here, but the same
    /// loop would keep doing real work if this ever grew more than one interior vertex.
    /// </summary>
    private static int FillSmooth(DMesh3 mesh, EdgeLoop loop)
    {
        int n = loop.Vertices.Length;
        if (n < 3)
        {
            return 0;
        }

        Vector2d[] points2d = ProjectLoop(mesh, loop.Vertices, out Vector3d origin, out Vector3d right, out Vector3d up);
        List<(int A, int B, int C)> ears = EarClipTriangulate(points2d);

        Vector3d centroid3d = LoopCentroid(mesh, loop.Vertices);
        Vector3d centroidLocal = centroid3d - origin;
        var centroid2d = new Vector2d(centroidLocal.Dot(right), centroidLocal.Dot(up));
        int splitIndex = FindContainingTriangle(points2d, ears, centroid2d);

        int centroidId = mesh.AppendVertex(centroid3d);

        int added = 0;
        for (int i = 0; i < ears.Count; i++)
        {
            (int a, int b, int c) = ears[i];
            if (i == splitIndex)
            {
                added += AppendFillTriangle(mesh, loop.Vertices[a], loop.Vertices[b], centroidId);
                added += AppendFillTriangle(mesh, loop.Vertices[b], loop.Vertices[c], centroidId);
                added += AppendFillTriangle(mesh, loop.Vertices[c], loop.Vertices[a], centroidId);
            }
            else
            {
                added += AppendFillTriangle(mesh, loop.Vertices[a], loop.Vertices[b], loop.Vertices[c]);
            }
        }

        for (int iter = 0; iter < 3; iter++)
        {
            Vector3d ring = Vector3d.Zero;
            mesh.VtxOneRingCentroid(centroidId, ref ring);
            mesh.SetVertex(centroidId, ring);
        }

        return added;
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
