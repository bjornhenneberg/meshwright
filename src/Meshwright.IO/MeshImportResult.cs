using g3;

namespace Meshwright.IO;

/// <summary>
/// An imported mesh, plus how much of the file could not be represented in it.
///
/// <para>
/// <see cref="DMesh3"/> is an indexed half-edge-style mesh and cannot represent a non-manifold
/// edge — one shared by more than two triangles — so <c>DMesh3.AppendTriangle</c> refuses such a
/// triangle and returns <c>NonManifoldID</c>. It likewise refuses a triangle with a repeated
/// corner. Both readers used to discard the return value, so importing silently dropped exactly
/// the geometry Meshwright exists to diagnose: across the M4-1 corpus of real print files, 14 of
/// 24 lost triangles this way and two lost about 73% of the mesh, after which every detector was
/// reporting on a mutilated remainder.
/// </para>
///
/// <para>
/// Representing that geometry properly is a change to the mesh data structure and out of scope
/// here. Reporting it is not: a user told "no problems found" about a mesh a quarter of which
/// failed to load has been actively misled, so the counts travel with the mesh and the UI surfaces
/// them.
/// </para>
/// </summary>
/// <param name="Mesh">The mesh as loaded.</param>
/// <param name="TrianglesInFile">Triangles present in the file.</param>
/// <param name="NonManifoldTrianglesDropped">Triangles refused because they would have created a non-manifold edge.</param>
/// <param name="DegenerateTrianglesDropped">Triangles refused for having a repeated corner (zero area by construction).</param>
public readonly record struct MeshImportResult(
    DMesh3 Mesh,
    int TrianglesInFile,
    int NonManifoldTrianglesDropped,
    int DegenerateTrianglesDropped)
{
    /// <summary>Total triangles in the file that are absent from <see cref="Mesh"/>.</summary>
    public int TrianglesDropped => NonManifoldTrianglesDropped + DegenerateTrianglesDropped;

    /// <summary>True when every triangle in the file made it into the mesh.</summary>
    public bool IsLossless => TrianglesDropped == 0;

    /// <summary>
    /// A plain-language warning for the UI, or null when the import was lossless. Phrased for the
    /// §5.1 "plain-language report" audience rather than in mesh-topology jargon.
    /// </summary>
    public string? Warning
    {
        get
        {
            if (IsLossless)
            {
                return null;
            }

            var parts = new List<string>(2);
            if (NonManifoldTrianglesDropped > 0)
            {
                parts.Add($"{NonManifoldTrianglesDropped:N0} where more than two faces met along one edge");
            }

            if (DegenerateTrianglesDropped > 0)
            {
                parts.Add($"{DegenerateTrianglesDropped:N0} with repeated corners");
            }

            double percent = TrianglesInFile > 0 ? 100.0 * TrianglesDropped / TrianglesInFile : 0.0;
            return $"{TrianglesDropped:N0} of {TrianglesInFile:N0} triangles ({percent:F1}%) could not be loaded: "
                + string.Join(", ", parts) + ".";
        }
    }
}
