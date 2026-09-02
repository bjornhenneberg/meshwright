namespace Meshwright.Core.Operations;

/// <summary>
/// Outcome of running an <see cref="IMeshOperation"/>: whether it changed the mesh and a
/// plain-language summary for the Auto Repair report (§5.1's "3 holes ... 14 flipped faces" style).
/// </summary>
public sealed record OperationResult(bool Changed, string Summary);
