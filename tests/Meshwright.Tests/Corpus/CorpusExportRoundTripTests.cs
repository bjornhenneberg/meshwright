using g3;
using Meshwright.IO;
using Meshwright.IO.Stl;
using Meshwright.IO.Wavefront;
using Xunit;
using Xunit.Abstractions;

namespace Meshwright.Tests.Corpus;

/// <summary>
/// Exercises <see cref="MeshExporter"/> (and the writers behind it) against the M4-1 real-world
/// corpus (<c>tests/corpus/files</c>, fetched via <c>scripts/fetch-corpus.sh</c>; see
/// reports/M4/CORPUS.md). With the corpus absent this passes trivially, matching
/// <see cref="CorpusSmokeTests"/>.
///
/// <para>
/// A bit-identical round trip is not the invariant to check: import now splits a mesh at
/// non-manifold junctions rather than dropping triangles (see <see cref="MeshImportResult"/>), and
/// STL is triangle-soup with no shared-vertex indexing, so a re-imported STL can legitimately have
/// a different vertex count than the mesh that was exported. What must hold, for any mesh already
/// loaded (and therefore already split) by the shipping importer, is that <em>exporting loses no
/// triangles</em>: both writers serialize every triangle currently in the mesh, and STL's
/// triangle-soup shape can only ever multiply shared vertices further, never merge non-manifold
/// geometry back together, so the reimported triangle count should never drop and should not grow
/// either (nothing on export introduces a new triangle).
/// </para>
/// </summary>
public class CorpusExportRoundTripTests
{
    private readonly ITestOutputHelper _output;

    public CorpusExportRoundTripTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EveryCorpusMesh_ExportsAndReimportsWithTheSameTriangleCount()
    {
        IReadOnlyList<string> files = CorpusPaths.Files;
        if (files.Count == 0)
        {
            _output.WriteLine("Corpus not fetched - run scripts/fetch-corpus.sh. Skipping.");
            return;
        }

        var failures = new List<string>();

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            DMesh3 original = LoadAny(path);

            CheckRoundTrip(name, original, ".stl", failures);
            CheckRoundTrip(name, original, ".obj", failures);
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} export round trips did not preserve triangle count:\n" + string.Join("\n", failures));
    }

    private static void CheckRoundTrip(string sourceName, DMesh3 original, string extension, List<string> failures)
    {
        try
        {
            using var stream = new MemoryStream();
            MeshExporter.Export(stream, original, "export" + extension);
            stream.Position = 0;

            MeshImportResult reimported = MeshImporter.ImportWithDiagnostics(stream, "reimport" + extension);

            if (reimported.TrianglesDropped != 0)
            {
                failures.Add($"{sourceName} ({extension}): {reimported.TrianglesDropped} triangles dropped on reimport");
            }

            if (reimported.Mesh.TriangleCount != original.TriangleCount)
            {
                failures.Add(
                    $"{sourceName} ({extension}): triangle count changed {original.TriangleCount} -> {reimported.Mesh.TriangleCount}");
            }
        }
        catch (Exception ex)
        {
            failures.Add($"{sourceName} ({extension}): {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static DMesh3 LoadAny(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => ObjReader.ReadFile(path),
            ".stl" => StlReader.ReadFile(path),
            var other => throw new NotSupportedException($"No importer for '{other}'."),
        };
}
