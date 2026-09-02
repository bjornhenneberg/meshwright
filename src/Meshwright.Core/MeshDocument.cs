using g3;
using Meshwright.Geometry.Diagnostics;

namespace Meshwright.Core;

/// <summary>
/// Holds the currently loaded mesh plus the <see cref="MeshDiagnosticsReport"/> computed for it.
/// Running diagnostics is the one thing every mesh load needs (§5.1 "Inspect"); this type keeps
/// that pairing in one place rather than scattering detector calls across UI code.
/// </summary>
public sealed class MeshDocument
{
    private static readonly IReadOnlyList<IMeshDetector> Detectors = new IMeshDetector[]
    {
        new NonManifoldDetector(),
        new BoundaryHoleDetector(),
        new SelfIntersectionDetector(),
        new InvertedNormalDetector(),
        new DegenerateTriangleDetector(),
        new DuplicateVertexDetector(),
        new DisconnectedShellDetector(),
    };

    public DMesh3? Mesh { get; private set; }

    public MeshDiagnosticsReport? Report { get; private set; }

    /// <summary>Sets <paramref name="mesh"/> as current and recomputes <see cref="Report"/> for it.</summary>
    public void Load(DMesh3 mesh)
    {
        Mesh = mesh;
        Report = MeshDiagnosticsRunner.Run(mesh, Detectors);
    }
}
