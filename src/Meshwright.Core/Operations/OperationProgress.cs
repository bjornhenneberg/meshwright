namespace Meshwright.Core.Operations;

/// <summary>
/// A progress update from a long-running <see cref="IMeshOperation"/> (§6.3, §4's honest
/// diagnostics). <see cref="FractionComplete"/> is null when the operation cannot honestly
/// report a fraction — most operations are one opaque call into vendored or native geometry
/// code with no safe mid-algorithm checkpoint, so faking a percentage for them would be a lie
/// dressed up as a progress bar. Only an operation that is genuinely composed of discrete steps
/// (see <see cref="IProgressReportingMeshOperation"/>) sets it.
/// </summary>
public sealed record OperationProgress(string Description, double? FractionComplete);
