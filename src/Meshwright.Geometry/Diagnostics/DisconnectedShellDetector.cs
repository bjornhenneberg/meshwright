using g3;
using Meshwright.Geometry.Mesh;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Flags shells that are disconnected from the mesh's single largest-by-volume
/// shell, e.g. small floating debris left over from a bad export.
///
/// <para>
/// A shell only counts as debris if it is <em>small</em>. A file can legitimately hold several
/// substantial solids — most obviously one this app has just split for printing — and calling half
/// a model a "stray disconnected shell" is the app telling the user something untrue about geometry
/// it made itself (backlog item 26). Shells at or above <see cref="SubstantialVolumeFraction"/> of
/// the total are therefore reported under the <c>SeparatePart</c> category at
/// <see cref="MeshIssueSeverity.Info"/>: still listed, because a part a user did not expect is
/// worth knowing about, but not counted among the defects. The fraction is the same 1% that
/// <see cref="Repair.SmallShellRemovalRepair"/> defaults to, so what Auto Repair is willing to
/// delete and what Inspect is willing to call debris cannot drift apart.
/// </para>
///
/// <para>
/// Components are found by position rather than by vertex id (see <see cref="PositionTopology"/>):
/// a surface that <see cref="NonManifoldMeshBuilder"/> had to cut in order to represent
/// non-manifold geometry is still one shell, and reporting its pieces as floating debris would be
/// an artefact of the mesh structure rather than a defect in the file.
/// </para>
/// </summary>
public sealed class DisconnectedShellDetector : IMeshDetector
{
    /// <summary>Category for a shell too small to be anything but debris.</summary>
    public string Category => "DisconnectedShell";

    /// <summary>Category for a disconnected shell big enough to be a part in its own right.</summary>
    public const string SeparatePartCategory = "SeparatePart";

    /// <summary>
    /// Share of the mesh's total volume at or above which a disconnected shell is a part rather
    /// than debris. Matches <see cref="Repair.SmallShellRemovalRepair"/>'s default threshold.
    /// </summary>
    public const double SubstantialVolumeFraction = 0.01;

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        IReadOnlyList<List<int>> componentList = PositionTopology.ConnectedComponents(mesh);

        if (componentList.Count <= 1)
        {
            return Array.Empty<MeshIssue>();
        }

        var shellVolumes = new double[componentList.Count];
        double totalVolume = 0.0;

        for (int i = 0; i < componentList.Count; i++)
        {
            double volume = 0.0;
            foreach (int tid in componentList[i])
            {
                Index3i tri = mesh.GetTriangle(tid);
                Vector3d v0 = mesh.GetVertex(tri.a);
                Vector3d v1 = mesh.GetVertex(tri.b);
                Vector3d v2 = mesh.GetVertex(tri.c);

                volume += v0.Dot(v1.Cross(v2)) / 6.0;
            }

            volume = Math.Abs(volume);
            shellVolumes[i] = volume;
            totalVolume += volume;
        }

        int largest = 0;
        for (int i = 1; i < shellVolumes.Length; i++)
        {
            if (shellVolumes[i] > shellVolumes[largest])
            {
                largest = i;
            }
        }

        var issues = new List<MeshIssue>();
        for (int i = 0; i < componentList.Count; i++)
        {
            if (i == largest)
            {
                continue;
            }

            List<int> component = componentList[i];
            double fraction = totalVolume > 0.0 ? shellVolumes[i] / totalVolume : 0.0;
            double percent = fraction * 100.0;
            bool isPart = fraction >= SubstantialVolumeFraction;

            issues.Add(new MeshIssue(
                Category: isPart ? SeparatePartCategory : Category,
                Severity: isPart ? MeshIssueSeverity.Info : MeshIssueSeverity.Warning,
                Message: isPart
                    ? $"Separate part ({component.Count} triangles, {percent:0.##}% of total volume)"
                    : $"Stray disconnected shell ({component.Count} triangles, {percent:0.##}% of total volume)",
                TriangleIds: component.ToArray()));
        }

        return issues;
    }
}
