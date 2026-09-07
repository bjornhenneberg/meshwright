using g3;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.IO.Stl;
using Meshwright.IO.Wavefront;
using Meshwright.Tests.Edit;
using Xunit;
using Xunit.Abstractions;

namespace Meshwright.Tests.Corpus;

/// <summary>
/// Item 22 across real files rather than one fixture. A synthetic thin-walled slab shows the bug
/// exists; the corpus answers whether the fix generalises — the same question that showed 14 of the
/// 24 ground-truth files carried seam-only boundary loops during the hole-fill slice.
///
/// <para>
/// The corpus is not committed (third-party models under their own licences;
/// <c>scripts/fetch-corpus.sh</c> fetches them), so like every other corpus test this one passes
/// trivially where the files are absent, which includes CI.
/// </para>
/// </summary>
public class CorpusDecimateValidityTests
{
    private readonly ITestOutputHelper _output;

    public CorpusDecimateValidityTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Self-intersection detection is quadratic-ish in overlapping bounds, and a corpus scan runs it
    /// twice per file. Above this size a single file can take minutes, which is not a price the unit
    /// suite should pay on every run — the largest models are covered by hand instead (see the
    /// slice's report for the 139,989-triangle case).
    /// </summary>
    private const int TriangleCeiling = 20_000;

    [Fact]
    public void DecimatingACleanCorpusMesh_LeavesItClean()
    {
        IReadOnlyList<string> files = CorpusPaths.Files;
        if (files.Count == 0)
        {
            _output.WriteLine("Corpus not fetched - run scripts/fetch-corpus.sh. Skipping.");
            return;
        }

        var cleanBefore = new List<string>();
        var brokenByDecimation = new List<string>();
        var rows = new List<string>();

        foreach (string path in files)
        {
            string name = Path.GetFileName(path);

            DMesh3 mesh;
            try
            {
                mesh = LoadAny(path);
            }
            catch (Exception ex)
            {
                // Loading is CorpusSmokeTests' subject, not this test's.
                rows.Add($"{name,-30} skipped ({ex.GetType().Name})");
                continue;
            }

            if (mesh.TriangleCount is < 16 or > TriangleCeiling)
            {
                continue;
            }

            MeshDiagnosticsReport before = Diagnose(mesh);
            int beforeCount = mesh.TriangleCount;

            DecimateOperation.ToPercentage(0.5).Apply(mesh);
            MeshDiagnosticsReport after = Diagnose(mesh);

            rows.Add($"{name,-30} {beforeCount,7} -> {mesh.TriangleCount,7} tris  defects {before.DefectCount,6} -> {after.DefectCount,6}");

            if (before.DefectCount != 0)
            {
                continue;
            }

            cleanBefore.Add(name);
            if (after.DefectCount != 0)
            {
                brokenByDecimation.Add($"{name}: {after.Summary}");
            }
        }

        foreach (string row in rows)
        {
            _output.WriteLine(row);
        }

        // The corpus must still contain the case this test is about. Without this, thinning the
        // corpus to files that all arrive with defects would leave the assertion below passing
        // while checking nothing at all.
        Assert.True(cleanBefore.Count > 0,
            $"no corpus file under {TriangleCeiling} triangles loads defect-free, so this test can no longer tell whether decimation preserves validity");

        Assert.True(brokenByDecimation.Count == 0,
            $"decimating to 50% introduced defects into {brokenByDecimation.Count} of {cleanBefore.Count} clean corpus meshes:\n"
            + string.Join("\n", brokenByDecimation));
    }

    private static MeshDiagnosticsReport Diagnose(DMesh3 mesh) =>
        MeshDiagnosticsRunner.Run(mesh, DecimateValidityTests.Detectors());

    private static DMesh3 LoadAny(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => ObjReader.ReadFile(path),
            ".stl" => StlReader.ReadFile(path),
            var other => throw new NotSupportedException($"No importer for '{other}'."),
        };
}
