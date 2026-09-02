using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Flags groups of distinct vertex ids that occupy the same (or near-identical)
/// position but were never welded into a single index.
/// </summary>
public sealed class DuplicateVertexDetector : IMeshDetector
{
    private const double Epsilon = 1e-9;

    public string Category => "DuplicateVertex";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        // Bucket vertices into a spatial grid keyed by position rounded to the
        // epsilon so coincident-but-distinct ids land in the same bucket,
        // avoiding an O(n^2) all-pairs comparison.
        var buckets = new Dictionary<(long, long, long), List<int>>();

        foreach (int vId in mesh.VertexIndices())
        {
            Vector3d v = mesh.GetVertex(vId);
            var key = CellKey(v);

            if (!buckets.TryGetValue(key, out List<int>? bucket))
            {
                bucket = new List<int>();
                buckets[key] = bucket;
            }

            bucket.Add(vId);
        }

        var issues = new List<MeshIssue>();
        var visited = new HashSet<int>();

        foreach (List<int> bucket in buckets.Values)
        {
            if (bucket.Count < 2)
            {
                continue;
            }

            foreach (int vId in bucket)
            {
                if (visited.Contains(vId))
                {
                    continue;
                }

                List<int> group = FindCoincidentGroup(mesh, bucket, vId, visited);
                if (group.Count < 2)
                {
                    continue;
                }

                issues.Add(new MeshIssue(
                    Category: Category,
                    Severity: MeshIssueSeverity.Warning,
                    Message: $"{group.Count} duplicate vertices at the same position",
                    VertexIds: group));
            }
        }

        return issues;
    }

    private static List<int> FindCoincidentGroup(DMesh3 mesh, List<int> bucket, int seedId, HashSet<int> visited)
    {
        Vector3d seed = mesh.GetVertex(seedId);
        var group = new List<int>();

        foreach (int candidateId in bucket)
        {
            if (visited.Contains(candidateId))
            {
                continue;
            }

            Vector3d candidate = mesh.GetVertex(candidateId);
            if (IsCoincident(seed, candidate))
            {
                group.Add(candidateId);
            }
        }

        foreach (int id in group)
        {
            visited.Add(id);
        }

        return group;
    }

    private static bool IsCoincident(Vector3d a, Vector3d b)
    {
        return Math.Abs(a.x - b.x) <= Epsilon
            && Math.Abs(a.y - b.y) <= Epsilon
            && Math.Abs(a.z - b.z) <= Epsilon;
    }

    private static (long, long, long) CellKey(Vector3d v)
    {
        // Cell size matches the epsilon so exact/near-exact duplicates always
        // share a cell; genuinely distinct nearby vertices may still land in
        // adjacent cells, but the pairwise epsilon check above is what
        // actually decides coincidence.
        return (Quantize(v.x), Quantize(v.y), Quantize(v.z));
    }

    private static long Quantize(double value)
    {
        return (long)Math.Round(value / Epsilon);
    }
}
