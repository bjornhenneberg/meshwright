using g3;

namespace Meshwright.Geometry.Edit;

/// <summary>
/// Takes a mesh apart into its connected components — the "shells" the rest of the app talks
/// about (<see cref="Diagnostics.DisconnectedShellDetector"/>, <see
/// cref="Diagnostics.MeshStatistics.ShellCount"/>) — as independent meshes.
///
/// <para>
/// This exists because a shell is the unit a printer actually prints. A split model is one file
/// holding two solids, and both the export path that writes one file per part and the tests that
/// have to measure each half on its own need the same decomposition; deriving it twice is how the
/// two would come to disagree about what a part is.
/// </para>
/// </summary>
public static class MeshShells
{
    /// <summary>
    /// Returns one mesh per connected component, largest enclosed volume first, so "part 1" is the
    /// same part every time the same model is decomposed. A mesh with a single shell comes back as
    /// a one-element list holding a copy — never the input itself, so a caller cannot mutate the
    /// original by accident.
    /// </summary>
    public static IReadOnlyList<DMesh3> Separate(DMesh3 mesh)
    {
        var components = new MeshConnectedComponents(mesh);
        components.FindConnectedT();

        if (components.Components.Count <= 1)
        {
            return new[] { new DMesh3(mesh) };
        }

        var shells = new List<(DMesh3 Mesh, double Volume)>(components.Components.Count);
        foreach (MeshConnectedComponents.Component component in components.Components)
        {
            var submesh = new DSubmesh3(mesh, component.Indices);
            DMesh3 shell = submesh.SubMesh;
            shell.CompactInPlace();
            shells.Add((shell, EnclosedVolume(shell)));
        }

        return shells
            .OrderByDescending(entry => entry.Volume)
            .Select(entry => entry.Mesh)
            .ToArray();
    }

    private static double EnclosedVolume(DMesh3 mesh)
    {
        double volume = 0.0;
        foreach (int tid in mesh.TriangleIndices())
        {
            Index3i tri = mesh.GetTriangle(tid);
            volume += mesh.GetVertex(tri.a).Dot(mesh.GetVertex(tri.b).Cross(mesh.GetVertex(tri.c))) / 6.0;
        }

        return Math.Abs(volume);
    }
}
