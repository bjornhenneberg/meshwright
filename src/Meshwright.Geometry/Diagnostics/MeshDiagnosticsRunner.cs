using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// Aggregates <see cref="MeshStatistics"/> and detector output into a single
/// <see cref="MeshDiagnosticsReport"/>. Does not own or discover the detector
/// list; callers supply it.
/// </summary>
public static class MeshDiagnosticsRunner
{
    /// <summary>
    /// Computes statistics for <paramref name="mesh"/> and runs each of
    /// <paramref name="detectors"/> in order, concatenating their issues.
    /// </summary>
    public static MeshDiagnosticsReport Run(DMesh3 mesh, IEnumerable<IMeshDetector> detectors)
    {
        MeshStatistics statistics = MeshStatistics.Compute(mesh);

        var issues = new List<MeshIssue>();
        foreach (IMeshDetector detector in detectors)
        {
            issues.AddRange(detector.Detect(mesh));
        }

        return new MeshDiagnosticsReport(statistics, issues);
    }
}
