using g3;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// What to build when a plane cut is asked for a registration pin
/// (SPECIFICATION.md §5.1 "Edit — Plane cut ... with an optional peg-and-socket alignment pin
/// pair on the mating faces").
/// </summary>
/// <param name="Diameter">Peg diameter in mesh units. The socket is this plus twice <paramref name="Clearance"/>.</param>
/// <param name="Clearance">
/// Radial gap between peg and socket wall, in mesh units. It is applied to the <i>socket</i> only:
/// the peg comes out at exactly the requested diameter, so the printed part measures what the user
/// asked for, and the hole it drops into is the one that grows.
/// </param>
/// <param name="Depth">
/// How far the peg protrudes, in mesh units. Null means one diameter, the usual rule of thumb for
/// an alignment dowel. The socket is cut this deep plus <paramref name="Clearance"/>, so the two
/// mating faces meet flush rather than bottoming out on the peg's end.
/// </param>
/// <param name="Center">
/// Where to put the pin, in world coordinates; the point is projected onto the cut plane. Null asks
/// for automatic placement at the point of the cross-section furthest from any of its edges.
/// </param>
public sealed record RegistrationPinOptions(
    double Diameter,
    double Clearance,
    double? Depth = null,
    Vector3d? Center = null);

/// <summary>
/// What a registration pin request actually produced. Every "achieved" figure is read back off the
/// finished geometry rather than copied from the request — the rule §11 (2026-09-06) records after
/// <c>DrainHoleResult.DiameterAchieved</c> returned the requested value verbatim and so could never
/// disagree with it.
/// </summary>
/// <param name="PinPlaced">False leaves both halves exactly as an unpinned cut would have left them.</param>
/// <param name="DiameterRequested">Peg diameter asked for.</param>
/// <param name="PegDiameterAchieved">Peg diameter measured from the peg half's cylinder vertices. 0 when no pin was placed.</param>
/// <param name="SocketDiameterAchieved">Socket bore diameter measured from the socket half's cylinder vertices. 0 when no pin was placed.</param>
/// <param name="ClearanceRequested">Radial gap asked for.</param>
/// <param name="ClearanceAchieved">Half the difference of the two measured diameters — the gap the printed parts will actually have.</param>
/// <param name="DepthRequested">Peg protrusion asked for (after the "one diameter" default is resolved).</param>
/// <param name="DepthAchieved">Peg protrusion measured along the plane normal from the mating face to the peg's end. 0 when no pin was placed.</param>
/// <param name="Center">Pin axis origin on the cut plane, in world coordinates.</param>
/// <param name="Axis">Pin axis direction: the plane normal, pointing from the socket half towards the peg half.</param>
/// <param name="LargestDiameterThatFits">
/// The biggest peg diameter this cross-section could host at the requested clearance, measured from
/// the cross-section itself. Meaningful when <paramref name="PinPlaced"/> is false because the pin
/// was too big; 0 when the cross-section cannot host any pin at all.
/// </param>
/// <param name="Message">Plain-language description of what was done, or why it could not be.</param>
public sealed record RegistrationPinResult(
    bool PinPlaced,
    double DiameterRequested,
    double PegDiameterAchieved,
    double SocketDiameterAchieved,
    double ClearanceRequested,
    double ClearanceAchieved,
    double DepthRequested,
    double DepthAchieved,
    Vector3d Center,
    Vector3d Axis,
    double LargestDiameterThatFits,
    string Message);

/// <summary>
/// The pin a plane cut is about to build: where its axis is, how big the two mating cylinders are,
/// and the two circles that have already been added to the split mesh as extra cross-section loops.
/// </summary>
internal sealed class RegistrationPinPlan
{
    internal required Vector3d Center { get; init; }

    internal required Vector3d Axis { get; init; }

    internal required double PegRadius { get; init; }

    internal required double SocketRadius { get; init; }

    internal required double PegDepth { get; init; }

    internal required double SocketDepth { get; init; }

    /// <summary>The peg circle, as split-mesh vertex ids, wound counter-clockwise in the plane basis.</summary>
    internal required List<int> PegLoop { get; init; }

    /// <summary>The socket circle, as split-mesh vertex ids, wound counter-clockwise in the plane basis.</summary>
    internal required List<int> SocketLoop { get; init; }

    internal required RegistrationPinOptions Options { get; init; }

    internal required double LargestDiameterThatFits { get; init; }
}

/// <summary>
/// Builds the peg-and-socket pair a plane cut leaves on its mating faces.
///
/// <para><b>Generated, not booleaned.</b> The pin is constructed directly: its circle joins the cut
/// cross-section as one more loop, so the existing capping code punches it out of the cap for free,
/// and a cylinder wall plus an end disc is stitched onto the resulting boundary. Nothing is
/// intersected against anything. The Reddit complaint that put this feature into v1.0 (§11,
/// 2026-09-06) was about a boolean union taking twenty minutes on a hollow cube — an implementation
/// that hands two meshes to a boolean reproduces exactly the problem the feature exists to solve.
/// The cost here is proportional to the cross-section, not to the model.</para>
///
/// <para><b>Both directions.</b> The peg half and the socket half are two ends of one round trip:
/// the user splits a model, prints it, and puts it back together. The socket is bored
/// <see cref="RegistrationPinOptions.Clearance"/> wider <i>and</i> that much deeper than the peg, so
/// the two mating faces meet flush instead of the peg's end bottoming out first.</para>
///
/// <para><b>Refusal over damage.</b> If the requested pin does not fit the cross-section, or the
/// socket would break out through the model's own walls below the cut, nothing is built and the
/// message names the largest diameter that would have fitted (§4, "Never silently destroy the
/// model"; §11's rule as applied to drain holes and decimation).</para>
/// </summary>
internal static class RegistrationPinBuilder
{
    /// <summary>Segments each pin circle is sampled with, matching <see cref="DrainHole"/>'s.</summary>
    internal const int CircleSegments = 32;

    /// <summary>
    /// How much bigger than the socket the cross-section's largest inscribed circle must be. At 1.5
    /// the material left around the bore is at least half the bore's own radius, which is what stops
    /// a pin from turning the mating face into a ring of paper. It is a ratio rather than an
    /// absolute wall thickness because the cut plane carries no unit information.
    /// </summary>
    internal const double CrossSectionWallFactor = 1.5;

    /// <summary>
    /// Clearance the socket bore must keep from the model's own surface below the cut, as a fraction
    /// of the bore radius. Without it a pin sunk into a part that necks in below the cut plane —
    /// perfectly comfortable in the cross-section — bores straight out through the side.
    /// </summary>
    internal const double SocketWallFactor = 0.2;

    /// <summary>Rings of sample points down the socket wall used for the break-out test.</summary>
    private const int SocketDepthSamples = 5;

    private const int SocketAngleSamples = 16;

    /// <summary>
    /// Works out where the pin goes and adds its two circles to <paramref name="splitMesh"/> as extra
    /// cross-section loops. Returns null when the pin cannot be built, with
    /// <paramref name="refusal"/> explaining why; the caller then leaves the mesh untouched.
    /// </summary>
    internal static RegistrationPinPlan? Plan(
        DMesh3 originalMesh,
        DMesh3 splitMesh,
        IReadOnlyList<IReadOnlyList<int>> capLoops,
        PlaneBasis basis,
        RegistrationPinOptions options,
        out RegistrationPinResult refusal)
    {
        double pegRadius = options.Diameter / 2.0;
        double clearance = options.Clearance;
        double depth = options.Depth ?? options.Diameter;

        if (!(options.Diameter > 0.0))
        {
            refusal = Refused(options, depth, 0.0, "Pin diameter must be greater than zero.");
            return null;
        }

        if (clearance < 0.0)
        {
            refusal = Refused(options, depth, 0.0, "Pin clearance cannot be negative.");
            return null;
        }

        if (!(depth > 0.0))
        {
            refusal = Refused(options, depth, 0.0, "Pin depth must be greater than zero.");
            return null;
        }

        double socketRadius = pegRadius + clearance;

        // The cross-section, projected into the cut plane. Parity across all loops decides what is
        // solid, exactly as it does for the cap itself: a Menger sponge's cut is dozens of separate
        // squares and holes inside squares, and a pin has to land in material rather than in one of
        // the holes.
        var rings = new List<List<Vector2d>>();
        foreach (IReadOnlyList<int> loop in capLoops)
        {
            var points = new List<Vector2d>(loop.Count);
            foreach (int vid in loop)
            {
                points.Add(basis.Project(splitMesh.GetVertex(vid)));
            }

            if (points.Count >= 3)
            {
                rings.Add(points);
            }
        }

        if (rings.Count == 0)
        {
            refusal = Refused(options, depth, 0.0, "The cut has no closed cross-section, so there is nowhere to put a pin.");
            return null;
        }

        Vector2d center2d;
        double roomAtCenter;
        if (options.Center is { } requested)
        {
            center2d = basis.Project(requested);
            roomAtCenter = SignedDistanceToRegion(rings, center2d);
        }
        else
        {
            (center2d, roomAtCenter) = LargestInscribedCircle(rings);
        }

        double largestFits = Math.Max(0.0, 2.0 * ((roomAtCenter / CrossSectionWallFactor) - clearance));
        double required = socketRadius * CrossSectionWallFactor;

        if (roomAtCenter <= 0.0)
        {
            refusal = Refused(
                options,
                depth,
                0.0,
                options.Center is null
                    ? "The cut cross-section is too thin to host a pin anywhere."
                    : "The requested pin position is not inside the cut cross-section.");
            return null;
        }

        if (roomAtCenter < required)
        {
            refusal = Refused(
                options,
                depth,
                largestFits,
                largestFits > 0.0
                    ? $"A Ø{options.Diameter:0.###} mm pin with {clearance:0.###} mm clearance does not fit the cut cross-section here — the largest that does is Ø{largestFits:0.###} mm. Mesh left unchanged."
                    : $"The cut cross-section is too thin to host a Ø{options.Diameter:0.###} mm pin with {clearance:0.###} mm clearance at any diameter. Mesh left unchanged.");
            return null;
        }

        Vector3d center3d = basis.Origin
            + (center2d.x * basis.Right)
            + (center2d.y * basis.Up);

        double socketDepth = depth + clearance;
        if (!SocketFitsInMaterial(originalMesh, center3d, basis.Normal, socketRadius, socketDepth))
        {
            refusal = Refused(
                options,
                depth,
                largestFits,
                $"A {socketDepth:0.###} mm deep socket would break out through the model's own wall below the cut. Mesh left unchanged.");
            return null;
        }

        var pegLoop = AppendCircle(splitMesh, center3d, basis, pegRadius);
        var socketLoop = AppendCircle(splitMesh, center3d, basis, socketRadius);

        refusal = null!;
        return new RegistrationPinPlan
        {
            Center = center3d,
            Axis = basis.Normal,
            PegRadius = pegRadius,
            SocketRadius = socketRadius,
            PegDepth = depth,
            SocketDepth = socketDepth,
            PegLoop = pegLoop,
            SocketLoop = socketLoop,
            Options = options,
            LargestDiameterThatFits = largestFits,
        };
    }

    private static RegistrationPinResult Refused(RegistrationPinOptions options, double depth, double largestFits, string message) =>
        new(
            PinPlaced: false,
            DiameterRequested: options.Diameter,
            PegDiameterAchieved: 0.0,
            SocketDiameterAchieved: 0.0,
            ClearanceRequested: options.Clearance,
            ClearanceAchieved: 0.0,
            DepthRequested: depth,
            DepthAchieved: 0.0,
            Center: Vector3d.Zero,
            Axis: Vector3d.Zero,
            LargestDiameterThatFits: largestFits,
            Message: message);

    /// <summary>
    /// Appends a circle of <paramref name="radius"/> around <paramref name="center"/> in the cut
    /// plane, wound counter-clockwise in <paramref name="basis"/>, and returns its vertex ids.
    /// </summary>
    private static List<int> AppendCircle(DMesh3 mesh, Vector3d center, PlaneBasis basis, double radius)
    {
        var ids = new List<int>(CircleSegments);
        for (int i = 0; i < CircleSegments; i++)
        {
            double angle = 2.0 * Math.PI * i / CircleSegments;
            Vector3d p = center
                + (radius * Math.Cos(angle) * basis.Right)
                + (radius * Math.Sin(angle) * basis.Up);
            ids.Add(mesh.AppendVertex(p));
        }

        return ids;
    }

    /// <summary>
    /// The point of the cross-section furthest from any of its edges, and that distance — the centre
    /// and radius of the largest circle that fits inside the material.
    ///
    /// <para>This is where automatic placement puts the pin, because it is the placement with the
    /// most wall left around the bore, and because it is defined for a cross-section of any shape.
    /// A cut through a Menger sponge produces dozens of disjoint filled squares with holes inside
    /// them; the centroid of "the" cross-section is meaningless there and lands in fresh air, while
    /// the largest inscribed circle lands in the middle of the thickest piece of material.</para>
    ///
    /// <para>Found by the standard "pole of inaccessibility" search: cover the bounding box with
    /// cells, and repeatedly subdivide whichever cell could still contain a better answer than the
    /// best one found so far. A cell of half-size <c>h</c> centred at <c>c</c> cannot beat
    /// <c>d(c) + h√2</c>, which is what makes the search exhaustive rather than a sampling
    /// heuristic.</para>
    /// </summary>
    internal static (Vector2d Center, double Radius) LargestInscribedCircle(List<List<Vector2d>> rings)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (List<Vector2d> ring in rings)
        {
            foreach (Vector2d p in ring)
            {
                minX = Math.Min(minX, p.x);
                minY = Math.Min(minY, p.y);
                maxX = Math.Max(maxX, p.x);
                maxY = Math.Max(maxY, p.y);
            }
        }

        double width = maxX - minX;
        double height = maxY - minY;
        double cellSize = Math.Min(width, height);
        if (!(cellSize > 0.0))
        {
            return (new Vector2d(minX, minY), 0.0);
        }

        double half = cellSize / 2.0;
        Vector2d bestCenter = new Vector2d(minX + (width / 2.0), minY + (height / 2.0));
        double bestDistance = SignedDistanceToRegion(rings, bestCenter);

        // Min-heap keyed on the negated upper bound, so the most promising cell comes out first.
        var queue = new PriorityQueue<(Vector2d Center, double Half, double Distance), double>();
        void Push(Vector2d center, double h)
        {
            double distance = SignedDistanceToRegion(rings, center);
            if (distance > bestDistance)
            {
                bestDistance = distance;
                bestCenter = center;
            }

            queue.Enqueue((center, h, distance), -(distance + (h * Math.Sqrt(2.0))));
        }

        for (double x = minX; x < maxX; x += cellSize)
        {
            for (double y = minY; y < maxY; y += cellSize)
            {
                Push(new Vector2d(x + half, y + half), half);
            }
        }

        double precision = cellSize * 1e-4;
        int guard = 200_000;
        while (queue.Count > 0 && guard-- > 0)
        {
            (Vector2d center, double h, double distance) = queue.Dequeue();
            if (distance + (h * Math.Sqrt(2.0)) - bestDistance <= precision)
            {
                continue;
            }

            double quarter = h / 2.0;
            Push(new Vector2d(center.x - quarter, center.y - quarter), quarter);
            Push(new Vector2d(center.x + quarter, center.y - quarter), quarter);
            Push(new Vector2d(center.x - quarter, center.y + quarter), quarter);
            Push(new Vector2d(center.x + quarter, center.y + quarter), quarter);
        }

        return (bestCenter, Math.Max(0.0, bestDistance));
    }

    /// <summary>
    /// Distance from <paramref name="point"/> to the nearest cross-section edge, positive inside the
    /// material and negative outside it. Inside-ness is the same even-odd parity rule the cap
    /// triangulation uses, so a pin can never be placed in a region the cap left open.
    /// </summary>
    internal static double SignedDistanceToRegion(List<List<Vector2d>> rings, Vector2d point)
    {
        double nearest = double.MaxValue;
        bool inside = false;

        foreach (List<Vector2d> ring in rings)
        {
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                Vector2d a = ring[j];
                Vector2d b = ring[i];
                nearest = Math.Min(nearest, DistanceToSegment(point, a, b));

                if ((b.y > point.y) != (a.y > point.y))
                {
                    double x = b.x + ((point.y - b.y) / (a.y - b.y) * (a.x - b.x));
                    if (x > point.x)
                    {
                        inside = !inside;
                    }
                }
            }
        }

        return inside ? nearest : -nearest;
    }

    private static double DistanceToSegment(Vector2d p, Vector2d a, Vector2d b)
    {
        Vector2d ab = b - a;
        double lengthSquared = ab.LengthSquared;
        if (lengthSquared < 1e-24)
        {
            return p.Distance(a);
        }

        double t = Math.Clamp((p - a).Dot(ab) / lengthSquared, 0.0, 1.0);
        return p.Distance(a + (t * ab));
    }

    /// <summary>
    /// True when a bore of <paramref name="radius"/> sunk <paramref name="depth"/> into
    /// <paramref name="mesh"/> from <paramref name="center"/> stays inside the material with a wall
    /// left around it. The test is against the <i>original</i> mesh rather than the cut half, which
    /// is the same solid below the plane and is available before either half has been built.
    /// </summary>
    private static bool SocketFitsInMaterial(DMesh3 mesh, Vector3d center, Vector3d normal, double radius, double depth)
    {
        double margin = radius * SocketWallFactor;
        var tree = new DMeshAABBTree3(mesh, autoBuild: true);
        bool closed = mesh.IsClosed();
        PlaneBasis basis = PlaneBasis.Create(center, normal);

        for (int d = 1; d <= SocketDepthSamples; d++)
        {
            double t = depth * d / SocketDepthSamples;
            Vector3d axisPoint = center - (t * normal);
            for (int a = 0; a < SocketAngleSamples; a++)
            {
                double angle = 2.0 * Math.PI * a / SocketAngleSamples;
                Vector3d p = axisPoint
                    + (radius * Math.Cos(angle) * basis.Right)
                    + (radius * Math.Sin(angle) * basis.Up);
                if (!HasMaterialAround(tree, closed, p, margin))
                {
                    return false;
                }
            }

            if (!HasMaterialAround(tree, closed, axisPoint, margin))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasMaterialAround(DMeshAABBTree3 tree, bool closed, Vector3d point, double margin)
    {
        if (closed && !tree.IsInside(point))
        {
            return false;
        }

        tree.FindNearestTriangle(point, out double nearestSquared);
        return nearestSquared >= margin * margin;
    }

    /// <summary>
    /// Builds the peg: a cylinder wall from <paramref name="ringIds"/> — the cap's pin circle, which
    /// the cap triangulation has already punched out — protruding <paramref name="depth"/> against
    /// the plane normal, closed by a flat end disc. Returns the far ring and the end disc's centre so
    /// the result's diameter and depth can be measured off the finished geometry.
    /// </summary>
    internal static (int[] FarRing, int EndCenter) AppendPeg(DMesh3 mesh, IReadOnlyList<int> ringIds, Vector3d axis, double depth)
    {
        int n = ringIds.Count;
        Vector3d centroid = Vector3d.Zero;
        for (int i = 0; i < n; i++)
        {
            centroid += mesh.GetVertex(ringIds[i]);
        }

        centroid /= n;

        var far = new int[n];
        for (int i = 0; i < n; i++)
        {
            far[i] = mesh.AppendVertex(mesh.GetVertex(ringIds[i]) - (depth * axis));
        }

        int endCenter = mesh.AppendVertex(centroid - (depth * axis));

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;

            // Wall, wound so its normal points radially outwards: the peg is solid, so material is
            // inside the cylinder.
            mesh.AppendTriangle(ringIds[i], far[i], ringIds[j]);
            mesh.AppendTriangle(ringIds[j], far[i], far[j]);

            // End disc, facing along -axis, away from the half it grows out of.
            mesh.AppendTriangle(endCenter, far[j], far[i]);
        }

        return (far, endCenter);
    }

    /// <summary>
    /// Builds the socket: the same construction inverted. The wall's normals point in towards the
    /// axis and the end disc faces back along the normal, because here the material is <i>outside</i>
    /// the cylinder and the cylinder is the void the peg drops into.
    /// </summary>
    internal static (int[] FarRing, int EndCenter) AppendSocket(DMesh3 mesh, IReadOnlyList<int> ringIds, Vector3d axis, double depth)
    {
        int n = ringIds.Count;
        Vector3d centroid = Vector3d.Zero;
        for (int i = 0; i < n; i++)
        {
            centroid += mesh.GetVertex(ringIds[i]);
        }

        centroid /= n;

        var far = new int[n];
        for (int i = 0; i < n; i++)
        {
            far[i] = mesh.AppendVertex(mesh.GetVertex(ringIds[i]) - (depth * axis));
        }

        int endCenter = mesh.AppendVertex(centroid - (depth * axis));

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            mesh.AppendTriangle(ringIds[i], ringIds[j], far[i]);
            mesh.AppendTriangle(ringIds[j], far[j], far[i]);
            mesh.AppendTriangle(endCenter, far[i], far[j]);
        }

        return (far, endCenter);
    }

    /// <summary>
    /// Mean distance of <paramref name="ringIds"/> from the pin axis, read out of
    /// <paramref name="mesh"/> after the fact. This is a measurement of the geometry that exists, not
    /// a restatement of the request: a basis, winding or offset error moves these vertices and the
    /// number changes with them.
    /// </summary>
    internal static double MeasureRingRadius(DMesh3 mesh, IReadOnlyList<int> ringIds, Vector3d center, Vector3d axis)
    {
        if (ringIds.Count == 0)
        {
            return 0.0;
        }

        double total = 0.0;
        foreach (int vid in ringIds)
        {
            Vector3d offset = mesh.GetVertex(vid) - center;
            total += (offset - (offset.Dot(axis) * axis)).Length;
        }

        return total / ringIds.Count;
    }

    /// <summary>Distance of <paramref name="vid"/> from the cut plane, along the pin axis.</summary>
    internal static double MeasureAxialDepth(DMesh3 mesh, int vid, Vector3d center, Vector3d axis) =>
        Math.Abs((mesh.GetVertex(vid) - center).Dot(axis));
}
