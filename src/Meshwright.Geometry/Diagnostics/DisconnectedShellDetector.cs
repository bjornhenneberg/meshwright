using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Flags shells that are disconnected from the mesh's single largest-by-volume
/// shell, e.g. small floating debris left over from a bad export.
/// </summary>
public sealed class DisconnectedShellDetector : IMeshDetector
{
    public string Category => "DisconnectedShell";

    public IReadOnlyList<MeshIssue> Detect(DMesh3 mesh)
    {
        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();

        if (components.Components.Count <= 1)
        {
            return Array.Empty<MeshIssue>();
        }

        var shellVolumes = new double[components.Components.Count];
        double totalVolume = 0.0;

        for (int i = 0; i < components.Components.Count; i++)
        {
            double volume = 0.0;
            foreach (int tid in components.Components[i].Indices)
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
        for (int i = 0; i < components.Components.Count; i++)
        {
            if (i == largest)
            {
                continue;
            }

            MeshConnectedComponents.Component component = components.Components[i];
            double percent = totalVolume > 0.0 ? (shellVolumes[i] / totalVolume) * 100.0 : 0.0;

            issues.Add(new MeshIssue(
                Category: Category,
                Severity: MeshIssueSeverity.Warning,
                Message: $"Stray disconnected shell ({component.Indices.Length} triangles, {percent:0.##}% of total volume)",
                TriangleIds: component.Indices));
        }

        return issues;
    }
}
