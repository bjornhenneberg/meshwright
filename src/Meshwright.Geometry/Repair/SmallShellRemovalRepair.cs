using g3;

namespace Meshwright.Geometry.Repair;

/// <summary>
/// Removes disconnected shells that are small relative to the mesh's total volume, e.g. floating
/// debris left over from a bad export. Companion repair to
/// <see cref="Diagnostics.DisconnectedShellDetector"/>: uses the same connected-components-plus
/// signed-volume approach, so "small" here means the same thing as the stray-shell percentage that
/// detector already reports.
/// </summary>
public sealed class SmallShellRemovalRepair
{
    /// <summary>
    /// Keeps the single largest-by-volume shell unconditionally, and removes every other shell
    /// whose volume is strictly less than <paramref name="minVolumeFraction"/> of the mesh's total
    /// volume (summed across all shells). No-op if the mesh has one shell or nothing qualifies.
    /// </summary>
    public SmallShellRemovalResult RemoveShellsBelowVolumeFraction(DMesh3 mesh, double minVolumeFraction)
    {
        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();

        if (components.Components.Count <= 1)
        {
            return new SmallShellRemovalResult(0, 0);
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

        int shellsRemoved = 0;
        int trianglesRemoved = 0;

        for (int i = 0; i < components.Components.Count; i++)
        {
            if (i == largest)
            {
                continue;
            }

            bool isSmall = totalVolume > 0.0 && shellVolumes[i] < minVolumeFraction * totalVolume;
            if (!isSmall)
            {
                continue;
            }

            int[] triangleIds = components.Components[i].Indices;
            foreach (int tid in triangleIds)
            {
                mesh.RemoveTriangle(tid);
            }

            shellsRemoved++;
            trianglesRemoved += triangleIds.Length;
        }

        return new SmallShellRemovalResult(shellsRemoved, trianglesRemoved);
    }
}

/// <summary>Outcome of <see cref="SmallShellRemovalRepair.RemoveShellsBelowVolumeFraction"/>.</summary>
public readonly record struct SmallShellRemovalResult(int ShellsRemoved, int TrianglesRemoved);
