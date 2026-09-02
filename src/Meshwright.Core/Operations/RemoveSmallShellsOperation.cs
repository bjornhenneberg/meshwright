using g3;
using Meshwright.Geometry.Repair;

namespace Meshwright.Core.Operations;

/// <summary>
/// Removes disconnected shells that are small relative to the mesh's total volume (§5.1 "remove
/// small disconnected shells ... with size threshold slider"). Thin wrapper around
/// <see cref="SmallShellRemovalRepair"/> — the slider itself is a UI concern for a later
/// milestone; this just exposes the threshold as a constructor parameter so callers (UI, Auto
/// Repair pipeline, CLI) can configure it.
/// </summary>
public sealed class RemoveSmallShellsOperation : MeshOperationBase
{
    private const double DefaultMinVolumeFraction = 0.01;

    private readonly double _minVolumeFraction;
    private readonly SmallShellRemovalRepair _repair = new();

    public RemoveSmallShellsOperation(double minVolumeFraction = DefaultMinVolumeFraction)
    {
        _minVolumeFraction = minVolumeFraction;
    }

    public override string Name => "Remove Small Shells";

    protected override OperationResult Execute(DMesh3 mesh)
    {
        SmallShellRemovalResult result = _repair.RemoveShellsBelowVolumeFraction(mesh, _minVolumeFraction);

        if (result.ShellsRemoved == 0)
        {
            return new OperationResult(Changed: false, Summary: "No small disconnected shells found.");
        }

        string shellWord = result.ShellsRemoved == 1 ? "shell" : "shells";
        return new OperationResult(
            Changed: true,
            Summary: $"Removed {result.ShellsRemoved} small disconnected {shellWord} ({result.TrianglesRemoved} triangles).");
    }
}
