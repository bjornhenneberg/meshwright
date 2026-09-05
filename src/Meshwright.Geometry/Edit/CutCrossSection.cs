using g3;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// An orthonormal frame for a cutting plane: <see cref="Right"/> and <see cref="Up"/> span the
/// plane and <c>Right × Up == Normal</c>, so a triangle wound counter-clockwise in the projected
/// (u, v) coordinates faces along <see cref="Normal"/> in 3D.
/// </summary>
internal readonly record struct PlaneBasis(Vector3d Origin, Vector3d Right, Vector3d Up, Vector3d Normal)
{
    internal static PlaneBasis Create(Vector3d origin, Vector3d normal)
    {
        Vector3d n = normal.Normalized;
        Vector3d seed = Math.Abs(n.x) < 0.9 ? Vector3d.AxisX : Vector3d.AxisY;
        Vector3d right = Vector3d.Cross(seed, n).Normalized;
        Vector3d up = Vector3d.Cross(n, right);
        return new PlaneBasis(origin, right, up, n);
    }

    internal Vector2d Project(Vector3d point)
    {
        Vector3d local = point - Origin;
        return new Vector2d(local.Dot(Right), local.Dot(Up));
    }
}

/// <summary>
/// The cross-section a plane cut leaves behind: closed loops recovered from the cut segments, and
/// a triangulation of the region they bound.
///
/// <para>
/// The loops are recovered from real connectivity — each split triangle contributes the one
/// segment where it meets the plane, joining that triangle's two intersection vertices — rather
/// than by sorting the intersection points by angle around the plane origin. Angular sorting can
/// only ever describe a single, star-shaped loop: on anything with a hole through it, a cut
/// crosses several separate boundary loops at once and the sorted "loop" zig-zags between them,
/// producing a cap that self-intersects and bears no relation to the real cross-section.
/// </para>
///
/// <para>
/// Loops are then nested by parity: a loop inside an odd number of other loops is a hole in the
/// cap, one inside an even number (zero included) is filled. That is what keeps the middle of a
/// tube open, and what keeps a pillar standing inside that tube solid instead of being punched out
/// again.
/// </para>
/// </summary>
internal static class CutCrossSection
{
    /// <summary>
    /// Walks <paramref name="segments"/> — undirected pairs of intersection-vertex ids — into
    /// closed loops. Segments that cannot be closed into a loop (which needs a vertex with an odd
    /// number of incident segments, so it cannot happen on a closed manifold cut) are dropped
    /// rather than joined to something they do not touch.
    /// </summary>
    internal static List<List<int>> ExtractLoops(IEnumerable<(int A, int B)> segments)
    {
        var deduplicated = new HashSet<(int, int)>();
        var edges = new List<(int A, int B)>();
        foreach ((int a, int b) in segments)
        {
            if (a == b)
            {
                continue;
            }

            (int, int) key = a < b ? (a, b) : (b, a);
            if (deduplicated.Add(key))
            {
                edges.Add(key);
            }
        }

        var incident = new Dictionary<int, List<int>>();
        for (int e = 0; e < edges.Count; e++)
        {
            Add(incident, edges[e].A, e);
            Add(incident, edges[e].B, e);
        }

        var used = new bool[edges.Count];
        var loops = new List<List<int>>();

        for (int seed = 0; seed < edges.Count; seed++)
        {
            if (used[seed])
            {
                continue;
            }

            int start = edges[seed].A;
            int current = edges[seed].B;
            used[seed] = true;

            var loop = new List<int> { start };
            bool closed = false;

            while (true)
            {
                if (current == start)
                {
                    closed = true;
                    break;
                }

                loop.Add(current);

                int next = -1;
                foreach (int candidate in incident[current])
                {
                    if (!used[candidate])
                    {
                        next = candidate;
                        break;
                    }
                }

                if (next < 0)
                {
                    break; // Open chain: the cut is not closed here, so there is no loop to cap.
                }

                used[next] = true;
                current = edges[next].A == current ? edges[next].B : edges[next].A;
            }

            if (closed && loop.Count >= 3)
            {
                loops.Add(loop);
            }
        }

        return loops;

        static void Add(Dictionary<int, List<int>> map, int vertex, int edge)
        {
            if (!map.TryGetValue(vertex, out List<int>? list))
            {
                list = [];
                map[vertex] = list;
            }

            list.Add(edge);
        }
    }

    /// <summary>One extracted loop, projected into the cut plane.</summary>
    private sealed class Ring
    {
        internal required List<int> Vertices { get; init; }

        internal required List<Vector2d> Points { get; init; }

        /// <summary>Shoelace area, positive when the loop runs counter-clockwise in the plane basis.</summary>
        internal double SignedArea { get; set; }

        internal Vector2d Representative { get; set; }

        internal int Depth { get; set; }
    }

    /// <summary>
    /// Triangulates the region bounded by <paramref name="loops"/>, returning triangles as mesh
    /// vertex ids wound counter-clockwise in <paramref name="basis"/> — that is, facing along
    /// <see cref="PlaneBasis.Normal"/>. Callers reverse the winding for the side of the cut whose
    /// solid lies on the positive side of the plane.
    /// </summary>
    /// <param name="flatFan">
    /// When true, a loop with no holes inside it is fanned from its centroid (the cheap
    /// <c>HoleFillMode.Flat</c> cap) instead of ear-clipped; the extra centroid vertex is appended
    /// to <paramref name="mesh"/>. A loop that does contain holes is always ear-clipped, since a
    /// centroid fan cannot leave a hole open.
    /// </param>
    internal static List<Index3i> Triangulate(
        IReadOnlyList<IReadOnlyList<int>> loops,
        DMesh3 mesh,
        PlaneBasis basis,
        bool flatFan = false)
    {
        var rings = new List<Ring>();
        foreach (IReadOnlyList<int> loop in loops)
        {
            var points = new List<Vector2d>(loop.Count);
            var vertices = new List<int>(loop.Count);
            foreach (int vid in loop)
            {
                Vector2d projected = basis.Project(mesh.GetVertex(vid));
                if (points.Count > 0 && projected.Distance(points[^1]) < CoincidentTolerance)
                {
                    continue;
                }

                points.Add(projected);
                vertices.Add(vid);
            }

            while (points.Count > 2 && points[0].Distance(points[^1]) < CoincidentTolerance)
            {
                points.RemoveAt(points.Count - 1);
                vertices.RemoveAt(vertices.Count - 1);
            }

            if (points.Count < 3)
            {
                continue;
            }

            var ring = new Ring { Vertices = vertices, Points = points };
            ring.SignedArea = ShoelaceArea(points);
            if (Math.Abs(ring.SignedArea) < AreaTolerance)
            {
                continue;
            }

            ring.Representative = InteriorPoint(ring);
            rings.Add(ring);
        }

        if (rings.Count == 0)
        {
            return [];
        }

        // Parity nesting: how many other loops enclose this one decides whether it is filled or
        // punched out, and which loop it belongs to.
        foreach (Ring ring in rings)
        {
            int depth = 0;
            foreach (Ring other in rings)
            {
                if (!ReferenceEquals(ring, other) && Contains(other.Points, ring.Representative))
                {
                    depth++;
                }
            }

            ring.Depth = depth;
        }

        var triangles = new List<Index3i>();
        foreach (Ring outer in rings)
        {
            if ((outer.Depth & 1) != 0)
            {
                continue; // A hole; it is triangulated as part of whichever loop encloses it.
            }

            var holes = new List<Ring>();
            foreach (Ring candidate in rings)
            {
                if (candidate.Depth == outer.Depth + 1 && Contains(outer.Points, candidate.Representative))
                {
                    holes.Add(candidate);
                }
            }

            if (holes.Count == 0 && flatFan)
            {
                FanFromCentroid(outer, mesh, basis, triangles);
                continue;
            }

            TriangulateWithHoles(outer, holes, triangles);
        }

        return triangles;
    }

    private const double CoincidentTolerance = 1e-12;
    private const double AreaTolerance = 1e-18;

    private static double ShoelaceArea(IReadOnlyList<Vector2d> points)
    {
        double sum = 0.0;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            sum += (points[j].x * points[i].y) - (points[i].x * points[j].y);
        }

        return 0.5 * sum;
    }

    /// <summary>
    /// A point strictly inside <paramref name="ring"/>, nudged in from the middle of its longest
    /// edge. Loops of a plane cut can touch at a vertex, so testing containment with one of the
    /// loop's own vertices would be ambiguous exactly where it matters.
    /// </summary>
    private static Vector2d InteriorPoint(Ring ring)
    {
        int best = 0;
        double bestLength = -1.0;
        for (int i = 0; i < ring.Points.Count; i++)
        {
            double length = ring.Points[i].Distance(ring.Points[(i + 1) % ring.Points.Count]);
            if (length > bestLength)
            {
                bestLength = length;
                best = i;
            }
        }

        Vector2d a = ring.Points[best];
        Vector2d b = ring.Points[(best + 1) % ring.Points.Count];
        Vector2d mid = 0.5 * (a + b);
        Vector2d direction = (b - a).Normalized;
        Vector2d inward = new Vector2d(-direction.y, direction.x);
        if (ring.SignedArea < 0)
        {
            inward = -inward;
        }

        return mid + (bestLength * 1e-6 * inward);
    }

    /// <summary>Even-odd crossing test; <c>polygon</c> is treated as closed.</summary>
    private static bool Contains(IReadOnlyList<Vector2d> polygon, Vector2d point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2d pi = polygon[i];
            Vector2d pj = polygon[j];
            if ((pi.y > point.y) != (pj.y > point.y))
            {
                double x = pi.x + ((point.y - pi.y) / (pj.y - pi.y) * (pj.x - pi.x));
                if (x > point.x)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private static void FanFromCentroid(Ring ring, DMesh3 mesh, PlaneBasis basis, List<Index3i> triangles)
    {
        Vector3d centroid = Vector3d.Zero;
        foreach (int vid in ring.Vertices)
        {
            centroid += mesh.GetVertex(vid);
        }

        centroid /= ring.Vertices.Count;
        int centroidId = mesh.AppendVertex(centroid);

        bool counterClockwise = ring.SignedArea > 0;
        for (int i = 0; i < ring.Vertices.Count; i++)
        {
            int a = ring.Vertices[i];
            int b = ring.Vertices[(i + 1) % ring.Vertices.Count];
            triangles.Add(counterClockwise ? new Index3i(a, b, centroidId) : new Index3i(b, a, centroidId));
        }
    }

    /// <summary>
    /// Ear-clips <paramref name="outer"/> after splicing each hole into it along a bridge, the
    /// standard reduction of a polygon-with-holes to a single weakly-simple polygon.
    /// </summary>
    private static void TriangulateWithHoles(Ring outer, List<Ring> holes, List<Index3i> triangles)
    {
        var polygon = new List<(int Vertex, Vector2d Point)>(outer.Points.Count);
        AppendRing(polygon, outer, counterClockwise: true);

        var pending = new List<List<(int Vertex, Vector2d Point)>>();
        foreach (Ring hole in holes)
        {
            var ordered = new List<(int Vertex, Vector2d Point)>(hole.Points.Count);
            AppendRing(ordered, hole, counterClockwise: false);
            pending.Add(ordered);
        }

        pending.Sort((left, right) => MinX(left).CompareTo(MinX(right)));

        for (int h = 0; h < pending.Count; h++)
        {
            List<(int Vertex, Vector2d Point)> hole = pending[h];
            int entry = 0;
            for (int i = 1; i < hole.Count; i++)
            {
                if (hole[i].Point.x < hole[entry].Point.x
                    || (hole[i].Point.x == hole[entry].Point.x && hole[i].Point.y < hole[entry].Point.y))
                {
                    entry = i;
                }
            }

            int bridge = FindBridge(polygon, hole, entry, pending, h + 1);
            if (bridge < 0)
            {
                continue; // No visible bridge found; leave this hole unfilled rather than crossing geometry.
            }

            var merged = new List<(int Vertex, Vector2d Point)>(polygon.Count + hole.Count + 2);
            for (int i = 0; i <= bridge; i++)
            {
                merged.Add(polygon[i]);
            }

            for (int i = 0; i < hole.Count; i++)
            {
                merged.Add(hole[(entry + i) % hole.Count]);
            }

            merged.Add(hole[entry]);
            merged.Add(polygon[bridge]);
            for (int i = bridge + 1; i < polygon.Count; i++)
            {
                merged.Add(polygon[i]);
            }

            polygon = merged;
        }

        EarClip(polygon, triangles, Tolerance(polygon));

        static void AppendRing(List<(int Vertex, Vector2d Point)> target, Ring ring, bool counterClockwise)
        {
            bool forward = (ring.SignedArea > 0) == counterClockwise;
            for (int i = 0; i < ring.Points.Count; i++)
            {
                int index = forward ? i : ring.Points.Count - 1 - i;
                target.Add((ring.Vertices[index], ring.Points[index]));
            }
        }

        static double MinX(List<(int Vertex, Vector2d Point)> ring)
        {
            double min = double.MaxValue;
            foreach ((int _, Vector2d point) in ring)
            {
                min = Math.Min(min, point.x);
            }

            return min;
        }
    }

    /// <summary>
    /// The index in <paramref name="polygon"/> of the nearest vertex the hole's entry point can
    /// see: the bridge must not cross any polygon or remaining-hole edge, and must run through the
    /// polygon's interior rather than across a notch.
    /// </summary>
    private static int FindBridge(
        List<(int Vertex, Vector2d Point)> polygon,
        List<(int Vertex, Vector2d Point)> hole,
        int entry,
        List<List<(int Vertex, Vector2d Point)>> allHoles,
        int firstRemainingHole)
    {
        Vector2d target = hole[entry].Point;

        var order = new List<int>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            order.Add(i);
        }

        order.Sort((left, right) =>
            polygon[left].Point.DistanceSquared(target).CompareTo(polygon[right].Point.DistanceSquared(target)));

        foreach (int candidate in order)
        {
            Vector2d from = polygon[candidate].Point;
            if (from.Distance(target) < CoincidentTolerance)
            {
                continue;
            }

            if (CrossesAnyEdge(polygon, from, target)
                || CrossesAnyEdge(hole, from, target))
            {
                continue;
            }

            bool blocked = false;
            for (int h = firstRemainingHole; h < allHoles.Count && !blocked; h++)
            {
                blocked = CrossesAnyEdge(allHoles[h], from, target);
            }

            if (blocked)
            {
                continue;
            }

            Vector2d midpoint = 0.5 * (from + target);
            if (!Contains(Points(polygon), midpoint))
            {
                continue;
            }

            if (Contains(Points(hole), midpoint))
            {
                continue;
            }

            bool insideRemaining = false;
            for (int h = firstRemainingHole; h < allHoles.Count && !insideRemaining; h++)
            {
                insideRemaining = Contains(Points(allHoles[h]), midpoint);
            }

            if (!insideRemaining)
            {
                return candidate;
            }
        }

        return -1;

        static List<Vector2d> Points(List<(int Vertex, Vector2d Point)> ring)
        {
            var points = new List<Vector2d>(ring.Count);
            foreach ((int _, Vector2d point) in ring)
            {
                points.Add(point);
            }

            return points;
        }
    }

    /// <summary>
    /// True when segment <paramref name="a"/>-<paramref name="b"/> properly crosses an edge of
    /// <paramref name="ring"/>, or passes through one of its vertices. Shared endpoints do not
    /// count — a bridge is expected to land on a vertex.
    /// </summary>
    private static bool CrossesAnyEdge(List<(int Vertex, Vector2d Point)> ring, Vector2d a, Vector2d b)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            Vector2d c = ring[i].Point;
            Vector2d d = ring[(i + 1) % ring.Count].Point;

            if (SegmentsProperlyCross(a, b, c, d))
            {
                return true;
            }

            if (c.Distance(a) > CoincidentTolerance
                && c.Distance(b) > CoincidentTolerance
                && PointOnSegmentInterior(a, b, c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SegmentsProperlyCross(Vector2d a, Vector2d b, Vector2d c, Vector2d d)
    {
        double d1 = Cross(a, b, c);
        double d2 = Cross(a, b, d);
        double d3 = Cross(c, d, a);
        double d4 = Cross(c, d, b);

        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static bool PointOnSegmentInterior(Vector2d a, Vector2d b, Vector2d p)
    {
        Vector2d ab = b - a;
        double length = ab.Length;
        if (length < CoincidentTolerance)
        {
            return false;
        }

        double distance = Math.Abs(Cross(a, b, p)) / length;
        if (distance > length * 1e-9)
        {
            return false;
        }

        double t = (p - a).Dot(ab) / (length * length);
        return t > 1e-9 && t < 1.0 - 1e-9;
    }

    private static double Cross(Vector2d a, Vector2d b, Vector2d c) =>
        ((b.x - a.x) * (c.y - a.y)) - ((b.y - a.y) * (c.x - a.x));

    /// <summary>
    /// Ear clipping over a counter-clockwise, weakly-simple polygon. Emits triangles wound
    /// counter-clockwise.
    /// </summary>
    private static void EarClip(List<(int Vertex, Vector2d Point)> polygon, List<Index3i> triangles, double tolerance)
    {
        var indices = new List<int>(polygon.Count);
        for (int i = 0; i < polygon.Count; i++)
        {
            indices.Add(i);
        }

        int guard = (indices.Count * indices.Count) + 16;
        while (indices.Count > 3 && guard-- > 0)
        {
            int ear = FindEar(polygon, indices, tolerance);
            if (ear < 0)
            {
                // No ear survives the containment test — which means the polygon is not simple,
                // usually because two cut loops touch. Clipping the most convex corner anyway
                // keeps the cap closed and terminates, rather than abandoning the remaining area.
                ear = MostConvex(polygon, indices);
                if (ear < 0)
                {
                    break;
                }
            }

            int count = indices.Count;
            int a = indices[(ear - 1 + count) % count];
            int b = indices[ear];
            int c = indices[(ear + 1) % count];
            Emit(polygon, triangles, a, b, c, tolerance);
            indices.RemoveAt(ear);
        }

        if (indices.Count == 3)
        {
            Emit(polygon, triangles, indices[0], indices[1], indices[2], tolerance);
        }

        static void Emit(List<(int Vertex, Vector2d Point)> polygon, List<Index3i> triangles, int a, int b, int c, double tolerance)
        {
            int va = polygon[a].Vertex;
            int vb = polygon[b].Vertex;
            int vc = polygon[c].Vertex;
            if (va == vb || vb == vc || va == vc)
            {
                return; // A bridge's doubled vertex; the triangle has no area.
            }

            if (!HasRealArea(polygon[a].Point, polygon[b].Point, polygon[c].Point, tolerance))
            {
                return;
            }

            triangles.Add(new Index3i(va, vb, vc));
        }
    }

    private static int FindEar(List<(int Vertex, Vector2d Point)> polygon, List<int> indices, double tolerance)
    {
        int count = indices.Count;
        for (int k = 0; k < count; k++)
        {
            int a = indices[(k - 1 + count) % count];
            int b = indices[k];
            int c = indices[(k + 1) % count];

            Vector2d pa = polygon[a].Point;
            Vector2d pb = polygon[b].Point;
            Vector2d pc = polygon[c].Point;

            if (Cross(pa, pb, pc) <= 0 || !HasRealArea(pa, pb, pc, tolerance))
            {
                continue; // Reflex, or so nearly collinear that clipping it would emit a sliver.
            }

            // Every polygon vertex is tested, not only the ones still waiting to be clipped. A
            // vertex that has already been clipped is still a vertex of the mesh, shared with the
            // wall triangles that meet the cut there; an ear whose edge runs exactly through it
            // does not overlap anything, but it does leave a cap triangle touching a wall triangle
            // it has no vertex in common with, which is a self-intersection.
            bool clear = true;
            for (int j = 0; j < polygon.Count && clear; j++)
            {
                if (j == a || j == b || j == c)
                {
                    continue;
                }

                Vector2d p = polygon[j].Point;

                // A bridge doubles the vertices it lands on, so "not one of a, b, c" has to be
                // judged by position too, or a vertex sitting exactly on top of the ear's own
                // corner blocks it.
                if (p.Distance(pa) < CoincidentTolerance
                    || p.Distance(pb) < CoincidentTolerance
                    || p.Distance(pc) < CoincidentTolerance)
                {
                    continue;
                }

                // Containment includes the triangle's boundary. A cut cross-section is mostly
                // axis-aligned, so loop vertices land exactly on other candidate ears' edges all
                // the time: a strict interior test lets an ear swallow a hole whose corners sit on
                // that edge, and lets a cap triangle graze a wall vertex it does not share, which
                // the self-intersection detector — rightly — reports.
                clear = !InsideOrOn(pa, pb, pc, p, tolerance);
            }

            if (clear)
            {
                return k;
            }
        }

        return -1;
    }

    private static int MostConvex(List<(int Vertex, Vector2d Point)> polygon, List<int> indices)
    {
        int count = indices.Count;
        int best = -1;
        double bestCross = double.NegativeInfinity;
        for (int k = 0; k < count; k++)
        {
            int a = indices[(k - 1 + count) % count];
            int b = indices[k];
            int c = indices[(k + 1) % count];
            double cross = Cross(polygon[a].Point, polygon[b].Point, polygon[c].Point);
            if (cross > bestCross)
            {
                bestCross = cross;
                best = k;
            }
        }

        return best;
    }

    private static bool InsideOrOn(Vector2d a, Vector2d b, Vector2d c, Vector2d p, double tolerance) =>
        Outside(a, b, p, tolerance) && Outside(b, c, p, tolerance) && Outside(c, a, p, tolerance);

    /// <summary>True when <paramref name="p"/> is on the inner side of the directed edge, or within <paramref name="tolerance"/> of it.</summary>
    private static bool Outside(Vector2d from, Vector2d to, Vector2d p, double tolerance) =>
        Cross(from, to, p) >= -tolerance * from.Distance(to);

    /// <summary>
    /// True when the triangle is thicker than <paramref name="tolerance"/> — its shortest height,
    /// not its area, since a long thin triangle spanning the cross-section has an area that looks
    /// respectable next to an absolute floor while being a sliver in every practical sense.
    /// </summary>
    private static bool HasRealArea(Vector2d a, Vector2d b, Vector2d c, double tolerance)
    {
        double longest = Math.Max(a.Distance(b), Math.Max(b.Distance(c), c.Distance(a)));
        return Math.Abs(Cross(a, b, c)) > tolerance * longest;
    }

    /// <summary>
    /// The distance below which two points count as on the same line for this polygon. Cut
    /// cross-sections are full of collinear points, but their coordinates are interpolated along
    /// mesh edges — from an STL's float32 vertices, most of the time — so they land slightly off
    /// the line they belong to. Judged against an exact zero, such a point reads as a valid
    /// corner: clipping it emits a sliver, and treating a grazing cap triangle as merely touching
    /// leaves it intersecting a wall triangle it shares no vertex with.
    /// </summary>
    private static double Tolerance(List<(int Vertex, Vector2d Point)> polygon)
    {
        double extent = 0.0;
        Vector2d origin = polygon[0].Point;
        foreach ((int _, Vector2d point) in polygon)
        {
            extent = Math.Max(extent, Math.Abs(point.x - origin.x));
            extent = Math.Max(extent, Math.Abs(point.y - origin.y));
        }

        return 1e-6 * extent;
    }
}
