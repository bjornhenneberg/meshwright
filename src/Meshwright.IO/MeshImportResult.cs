using g3;

namespace Meshwright.IO;

/// <summary>
/// An imported mesh, plus what the mesh representation had to do to hold the file.
///
/// <para>
/// <see cref="DMesh3"/> is an indexed, edge-based mesh: an edge belongs to at most two triangles,
/// so it cannot directly represent a non-manifold junction, and it needs three distinct vertex ids
/// per triangle. Real 3D-printing files contain both constantly. Rather than skip those triangles —
/// which silently lost up to 73% of a real corpus file, leaving every detector describing something
/// the user had not opened — the readers split the mesh at the offending vertices, so the geometry
/// is complete and only the connectivity is cut. See
/// <c>Meshwright.Geometry.Mesh.NonManifoldMeshBuilder</c>.
/// </para>
///
/// <para>
/// The split counts are therefore a description of the file, not a loss report: they say how much
/// of it sits on non-manifold junctions. The defects themselves are reported normally by Inspect,
/// so nothing here needs surfacing twice. <see cref="TrianglesDropped"/> is the genuine failure
/// case, expected to stay zero.
/// </para>
/// </summary>
/// <param name="Mesh">The mesh as loaded.</param>
/// <param name="TrianglesInFile">Triangles present in the file.</param>
/// <param name="NonManifoldTrianglesSplit">Triangles kept by duplicating a vertex because they sat on a non-manifold edge.</param>
/// <param name="DegenerateTrianglesSplit">Zero-area triangles kept by re-separating corners that had welded together.</param>
/// <param name="TrianglesDropped">Triangles that could not be represented at all. Expected to be zero.</param>
public readonly record struct MeshImportResult(
    DMesh3 Mesh,
    int TrianglesInFile,
    int NonManifoldTrianglesSplit,
    int DegenerateTrianglesSplit,
    int TrianglesDropped)
{
    /// <summary>True when every triangle in the file is present in <see cref="Mesh"/>.</summary>
    public bool IsComplete => TrianglesDropped == 0;

    /// <summary>Triangles that needed the mesh split to be representable.</summary>
    public int TrianglesSplit => NonManifoldTrianglesSplit + DegenerateTrianglesSplit;

    /// <summary>
    /// A plain-language warning when geometry was actually lost, or null. Splitting is not a
    /// warning — nothing is missing and Inspect reports the underlying defects itself — so this
    /// stays null for any normal import and exists to make a genuine regression loud.
    /// </summary>
    public string? Warning => IsComplete
        ? null
        : $"{TrianglesDropped:N0} of {TrianglesInFile:N0} triangles could not be loaded at all. "
          + "This is a bug in Meshwright, not a defect in the file — please report it.";
}
