using g3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// Result of a drain hole drilling operation, reporting what was actually achieved.
///
/// <para>
/// Every "achieved" field on this record is <b>measured from the resulting mesh</b>, never copied
/// from the request. An earlier version returned <c>DiameterAchieved: diameter</c> — the requested
/// value verbatim — so it could not disagree with the request even when the operation had removed
/// sixteen times the requested area (SPECIFICATION.md §11, 2026-09-06). Compare
/// <see cref="DiameterRequested"/> with <see cref="DiameterAchieved"/> to see whether the mesh could
/// honour the request; <see cref="Message"/> says so in words when it could not.
/// </para>
/// </summary>
/// <param name="HolePlaced">True if a hole was cut. False leaves the mesh untouched.</param>
/// <param name="DiameterRequested">The diameter that was asked for, in mesh units.</param>
/// <param name="DiameterAchieved">
/// Diameter of the opening, measured by walking the resulting boundary loop in the mesh and
/// averaging its vertices' distance from the hole axis. 0 when no hole was cut.
/// </param>
/// <param name="CountersinkRequested">The countersink depth that was asked for, in mesh units.</param>
/// <param name="CountersinkAchieved">
/// Depth of the conical chamfer, measured along the hole axis between the chamfer rim on the surface
/// and the resulting boundary loop. 0 when no countersink was cut.
/// </param>
/// <param name="TrianglesRemoved">Triangles removed from the surface patch the hole was cut in.</param>
/// <param name="TrianglesAdded">Triangles added to stitch the opening back into the surface.</param>
/// <param name="VerticesAdded">Net change in vertex count. A drill that constructs nothing shows 0.</param>
/// <param name="SurfaceAreaRemoved">
/// Change in surface area, measured as area-before minus area-after. For a hole with no countersink
/// this is close to the requested circle's area (πr²). It goes <i>negative</i> for a countersink,
/// whose cone wall puts back more surface than the wider surface circle removes.
/// </param>
/// <param name="Message">Plain-language description of what was done, or why it could not be done.</param>
public sealed record DrainHoleResult(
    bool HolePlaced,
    double DiameterRequested,
    double DiameterAchieved,
    double CountersinkRequested,
    double CountersinkAchieved,
    int TrianglesRemoved,
    int TrianglesAdded,
    int VerticesAdded,
    double SurfaceAreaRemoved,
    string Message);

/// <summary>
/// Drain hole drilling (SPECIFICATION.md §5.1 "Edit — Drain holes"). A drain hole lets trapped resin
/// out of a hollowed print, so the opening has to be the size the user asked for.
///
/// <para><b>What this does.</b> It cuts a circle of the requested diameter through the surface at the
/// picked point and stitches the surrounding surface back onto that circle:</para>
/// <list type="number">
/// <item>Find the triangle under the pick and grow a local patch of connected, similarly-facing
/// triangles around the hole axis — the patch must be a topological disc that fully contains the
/// requested circle, or the operation refuses rather than cutting something else.</item>
/// <item>Sample the requested circle and project each sample onto that patch, so the new rim follows
/// the existing surface instead of floating in the tangent plane.</item>
/// <item>Remove the patch and re-triangulate the ring between the patch's own outline and the new
/// circle, traversing the outline the way the surrounding mesh requires so the result stays
/// consistently wound.</item>
/// <item>With a countersink, cut the surface circle wider by the countersink depth (a 45° chamfer)
/// and add a cone from that rim down to the requested circle, which remains the opening.</item>
/// </list>
///
/// <para><b>Open, not a bored tube.</b> A drain hole deliberately leaves a boundary loop: the point of
/// the hole in a hollowed model is that the cavity is now connected to the outside, and §5.1 lists
/// drain holes as the companion to Hollow. Each hole therefore adds exactly one boundary loop — the
/// requested circle — and the mesh is legitimately open there. It is <i>not</i> a boolean-subtracted
/// tube: that would add a cylinder wall's worth of surface area to the model and would still leave a
/// hollow model's cavity sealed off behind the far wall. Inspect will report one boundary hole per
/// drain hole, which is the truth about the model.</para>
///
/// <para><b>Refusal over damage.</b> If the requested circle does not fit in the local surface, the mesh
/// is left exactly as it was and the message names the largest diameter that does fit (§4, "Never
/// silently destroy the model"). All geometry is built on a working copy and committed only once
/// every step has succeeded.</para>
/// </summary>
public sealed class DrainHole
{
    /// <summary>Segments the hole circle is sampled with. 32 keeps the polygon's area within 0.7% of
    /// the true circle while every vertex sits exactly on the requested radius.</summary>
    private const int CircleSegments = 32;

    /// <summary>How far around the hole the local surface patch is grown, as a multiple of the rim
    /// radius, tried in order until the circle fits inside the patch.</summary>
    private static readonly double[] GrowthFactors = { 2.0, 3.0, 4.5, 7.0, 12.0 };

    /// <summary>
    /// How much wider than the hole the surrounding surface must be before the hole is cut there.
    /// The margin is what keeps the outline far enough outside the rim for the fan between them to
    /// stay unfolded: at 1.10 an outline vertex sees at least 24° of the rim arc, comfortably more
    /// than the <see cref="MaxOutlineSubtendedAngle"/> any single fan spans.
    /// </summary>
    private const double FitMargin = 1.10;

    /// <summary>
    /// The largest angle, measured at the hole axis, that one outline edge may subtend before it is
    /// split. Without this the outline of a coarse patch — the four corners of a cube face — fans
    /// onto the rim from so far away that part of the arc is behind the hole from the corner's point
    /// of view, and those triangles come out inverted and overlapping. Refining the outline first is
    /// what lets a coarse mesh carry a circular hole at all.
    /// </summary>
    private static readonly double MaxOutlineSubtendedAngle = 2.0 * Math.PI / (CircleSegments * 1.5);

    /// <summary>Cap on outline edge splits: 2^6 = 64 pieces per original edge.</summary>
    private const int MaxOutlineSplitDepth = 6;

    /// <summary>A patch triangle must face roughly the same way as the hole axis. This keeps the patch
    /// from wrapping around a corner or reaching the far wall of a thin shell.</summary>
    private const double MinFacingDot = 0.15;

    /// <summary>
    /// Drills a drain hole at the given surface location. On success <paramref name="mesh"/> is
    /// modified in place; on failure it is left exactly as it was.
    /// </summary>
    /// <param name="mesh">Mesh to drill, in mesh units (mm).</param>
    /// <param name="surfacePoint">Centre of the hole on the mesh surface, in world coordinates.</param>
    /// <param name="surfaceNormal">Surface normal at the hole location, pointing out of the mesh.</param>
    /// <param name="diameter">Requested hole diameter. Must be positive.</param>
    /// <param name="countersinkDepth">
    /// Depth of the conical chamfer at the surface, in mesh units. 0 = a plain hole. The chamfer
    /// widens the surface opening to <c>diameter + 2 × countersinkDepth</c> and tapers back down to
    /// the requested diameter, which stays the size of the opening itself.
    /// </param>
    public static DrainHoleResult PlaceDrainHole(
        DMesh3 mesh,
        Vector3d surfacePoint,
        Vector3d surfaceNormal,
        double diameter,
        double countersinkDepth = 0.0)
    {
        if (diameter <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(diameter), diameter, "Diameter must be positive.");
        }

        if (countersinkDepth < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(countersinkDepth), countersinkDepth, "Countersink depth must be non-negative.");
        }

        if (surfaceNormal.LengthSquared < 1e-20)
        {
            throw new ArgumentException("Surface normal must be non-zero.", nameof(surfaceNormal));
        }

        if (mesh.TriangleCount == 0)
        {
            return Failed(diameter, countersinkDepth, "The mesh has no triangles to drill.");
        }

        Vector3d axis = surfaceNormal.Normalized;
        BuildFrame(axis, out Vector3d u, out Vector3d v);

        double holeRadius = diameter / 2.0;

        // Work on a copy: a half-built hole is worse than no hole (§4).
        var work = new DMesh3(mesh);

        int seed = FindSeedTriangle(work, surfacePoint, axis);
        if (seed < 0)
        {
            return Failed(diameter, countersinkDepth,
                "No surface facing the requested direction was found near that point, so there was nothing to drill.");
        }

        // A countersink is a 45° chamfer, so the surface circle widens by its depth. Clamp it to the
        // material actually under the pick rather than pushing a cone out through the far side.
        double materialDepth = MeasureMaterialDepth(work, surfacePoint, axis);
        double countersink = countersinkDepth;
        string countersinkNote = "";
        if (countersink > 0.0 && materialDepth > 0.0 && countersink > materialDepth * 0.8)
        {
            countersink = materialDepth * 0.8;
            countersinkNote = string.Format(
                CultureInfo.InvariantCulture,
                " Countersink reduced from {0:0.##}mm to {1:0.##}mm: there is only {2:0.##}mm of material under this point.",
                countersinkDepth, countersink, materialDepth);
        }

        double bestFitRadius = 0.0;
        string? structuralFailure = null;

        // Grow the patch until the rim circle fits inside it.
        Patch? FindPatch(double rimRadius)
        {
            foreach (double factor in GrowthFactors)
            {
                PatchAttempt attempt = TryBuildPatch(work, seed, surfacePoint, axis, u, v, rimRadius * factor);
                if (attempt.Patch is null)
                {
                    structuralFailure ??= attempt.Failure;
                    continue;
                }

                bestFitRadius = Math.Max(bestFitRadius, attempt.Patch.MaxFittingRadius);
                if (attempt.Patch.MaxFittingRadius >= rimRadius * FitMargin)
                {
                    return attempt.Patch;
                }
            }

            return null;
        }

        Patch? patch = FindPatch(holeRadius + countersink);

        // The requested diameter is what the user is drilling for, so if only the chamfer does not
        // fit, narrow the chamfer and say so rather than refusing the hole outright.
        if (patch is null && countersink > 0.0 && bestFitRadius > holeRadius * FitMargin)
        {
            double narrowed = bestFitRadius / FitMargin - holeRadius;
            Patch? retry = FindPatch(holeRadius + narrowed);
            if (retry is not null)
            {
                countersinkNote = string.Format(
                    CultureInfo.InvariantCulture,
                    " Countersink reduced from {0:0.##}mm to {1:0.##}mm: the surface at this point is not wide enough for a fuller chamfer.",
                    countersinkDepth, narrowed);
                countersink = narrowed;
                patch = retry;
            }
        }

        double rimRadius = holeRadius + countersink;

        if (patch is null)
        {
            if (bestFitRadius > 0.0)
            {
                double largest = 2.0 * (bestFitRadius / FitMargin - countersink);
                string what = largest > 0.0
                    ? string.Format(CultureInfo.InvariantCulture, "the largest that fits here is about Ø{0:0.##}mm", largest)
                    : "no hole of any size fits here with that countersink";
                return Failed(diameter, countersinkDepth, string.Format(
                    CultureInfo.InvariantCulture,
                    "Could not cut a Ø{0:0.##}mm hole at this point: {1}. The mesh was left unchanged.",
                    diameter, what));
            }

            return Failed(diameter, countersinkDepth,
                (structuralFailure ?? "The surface around this point is not a simple patch.") + " The mesh was left unchanged.");
        }

        // Sample the rim circle and project it onto the patch, so the new geometry follows the
        // existing surface rather than the tangent plane. Sampling starts at the outline's own first
        // vertex and runs in the outline's rotational sense, so the loft below never twists.
        var rimPoints = new Vector3d[CircleSegments];
        var holePoints = new Vector3d[CircleSegments];
        for (int k = 0; k < CircleSegments; k++)
        {
            double theta = patch.StartAngle + patch.Sense * 2.0 * Math.PI * k / CircleSegments;
            double c = Math.Cos(theta), s = Math.Sin(theta);

            if (!patch.TryProject(new Vector2d(rimRadius * c, rimRadius * s), out rimPoints[k]))
            {
                return Failed(diameter, countersinkDepth, string.Format(
                    CultureInfo.InvariantCulture,
                    "Could not cut a Ø{0:0.##}mm hole at this point: part of the circle falls off the local surface. The mesh was left unchanged.",
                    diameter));
            }

            if (countersink > 0.0)
            {
                if (!patch.TryProject(new Vector2d(holeRadius * c, holeRadius * s), out Vector3d holeSurface))
                {
                    return Failed(diameter, countersinkDepth, string.Format(
                        CultureInfo.InvariantCulture,
                        "Could not cut a Ø{0:0.##}mm hole at this point: part of the circle falls off the local surface. The mesh was left unchanged.",
                        diameter));
                }

                holePoints[k] = holeSurface - axis * countersink;
            }
        }

        double areaBefore = TotalArea(work);
        int verticesBefore = work.VertexCount;

        // Remove the patch. Everything above this line is non-destructive.
        int removed = 0;
        foreach (int tid in patch.Triangles)
        {
            if (work.IsTriangle(tid) && work.RemoveTriangle(tid) == MeshResult.Ok)
            {
                removed++;
            }
        }

        int group = patch.GroupId;

        int[] rimRing = AppendRing(work, rimPoints);
        int[] holeRing = countersink > 0.0 ? AppendRing(work, holePoints) : rimRing;

        // Refine the outline before stitching: a coarse outline fans onto the rim from too far away
        // and folds over itself (see MaxOutlineSubtendedAngle). Splitting boundary edges moves no
        // vertex and adds no area, so volume, bounds and surface area are untouched by this step.
        int[] outline = RefineOutline(work, patch.Outline, surfacePoint, axis, u, v);
        if (!TryOutlineParameters(work, outline, surfacePoint, axis, u, v, out double[] outlineT))
        {
            return Failed(diameter, countersinkDepth,
                "The surface around this point could not be prepared for a clean opening. The mesh was left unchanged.");
        }

        int added = 0;
        if (!TryStitchAnnulus(work, outline, outlineT, rimRing, group, ref added))
        {
            return Failed(diameter, countersinkDepth,
                "Could not stitch the hole into the surrounding surface at this point. The mesh was left unchanged.");
        }

        if (countersink > 0.0 && !TryStitchCone(work, rimRing, holeRing, group, ref added))
        {
            return Failed(diameter, countersinkDepth,
                "Could not build the countersink chamfer at this point. The mesh was left unchanged.");
        }

        // Measure the result from the mesh itself.
        if (!TryMeasureBoundaryLoop(work, holeRing, surfacePoint, axis, out double achievedDiameter, out int loopLength))
        {
            return Failed(diameter, countersinkDepth,
                "The hole did not close into a single boundary loop, so it was discarded. The mesh was left unchanged.");
        }

        if (loopLength != CircleSegments)
        {
            return Failed(diameter, countersinkDepth,
                "The hole merged with existing geometry instead of forming its own opening, so it was discarded. The mesh was left unchanged.");
        }

        double achievedCountersink = countersink > 0.0
            ? Math.Max(0.0, MeanAxial(work, rimRing, axis) - MeanAxial(work, holeRing, axis))
            : 0.0;

        double areaRemoved = areaBefore - TotalArea(work);
        int verticesAdded = work.VertexCount - verticesBefore;

        // Commit.
        mesh.Copy(work);

        // DMesh3.Copy replaces the mesh's contents without advancing its Timestamp, and DMesh3 keys
        // CachedIsClosed off that stamp — which MeshBoundaryLoops early-returns on. Left alone, a mesh
        // whose closedness had been cached before the drill (Inspect does exactly that on every load)
        // keeps answering "closed" afterwards, and the drain hole becomes invisible to hole detection,
        // hole filling and the diagnostics panel alike. Re-stamping the mesh by writing one vertex
        // back to where it already is costs nothing and moves nothing.
        foreach (int vid in mesh.VertexIndices())
        {
            mesh.SetVertex(vid, mesh.GetVertex(vid));
            break;
        }

        string message = string.Format(
            CultureInfo.InvariantCulture,
            "Drilled a Ø{0:0.###}mm drain hole (measured Ø{1:0.###}mm){2}. Surface area {3} by {4:0.###}mm².",
            diameter,
            achievedDiameter,
            achievedCountersink > 1e-9
                ? string.Format(CultureInfo.InvariantCulture, " with a {0:0.###}mm countersink", achievedCountersink)
                : "",
            areaRemoved >= 0.0 ? "fell" : "rose",
            Math.Abs(areaRemoved));

        if (Math.Abs(achievedDiameter - diameter) > diameter * 0.02)
        {
            message += string.Format(
                CultureInfo.InvariantCulture,
                " The opening measures Ø{0:0.###}mm rather than the requested Ø{1:0.###}mm — the local mesh could not hold the full circle.",
                achievedDiameter, diameter);
        }

        message += countersinkNote;

        return new DrainHoleResult(
            HolePlaced: true,
            DiameterRequested: diameter,
            DiameterAchieved: achievedDiameter,
            CountersinkRequested: countersinkDepth,
            CountersinkAchieved: achievedCountersink,
            TrianglesRemoved: removed,
            TrianglesAdded: added,
            VerticesAdded: verticesAdded,
            SurfaceAreaRemoved: areaRemoved,
            Message: message);
    }

    private static DrainHoleResult Failed(double diameter, double countersink, string message) =>
        new(
            HolePlaced: false,
            DiameterRequested: diameter,
            DiameterAchieved: 0.0,
            CountersinkRequested: countersink,
            CountersinkAchieved: 0.0,
            TrianglesRemoved: 0,
            TrianglesAdded: 0,
            VerticesAdded: 0,
            SurfaceAreaRemoved: 0.0,
            Message: message);

    // ---------------------------------------------------------------- frame and measurement helpers

    private static void BuildFrame(Vector3d axis, out Vector3d u, out Vector3d v)
    {
        Vector3d helper = Math.Abs(axis.z) < 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(1, 0, 0);
        u = axis.Cross(helper).Normalized;
        v = axis.Cross(u).Normalized;
    }

    private static double TotalArea(DMesh3 mesh)
    {
        double area = 0.0;
        foreach (int tid in mesh.TriangleIndices())
        {
            area += mesh.GetTriArea(tid);
        }

        return area;
    }

    private static double MeanAxial(DMesh3 mesh, int[] ring, Vector3d axis)
    {
        double sum = 0.0;
        foreach (int vid in ring)
        {
            sum += mesh.GetVertex(vid).Dot(axis);
        }

        return sum / ring.Length;
    }

    /// <summary>
    /// The triangle nearest the pick that faces the same way as the hole axis. The facing test is what
    /// keeps a pick on a thin shell from seizing the wall behind it.
    /// </summary>
    private static int FindSeedTriangle(DMesh3 mesh, Vector3d point, Vector3d axis)
    {
        int best = -1;
        double bestDist = double.MaxValue;

        foreach (int tid in mesh.TriangleIndices())
        {
            if (mesh.GetTriNormal(tid).Dot(axis) < MinFacingDot)
            {
                continue;
            }

            Index3i tri = mesh.GetTriangle(tid);
            double d = PointTriangleDistance(point, mesh.GetVertex(tri.a), mesh.GetVertex(tri.b), mesh.GetVertex(tri.c));
            if (d < bestDist)
            {
                bestDist = d;
                best = tid;
            }
        }

        return best;
    }

    /// <summary>
    /// Distance along -axis from the surface point to the next piece of mesh: how much material is
    /// under the pick. 0 if the ray leaves the model without hitting anything.
    /// </summary>
    private static double MeasureMaterialDepth(DMesh3 mesh, Vector3d point, Vector3d axis)
    {
        double scale = mesh.GetBounds().DiagonalLength;
        Vector3d origin = point - axis * (scale * 1e-6);
        Vector3d dir = -axis;

        double nearest = double.MaxValue;
        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            if (RayTriangle(origin, dir, mesh.GetVertex(tri.a), mesh.GetVertex(tri.b), mesh.GetVertex(tri.c), out double t)
                && t > scale * 1e-5 && t < nearest)
            {
                nearest = t;
            }
        }

        return nearest == double.MaxValue ? 0.0 : nearest;
    }

    private static bool RayTriangle(Vector3d o, Vector3d d, Vector3d a, Vector3d b, Vector3d c, out double t)
    {
        t = 0.0;
        Vector3d e1 = b - a, e2 = c - a;
        Vector3d p = d.Cross(e2);
        double det = e1.Dot(p);
        if (Math.Abs(det) < 1e-14)
        {
            return false;
        }

        double inv = 1.0 / det;
        Vector3d tv = o - a;
        double uu = tv.Dot(p) * inv;
        if (uu < -1e-9 || uu > 1.0 + 1e-9)
        {
            return false;
        }

        Vector3d q = tv.Cross(e1);
        double vv = d.Dot(q) * inv;
        if (vv < -1e-9 || uu + vv > 1.0 + 1e-9)
        {
            return false;
        }

        t = e2.Dot(q) * inv;
        return t > 0.0;
    }

    private static double PointTriangleDistance(Vector3d p, Vector3d a, Vector3d b, Vector3d c)
    {
        var dt = new DistPoint3Triangle3(p, new Triangle3d(a, b, c));
        return Math.Sqrt(Math.Max(0.0, dt.GetSquared()));
    }

    // ---------------------------------------------------------------- the local patch

    /// <summary>A connected, similarly-facing disc of triangles surrounding the hole axis.</summary>
    private sealed class Patch
    {
        public required HashSet<int> Triangles { get; init; }

        /// <summary>Outline vertices in the order the new triangles must traverse them.</summary>
        public required int[] Outline { get; init; }

        /// <summary>Each outline vertex's position around the axis, normalised to [0,1).</summary>
        public required double[] OutlineParameters { get; init; }

        /// <summary>Angle of <c>Outline[0]</c> in the hole's tangent frame.</summary>
        public required double StartAngle { get; init; }

        /// <summary>+1 if the outline runs counter-clockwise in that frame, -1 if clockwise.</summary>
        public required double Sense { get; init; }

        /// <summary>Largest circle centred on the axis that still lies inside the outline.</summary>
        public required double MaxFittingRadius { get; init; }

        public required int GroupId { get; init; }

        public required Vector3d Center { get; init; }

        public required Vector3d Axis { get; init; }

        /// <summary>2D-projected patch triangles paired with their 3D vertices.</summary>
        public required List<(Vector2d A2, Vector2d B2, Vector2d C2, Vector3d A, Vector3d B, Vector3d C)> Projected { get; init; }

        /// <summary>
        /// Maps a point in the hole's tangent plane onto the patch surface. The mapping is affine
        /// within each triangle, so the result projects back to exactly the requested 2D point: a
        /// sample at radius r ends up at radius r from the axis, and the achieved diameter cannot
        /// drift from the request through this step.
        /// </summary>
        public bool TryProject(Vector2d p, out Vector3d point)
        {
            point = Vector3d.Zero;
            bool found = false;
            double bestAxial = double.MaxValue;

            foreach (var t in Projected)
            {
                if (!Barycentric(p, t.A2, t.B2, t.C2, out double wa, out double wb, out double wc))
                {
                    continue;
                }

                Vector3d candidate = t.A * wa + t.B * wb + t.C * wc;
                double axial = Math.Abs((candidate - Center).Dot(Axis));
                if (!found || axial < bestAxial)
                {
                    found = true;
                    bestAxial = axial;
                    point = candidate;
                }
            }

            return found;
        }

        private static bool Barycentric(Vector2d p, Vector2d a, Vector2d b, Vector2d c,
            out double wa, out double wb, out double wc)
        {
            wa = wb = wc = 0.0;
            double det = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Math.Abs(det) < 1e-18)
            {
                return false;
            }

            wa = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / det;
            wb = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / det;
            wc = 1.0 - wa - wb;
            const double eps = -1e-9;
            return wa >= eps && wb >= eps && wc >= eps;
        }
    }

    private readonly struct PatchAttempt
    {
        public Patch? Patch { get; init; }
        public string? Failure { get; init; }
    }

    private static PatchAttempt TryBuildPatch(
        DMesh3 mesh, int seed, Vector3d center, Vector3d axis, Vector3d u, Vector3d v, double growRadius)
    {
        var patch = new HashSet<int> { seed };
        var queue = new Queue<int>();
        queue.Enqueue(seed);

        while (queue.Count > 0)
        {
            int tid = queue.Dequeue();
            Index3i edges = mesh.GetTriEdges(tid);
            for (int i = 0; i < 3; i++)
            {
                Index2i tris = mesh.GetEdgeT(edges[i]);
                int other = tris.a == tid ? tris.b : tris.a;
                if (other == DMesh3.InvalidID || patch.Contains(other))
                {
                    continue;
                }

                if (mesh.GetTriNormal(other).Dot(axis) < MinFacingDot)
                {
                    continue;
                }

                if (MinRadialDistance(mesh, mesh.GetTriangle(other), center, axis) > growRadius)
                {
                    continue;
                }

                patch.Add(other);
                queue.Enqueue(other);
            }
        }

        // Oriented outline: for every patch edge whose other triangle is outside the patch, the new
        // geometry must traverse that edge opposite to the way the outside triangle uses it.
        var next = new Dictionary<int, int>();
        foreach (int tid in patch)
        {
            Index3i edges = mesh.GetTriEdges(tid);
            for (int i = 0; i < 3; i++)
            {
                int eid = edges[i];
                Index2i tris = mesh.GetEdgeT(eid);
                int other = tris.a == tid ? tris.b : tris.a;
                if (other != DMesh3.InvalidID && patch.Contains(other))
                {
                    continue;
                }

                if (other == DMesh3.InvalidID)
                {
                    return new PatchAttempt
                    {
                        Failure = "The surface around this point already has a boundary, and a drain hole must not be cut into an existing opening."
                    };
                }

                Index2i ev = mesh.GetEdgeV(eid);
                (int a, int b) = OrientedUse(mesh.GetTriangle(other), ev.a, ev.b);
                if (next.ContainsKey(b))
                {
                    return new PatchAttempt
                    {
                        Failure = "The surface around this point pinches together, so the hole has no single outline to stitch to."
                    };
                }

                next[b] = a;
            }
        }

        if (next.Count < 3)
        {
            return new PatchAttempt { Failure = "The surface around this point is too small to cut a hole in." };
        }

        var outline = new List<int>();
        int start = next.Keys.First();
        int cur = start;
        do
        {
            outline.Add(cur);
            if (!next.TryGetValue(cur, out int nxt))
            {
                return new PatchAttempt { Failure = "The surface around this point does not form a closed outline." };
            }

            cur = nxt;
        }
        while (cur != start && outline.Count <= next.Count);

        if (cur != start || outline.Count != next.Count)
        {
            return new PatchAttempt
            {
                Failure = "The surface around this point forms more than one outline, so the hole cannot be stitched cleanly."
            };
        }

        // Project the outline into the hole's tangent plane and require it to wind exactly once around
        // the axis. A patch that folds back on itself would produce inverted triangles.
        int m = outline.Count;
        var flat = new Vector2d[m];
        var angles = new double[m];
        for (int i = 0; i < m; i++)
        {
            flat[i] = Flatten(mesh.GetVertex(outline[i]), center, u, v);
            if (flat[i].Length < 1e-12)
            {
                return new PatchAttempt { Failure = "The surface around this point is degenerate at the hole centre." };
            }

            angles[i] = Math.Atan2(flat[i].y, flat[i].x);
        }

        double total = 0.0;
        int sign = 0;
        var cumulative = new double[m];
        double maxFitting = double.MaxValue;
        for (int i = 0; i < m; i++)
        {
            cumulative[i] = total;
            double delta = WrapPi(angles[(i + 1) % m] - angles[i]);
            if (Math.Abs(delta) > Math.PI * 0.98)
            {
                return new PatchAttempt { Failure = "The outline around this point doubles back, so the hole cannot be stitched cleanly." };
            }

            int s = Math.Sign(delta);
            if (s != 0)
            {
                if (sign == 0)
                {
                    sign = s;
                }
                else if (s != sign)
                {
                    return new PatchAttempt { Failure = "The outline around this point is not star-shaped about the hole, so it cannot be stitched cleanly." };
                }
            }

            total += delta;
            maxFitting = Math.Min(maxFitting, DistanceToSegment(flat[i], flat[(i + 1) % m]));
        }

        if (Math.Abs(Math.Abs(total) - 2.0 * Math.PI) > 1e-6 || sign == 0)
        {
            return new PatchAttempt { Failure = "The outline around this point does not enclose the hole." };
        }

        for (int i = 0; i < m; i++)
        {
            cumulative[i] = cumulative[i] / total;      // total carries the sign, so this lands in [0,1).
        }

        var projected = new List<(Vector2d, Vector2d, Vector2d, Vector3d, Vector3d, Vector3d)>(patch.Count);
        foreach (int tid in patch)
        {
            Index3i tri = mesh.GetTriangle(tid);
            Vector3d a = mesh.GetVertex(tri.a), b = mesh.GetVertex(tri.b), c = mesh.GetVertex(tri.c);
            projected.Add((Flatten(a, center, u, v), Flatten(b, center, u, v), Flatten(c, center, u, v), a, b, c));
        }

        return new PatchAttempt
        {
            Patch = new Patch
            {
                Triangles = patch,
                Outline = outline.ToArray(),
                OutlineParameters = cumulative,
                StartAngle = angles[0],
                Sense = sign,
                MaxFittingRadius = maxFitting,
                GroupId = mesh.GetTriangleGroup(seed),
                Center = center,
                Axis = axis,
                Projected = projected
            }
        };
    }

    private static Vector2d Flatten(Vector3d p, Vector3d center, Vector3d u, Vector3d v)
    {
        Vector3d d = p - center;
        return new Vector2d(d.Dot(u), d.Dot(v));
    }

    private static double DistanceToSegment(Vector2d a, Vector2d b)
    {
        Vector2d ab = b - a;
        double len2 = ab.LengthSquared;
        if (len2 < 1e-18)
        {
            return a.Length;
        }

        double t = Math.Clamp(-a.Dot(ab) / len2, 0.0, 1.0);
        return (a + ab * t).Length;
    }

    /// <summary>
    /// Distance from the hole axis to a triangle, sampled at its vertices, edge midpoints and centroid.
    /// Close enough to decide membership of a growth band, and cheap.
    /// </summary>
    private static double MinRadialDistance(DMesh3 mesh, Index3i tri, Vector3d center, Vector3d axis)
    {
        Vector3d a = mesh.GetVertex(tri.a), b = mesh.GetVertex(tri.b), c = mesh.GetVertex(tri.c);
        double best = double.MaxValue;
        foreach (Vector3d p in new[] { a, b, c, (a + b) * 0.5, (b + c) * 0.5, (c + a) * 0.5, (a + b + c) / 3.0 })
        {
            Vector3d d = p - center;
            best = Math.Min(best, (d - axis * d.Dot(axis)).Length);
        }

        return best;
    }

    private static double WrapPi(double angle)
    {
        while (angle > Math.PI) angle -= 2.0 * Math.PI;
        while (angle < -Math.PI) angle += 2.0 * Math.PI;
        return angle;
    }

    // ---------------------------------------------------------------- stitching

    /// <summary>
    /// Splits the patch outline's (now boundary) edges until no edge subtends more than
    /// <see cref="MaxOutlineSubtendedAngle"/> at the hole axis, returning the refined outline in the
    /// same traversal order. New vertices are edge midpoints, so the surface is unchanged in shape.
    /// </summary>
    private static int[] RefineOutline(DMesh3 mesh, int[] outline, Vector3d center, Vector3d axis, Vector3d u, Vector3d v)
    {
        var refined = new List<int>(outline.Length * 4);
        for (int i = 0; i < outline.Length; i++)
        {
            int a = outline[i];
            int b = outline[(i + 1) % outline.Length];
            refined.Add(a);
            SplitSpan(mesh, a, b, center, u, v, 0, refined);
        }

        return refined.ToArray();
    }

    private static void SplitSpan(DMesh3 mesh, int a, int b, Vector3d center, Vector3d u, Vector3d v, int depth, List<int> into)
    {
        if (depth >= MaxOutlineSplitDepth)
        {
            return;
        }

        double angleA = Angle(mesh.GetVertex(a), center, u, v);
        double angleB = Angle(mesh.GetVertex(b), center, u, v);
        if (Math.Abs(WrapPi(angleB - angleA)) <= MaxOutlineSubtendedAngle)
        {
            return;
        }

        if (mesh.SplitEdge(a, b, out DMesh3.EdgeSplitInfo split) != MeshResult.Ok)
        {
            return;
        }

        SplitSpan(mesh, a, split.vNew, center, u, v, depth + 1, into);
        into.Add(split.vNew);
        SplitSpan(mesh, split.vNew, b, center, u, v, depth + 1, into);
    }

    private static double Angle(Vector3d p, Vector3d center, Vector3d u, Vector3d v)
    {
        Vector2d flat = Flatten(p, center, u, v);
        return Math.Atan2(flat.y, flat.x);
    }

    /// <summary>
    /// Each outline vertex's position around the hole axis, normalised to [0,1) in the outline's own
    /// traversal direction. Fails if the refined outline no longer winds exactly once around the
    /// axis, which would mean the loft could not be trusted.
    /// </summary>
    private static bool TryOutlineParameters(
        DMesh3 mesh, int[] outline, Vector3d center, Vector3d axis, Vector3d u, Vector3d v, out double[] parameters)
    {
        int m = outline.Length;
        parameters = new double[m];
        var angles = new double[m];
        for (int i = 0; i < m; i++)
        {
            angles[i] = Angle(mesh.GetVertex(outline[i]), center, u, v);
        }

        double total = 0.0;
        int sign = 0;
        for (int i = 0; i < m; i++)
        {
            parameters[i] = total;
            double delta = WrapPi(angles[(i + 1) % m] - angles[i]);
            int s = Math.Sign(delta);
            if (s != 0)
            {
                if (sign == 0)
                {
                    sign = s;
                }
                else if (s != sign)
                {
                    return false;
                }
            }

            total += delta;
        }

        if (sign == 0 || Math.Abs(Math.Abs(total) - 2.0 * Math.PI) > 1e-6)
        {
            return false;
        }

        for (int i = 0; i < m; i++)
        {
            parameters[i] /= total;
        }

        return true;
    }

    private static int[] AppendRing(DMesh3 mesh, Vector3d[] points)
    {
        var ring = new int[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            ring[i] = mesh.AppendVertex(points[i]);
        }

        return ring;
    }

    /// <summary>
    /// Lofts the patch outline onto the new ring, walking both loops in step by angle. Triangles are
    /// emitted as (O_i, O_i+1, I_j) and (O_i, I_j+1, I_j), which traverses every outline edge in the
    /// direction the surrounding mesh requires and every ring edge the opposite way, so the ring is
    /// left as a boundary and the winding matches the surface it replaced.
    /// </summary>
    private static bool TryStitchAnnulus(DMesh3 mesh, int[] outer, double[] outerT, int[] inner, int group, ref int added)
    {
        int m = outer.Length, n = inner.Length;

        int i = 0, j = 0;
        while (i < m || j < n)
        {
            double nextOuter = i + 1 < m ? outerT[i + 1] : 1.0;
            double nextInner = j + 1 < n ? (j + 1) / (double)n : 1.0;
            bool takeOuter = i < m && (j >= n || nextOuter <= nextInner);

            int tid = takeOuter
                ? mesh.AppendTriangle(outer[i], outer[(i + 1) % m], inner[j % n], group)
                : mesh.AppendTriangle(outer[i % m], inner[(j + 1) % n], inner[j % n], group);

            if (tid < 0)
            {
                return false;
            }

            added++;
            if (takeOuter)
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return true;
    }

    /// <summary>Cone between two rings of equal length: the countersink chamfer.</summary>
    private static bool TryStitchCone(DMesh3 mesh, int[] rim, int[] hole, int group, ref int added)
    {
        int n = rim.Length;
        for (int i = 0; i < n; i++)
        {
            int i2 = (i + 1) % n;
            if (mesh.AppendTriangle(rim[i], rim[i2], hole[i], group) < 0)
            {
                return false;
            }

            added++;

            if (mesh.AppendTriangle(rim[i2], hole[i2], hole[i], group) < 0)
            {
                return false;
            }

            added++;
        }

        return true;
    }

    // ---------------------------------------------------------------- result measurement

    /// <summary>
    /// Walks the boundary loop the drill left in the mesh and measures its diameter from the vertex
    /// positions actually stored there. This is the only source of <c>DiameterAchieved</c>: it reads
    /// the mesh, so it can disagree with the request.
    /// </summary>
    private static bool TryMeasureBoundaryLoop(
        DMesh3 mesh, int[] ring, Vector3d center, Vector3d axis, out double diameter, out int loopLength)
    {
        diameter = 0.0;
        loopLength = 0;

        int startVertex = ring[0];
        if (!mesh.IsVertex(startVertex) || !mesh.IsBoundaryVertex(startVertex))
        {
            return false;
        }

        var visited = new HashSet<int>();
        var loop = new List<int>();
        int cur = startVertex;
        int prev = -1;

        while (true)
        {
            loop.Add(cur);
            if (!visited.Add(cur))
            {
                return false;
            }

            var neighbours = new List<int>();
            foreach (int eid in mesh.VtxEdgesItr(cur))
            {
                if (!mesh.IsBoundaryEdge(eid))
                {
                    continue;
                }

                Index2i ev = mesh.GetEdgeV(eid);
                neighbours.Add(ev.a == cur ? ev.b : ev.a);
            }

            if (neighbours.Count != 2)
            {
                return false;       // A dead end or a fork: not a simple loop.
            }

            // At the start there is no previous vertex, so either direction will do.
            int next = prev < 0 ? neighbours[0] : neighbours.FirstOrDefault(x => x != prev, -1);
            if (next < 0)
            {
                return false;
            }

            if (next == startVertex)
            {
                break;
            }

            prev = cur;
            cur = next;

            if (loop.Count > ring.Length * 4)
            {
                return false;
            }
        }

        double sum = 0.0;
        foreach (int vid in loop)
        {
            Vector3d d = mesh.GetVertex(vid) - center;
            sum += (d - axis * d.Dot(axis)).Length;
        }

        loopLength = loop.Count;
        diameter = 2.0 * sum / loop.Count;
        return true;
    }

    private static (int, int) OrientedUse(Index3i tri, int x, int y)
    {
        if ((tri.a == x && tri.b == y) || (tri.b == x && tri.c == y) || (tri.c == x && tri.a == y))
        {
            return (x, y);
        }

        return (y, x);
    }
}
