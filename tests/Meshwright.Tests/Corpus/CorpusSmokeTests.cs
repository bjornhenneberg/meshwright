using System.Diagnostics;
using g3;
using Meshwright.Core;
using Meshwright.Geometry.Diagnostics;
using Meshwright.IO.Wavefront;
using Meshwright.IO.Stl;
using Xunit;
using Xunit.Abstractions;

namespace Meshwright.Tests.Corpus;

/// <summary>
/// M4-1's bar, per SPECIFICATION.md §7: crash-free on a real-world test corpus. Loads every mesh
/// under <c>tests/corpus/files</c> through the shipping import and diagnostics path and asserts
/// nothing throws, then prints a defect census.
///
/// <para>
/// The corpus is not committed. The files are third-party models under their own licences and the
/// repository should not redistribute them — <c>scripts/fetch-corpus.sh</c> downloads them from
/// their original homes instead, and <c>tests/corpus/manifest.tsv</c> records provenance, licence
/// and checksum for each. When the corpus is absent these tests pass trivially, so a clean
/// checkout and a CI run without it stay green rather than failing for a missing optional asset.
/// </para>
/// </summary>
public class CorpusSmokeTests
{
    private readonly ITestOutputHelper _output;

    public CorpusSmokeTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void EveryCorpusMesh_LoadsAndDiagnosesWithoutThrowing()
    {
        IReadOnlyList<string> files = CorpusPaths.Files;
        if (files.Count == 0)
        {
            _output.WriteLine("Corpus not fetched - run scripts/fetch-corpus.sh. Skipping.");
            return;
        }

        var failures = new List<string>();
        var rows = new List<string>();

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);
            try
            {
                var stopwatch = Stopwatch.StartNew();
                DMesh3 mesh = LoadAny(path);
                var document = new MeshDocument();
                document.Load(mesh);
                stopwatch.Stop();

                MeshDiagnosticsReport report = document.Report!;
                string categories = string.Join(" ", report.Issues
                    .GroupBy(issue => issue.Category)
                    .OrderBy(group => group.Key)
                    .Select(group => $"{group.Key}:{group.Count()}"));

                rows.Add($"{name,-26} {mesh.TriangleCount,8} tris {stopwatch.ElapsedMilliseconds,7}ms  {categories}");
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        foreach (string row in rows)
        {
            _output.WriteLine(row);
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} of {files.Count} corpus meshes failed to load or diagnose:\n" + string.Join("\n", failures));
    }

    private static DMesh3 LoadAny(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => ObjReader.ReadFile(path),
            ".stl" => StlReader.ReadFile(path),
            var other => throw new NotSupportedException($"No importer for '{other}'."),
        };
}

/// <summary>Locates the corpus directory by walking up from the test assembly to the repo root.</summary>
internal static class CorpusPaths
{
    internal static IReadOnlyList<string> Files { get; } = Discover();

    internal static string? Directory
    {
        get
        {
            var probe = new DirectoryInfo(AppContext.BaseDirectory);
            while (probe is not null)
            {
                string candidate = Path.Combine(probe.FullName, "tests", "corpus", "files");
                if (System.IO.Directory.Exists(candidate))
                {
                    return candidate;
                }

                probe = probe.Parent;
            }

            return null;
        }
    }

    private static IReadOnlyList<string> Discover()
    {
        string? dir = Directory;
        if (dir is null)
        {
            return Array.Empty<string>();
        }

        return System.IO.Directory
            .EnumerateFiles(dir)
            .Where(p => p.EndsWith(".obj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }
}
