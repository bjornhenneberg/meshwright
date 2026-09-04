using g3;
using Meshwright.Core;
using Meshwright.IO;
using Xunit;
using Xunit.Abstractions;

namespace Meshwright.Tests.Corpus;

/// <summary>
/// Checks Meshwright's detectors against Thingi10K's independent analysis of the same files, which
/// the manifest records per file. This is what turns the corpus from a smoke test ("nothing threw")
/// into a regression corpus ("we agree with a second implementation about what is wrong").
///
/// <para>
/// Exact counts are deliberately <em>not</em> asserted. Two independent implementations legitimately
/// disagree on how to count the same defect — one self-intersection can be one face pair or many,
/// and a hole's size in edges depends on how the boundary is walked. What must hold is the
/// direction that actually harms a user: <b>a mesh the reference calls clean in some category must
/// not be reported as defective in that category</b>. False positives are the dangerous class for a
/// repair tool, because they push a user into "repairing" geometry that was fine.
/// </para>
///
/// <para>
/// Assertions are restricted to completely-imported meshes. Where <see cref="DMesh3"/> could not
/// hold part of the file (see <see cref="MeshImportResult"/>), the detectors are describing a
/// different mesh from the one Thingi10K measured, and comparing the two would be meaningless.
/// </para>
/// </summary>
public class CorpusGroundTruthTests
{
    private readonly ITestOutputHelper _output;

    public CorpusGroundTruthTests(ITestOutputHelper output) => _output = output;

    private sealed record Loaded(CorpusEntry Entry, MeshImportResult Import, IReadOnlyDictionary<string, int> Issues);

    private static List<Loaded> LoadAll()
    {
        var loaded = new List<Loaded>();
        string? dir = CorpusPaths.Directory;
        if (dir is null)
        {
            return loaded;
        }

        foreach (CorpusEntry entry in CorpusManifest.Entries.Where(e => e.GroundTruth.ContainsKey("faces")))
        {
            string path = Path.Combine(dir, entry.FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            MeshImportResult import = MeshImporter.ImportFileWithDiagnostics(path);
            var document = new MeshDocument();
            document.Load(import.Mesh);

            loaded.Add(new Loaded(
                entry,
                import,
                document.Report!.Issues.GroupBy(i => i.Category).ToDictionary(g => g.Key, g => g.Count())));
        }

        return loaded;
    }

    private static int Count(Loaded l, string category) => l.Issues.TryGetValue(category, out int v) ? v : 0;

    private static int Truth(Loaded l, string key) => l.Entry.GroundTruth.TryGetValue(key, out int v) ? v : 0;

    [Fact]
    public void ImportAccountsForEveryTriangleInTheFile()
    {
        List<Loaded> loaded = LoadAll();
        if (loaded.Count == 0)
        {
            _output.WriteLine("Corpus not fetched - run scripts/fetch-corpus.sh. Skipping.");
            return;
        }

        foreach (Loaded l in loaded)
        {
            // Internal consistency: nothing vanishes unaccounted for.
            Assert.Equal(l.Import.TrianglesInFile, l.Import.Mesh.TriangleCount + l.Import.TrianglesDropped);

            // External consistency: we read the same number of triangles the reference did, so a
            // parser bug cannot masquerade as a representation limit.
            Assert.Equal(Truth(l, "faces"), l.Import.TrianglesInFile);

            // And the point of splitting rather than skipping: every triangle in the file is in the
            // mesh. Two of these files used to lose ~73% of themselves here.
            Assert.True(l.Import.IsComplete, $"{l.Entry.FileName} dropped {l.Import.TrianglesDropped} triangles.");
            Assert.Equal(Truth(l, "faces"), l.Import.Mesh.TriangleCount);
        }
    }

    [Fact]
    public void CleanMeshesAreNotReportedAsDefective()
    {
        List<Loaded> loaded = LoadAll();
        if (loaded.Count == 0)
        {
            _output.WriteLine("Corpus not fetched - run scripts/fetch-corpus.sh. Skipping.");
            return;
        }

        var lossless = loaded.Where(l => l.Import.IsComplete).ToList();
        Assert.NotEmpty(lossless);

        var failures = new List<string>();

        foreach (Loaded l in lossless)
        {
            void MustBeSilent(bool referenceSaysClean, string category, string because)
            {
                if (referenceSaysClean && Count(l, category) > 0)
                {
                    failures.Add($"{l.Entry.FileName}: reference reports {because}, but {category} fired {Count(l, category)} times.");
                }
            }

            MustBeSilent(Truth(l, "self_int") == 0, "SelfIntersection", "no self-intersections");
            MustBeSilent(Truth(l, "boundary_edges") == 0, "BoundaryHole", "a closed surface (no boundary edges)");
            MustBeSilent(Truth(l, "components") == 1, "DisconnectedShell", "a single connected component");

            // Meshwright files bowtie vertices under the same category as non-manifold edges, so
            // both of the reference's manifoldness flags must be clean before silence is required.
            MustBeSilent(
                Truth(l, "edge_manifold") == 1 && Truth(l, "vertex_manifold") == 1,
                "NonManifoldEdge",
                "an edge- and vertex-manifold mesh");
        }

        Assert.True(failures.Count == 0,
            $"Detectors reported defects on geometry the reference calls clean ({lossless.Count} completely-imported files checked):\n"
            + string.Join("\n", failures));
    }

    [Fact]
    public void DefectsTheReferenceReportsAreNotMissedWholesale()
    {
        List<Loaded> loaded = LoadAll();
        if (loaded.Count == 0)
        {
            _output.WriteLine("Corpus not fetched - run scripts/fetch-corpus.sh. Skipping.");
            return;
        }

        // The weaker converse direction: exact counts differ between implementations, but a file
        // the reference calls badly broken in a category should not come back completely silent.
        // Thresholded well above the noise so ordinary counting differences do not fail the build.
        const int Substantial = 50;
        var failures = new List<string>();

        foreach (Loaded l in loaded.Where(l => l.Import.IsComplete))
        {
            if (Truth(l, "self_int") >= Substantial && Count(l, "SelfIntersection") == 0)
            {
                failures.Add($"{l.Entry.FileName}: reference reports {Truth(l, "self_int")} self-intersections, we found none.");
            }

            // An open surface may be a hole or a crack — two open edges at the same position, left
            // by near-coincident vertices. Meshwright reports the second as duplicate vertices
            // rather than as a hole, because that names the cause and points at the repair that
            // fixes it. The reference counts both as boundary edges, so either category satisfies
            // "we noticed this surface is not closed".
            if (Truth(l, "boundary_edges") >= Substantial
                && Count(l, "BoundaryHole") == 0
                && Count(l, "DuplicateVertex") == 0)
            {
                failures.Add(
                    $"{l.Entry.FileName}: reference reports {Truth(l, "boundary_edges")} boundary edges, "
                    + "we reported neither holes nor duplicate vertices.");
            }
        }

        Assert.True(failures.Count == 0, "Substantial defects missed entirely:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void ReportsTheImportLossItCannotRepresent()
    {
        List<Loaded> loaded = LoadAll();
        if (loaded.Count == 0)
        {
            _output.WriteLine("Corpus not fetched - run scripts/fetch-corpus.sh. Skipping.");
            return;
        }

        // DMesh3 cannot hold a non-manifold edge, so some real print files genuinely do not fit.
        // What matters is that the loss is never silent: every lossy import must produce a warning
        // naming the scale of it. This is the guard on the bug the ground-truth comparison found -
        // 14 of 24 real files were losing triangles with no indication at all.
        var lossy = loaded.Where(l => !l.Import.IsComplete).ToList();

        foreach (Loaded l in lossy)
        {
            Assert.False(string.IsNullOrWhiteSpace(l.Import.Warning));
            Assert.Contains(l.Import.TrianglesDropped.ToString("N0"), l.Import.Warning!);
            _output.WriteLine($"{l.Entry.FileName,-24} {l.Import.Warning}");
        }

        foreach (Loaded l in loaded.Where(l => l.Import.IsComplete))
        {
            Assert.Null(l.Import.Warning);
        }
    }
}
