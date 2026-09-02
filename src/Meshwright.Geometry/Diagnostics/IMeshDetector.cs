using g3;

namespace Meshwright.Geometry.Diagnostics;

/// <summary>
/// A single mesh-quality check. Each detector implementation owns one
/// <see cref="Category"/> and is built independently of the others.
/// </summary>
public interface IMeshDetector
{
    /// <summary>Machine-stable category name this detector reports issues under.</summary>
    string Category { get; }

    /// <summary>Runs the check against <paramref name="mesh"/> and returns any issues found.</summary>
    IReadOnlyList<MeshIssue> Detect(DMesh3 mesh);
}
