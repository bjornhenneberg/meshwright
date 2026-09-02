using g3;

namespace Meshwright.Geometry.Repair;

/// <summary>Outcome of <see cref="RemoveDegenerateAndDuplicatesRepair.Run"/>.</summary>
public sealed record RemoveDegenerateAndDuplicatesResult(int MergedVertexCount, int RemovedTriangleCount);

/// <summary>
/// Implements §5.1's "remove degenerate triangles and duplicate vertices": welds coincident
/// vertices into a single id, then drops any triangle that becomes (or already was) degenerate
/// as a result. Uses the same coincidence epsilon as <see cref="Diagnostics.DuplicateVertexDetector"/>
/// and the same area epsilon as <see cref="Diagnostics.DegenerateTriangleDetector"/>, so this
/// operation's notion of "duplicate"/"degenerate" matches what M1's diagnostics already flag.
/// </summary>
public static class RemoveDegenerateAndDuplicatesRepair
{
    private const double CoincidenceEpsilon = 1e-9;
    private const double AbsoluteAreaEpsilon = 1e-10;
    private const double RelativeAreaEpsilonFactor = 1e-6;

    /// <summary>Mutates <paramref name="mesh"/> in place (via rebuild + copy-back) and reports what changed.</summary>
    public static RemoveDegenerateAndDuplicatesResult Run(DMesh3 mesh)
    {
        Dictionary<int, int> canonical = BuildCanonicalVertexMap(mesh);
        int mergedVertexCount = mesh.VertexCount - canonical.Values.Distinct().Count();

        double areaEpsilon = Math.Max(
            AbsoluteAreaEpsilon,
            RelativeAreaEpsilonFactor * ComputeAverageEdgeLengthSquared(mesh));

        var rebuilt = new DMesh3();
        var newVertexId = new Dictionary<int, int>();
        int removedTriangleCount = 0;

        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            int ca = canonical[tri.a];
            int cb = canonical[tri.b];
            int cc = canonical[tri.c];

            // Welding two corners of a triangle to the same id zeroes its area exactly, even
            // when that area would otherwise sit above areaEpsilon (e.g. a huge, thin triangle).
            if (ca == cb || cb == cc || ca == cc)
            {
                removedTriangleCount++;
                continue;
            }

            Vector3d va = mesh.GetVertex(ca);
            Vector3d vb = mesh.GetVertex(cb);
            Vector3d vc = mesh.GetVertex(cc);
            double area = 0.5 * (vb - va).Cross(vc - va).Length;

            if (area < areaEpsilon)
            {
                removedTriangleCount++;
                continue;
            }

            int na = GetOrAddVertex(rebuilt, newVertexId, mesh, ca);
            int nb = GetOrAddVertex(rebuilt, newVertexId, mesh, cb);
            int nc = GetOrAddVertex(rebuilt, newVertexId, mesh, cc);
            rebuilt.AppendTriangle(na, nb, nc);
        }

        bool changed = mergedVertexCount > 0 || removedTriangleCount > 0;
        if (changed)
        {
            // Rebuilding into a fresh mesh (rather than editing in place) sidesteps g3Sharp's
            // in-place topology invariants around welding/removing triangles and vertices;
            // Copy() then overwrites the caller's instance so Execute still mutates in place.
            mesh.Copy(rebuilt);
        }

        return new RemoveDegenerateAndDuplicatesResult(mergedVertexCount, removedTriangleCount);
    }

    private static int GetOrAddVertex(DMesh3 target, Dictionary<int, int> newVertexId, DMesh3 source, int canonicalOriginalId)
    {
        if (newVertexId.TryGetValue(canonicalOriginalId, out int existing))
        {
            return existing;
        }

        int id = target.AppendVertex(source.GetVertex(canonicalOriginalId));
        newVertexId[canonicalOriginalId] = id;
        return id;
    }

    /// <summary>
    /// Maps every vertex id to a single representative id per coincident-position group, using
    /// the same spatial-bucket + pairwise-epsilon grouping as DuplicateVertexDetector so this
    /// operation welds exactly the vertices that detector would flag as duplicates.
    /// </summary>
    private static Dictionary<int, int> BuildCanonicalVertexMap(DMesh3 mesh)
    {
        var buckets = new Dictionary<(long, long, long), List<int>>();

        foreach (int vId in mesh.VertexIndices())
        {
            var key = CellKey(mesh.GetVertex(vId));

            if (!buckets.TryGetValue(key, out List<int>? bucket))
            {
                bucket = new List<int>();
                buckets[key] = bucket;
            }

            bucket.Add(vId);
        }

        var canonical = new Dictionary<int, int>();
        var visited = new HashSet<int>();

        foreach (List<int> bucket in buckets.Values)
        {
            foreach (int seedId in bucket)
            {
                if (visited.Contains(seedId))
                {
                    continue;
                }

                Vector3d seed = mesh.GetVertex(seedId);
                visited.Add(seedId);
                canonical[seedId] = seedId;

                foreach (int candidateId in bucket)
                {
                    if (candidateId == seedId || visited.Contains(candidateId))
                    {
                        continue;
                    }

                    if (IsCoincident(seed, mesh.GetVertex(candidateId)))
                    {
                        visited.Add(candidateId);
                        canonical[candidateId] = seedId;
                    }
                }
            }
        }

        return canonical;
    }

    private static bool IsCoincident(Vector3d a, Vector3d b)
    {
        return Math.Abs(a.x - b.x) <= CoincidenceEpsilon
            && Math.Abs(a.y - b.y) <= CoincidenceEpsilon
            && Math.Abs(a.z - b.z) <= CoincidenceEpsilon;
    }

    private static (long, long, long) CellKey(Vector3d v)
    {
        return (Quantize(v.x), Quantize(v.y), Quantize(v.z));
    }

    private static long Quantize(double value)
    {
        return (long)Math.Round(value / CoincidenceEpsilon);
    }

    private static double ComputeAverageEdgeLengthSquared(DMesh3 mesh)
    {
        double total = 0.0;
        int count = 0;

        foreach (int eid in mesh.EdgeIndices())
        {
            Index2i ev = mesh.GetEdgeV(eid);
            total += (mesh.GetVertex(ev.a) - mesh.GetVertex(ev.b)).LengthSquared;
            count++;
        }

        return count > 0 ? total / count : 1.0;
    }
}
