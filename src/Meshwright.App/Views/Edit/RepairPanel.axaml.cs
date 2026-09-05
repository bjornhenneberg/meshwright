using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Repair;

namespace Meshwright.App.Views.Edit;

/// <summary>
/// The Inspect-then-Repair half of the app (§5.1): one-click Auto Repair plus each repair step
/// individually, for meshes where the default sequence does the wrong thing. Voxel remesh is kept
/// apart from the rest because it rebuilds the mesh wholesale rather than mending it.
/// </summary>
public partial class RepairPanel : UserControl
{
    private MeshDocument? _document;

    public RepairPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the mesh document this panel operates on, exposed for testing. Called by the
    /// integrating view (or test harness) to bind this panel to a live document.
    /// </summary>
    public void SetDocument(MeshDocument document)
    {
        _document = document;
        UpdateStatsDisplay();
    }

    /// <summary>Diagnostics report for the currently loaded mesh, exposed for testing.</summary>
    public MeshDiagnosticsReport? CurrentReport => _document?.Report;

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultMessageText?.Text;

    /// <summary>
    /// The in-flight Apply from the most recent button click, exposed so tests can await the
    /// real completion of an operation that now runs off the UI thread instead of asserting
    /// immediately after the click returns (which would race the background work).
    /// </summary>
    public Task? PendingOperationForTesting { get; private set; }

    private void UpdateStatsDisplay()
    {
        if (_document?.Mesh is null)
        {
            BeforeStats.Text = "(No mesh loaded)";
            AfterStats.Text = "(No mesh loaded)";
            return;
        }

        BeforeStats.Text = DescribeCurrentMesh();
        AfterStats.Text = "(Not applied yet)";
    }

    private string DescribeCurrentMesh()
    {
        if (_document?.Mesh is null)
        {
            return "(No mesh loaded)";
        }

        MeshStatistics stats = MeshStatistics.Compute(_document.Mesh);
        int issueCount = _document.Report?.Issues.Count ?? 0;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Triangles: {0}\nShells: {1}\nIssues: {2}",
            stats.TriangleCount,
            stats.ShellCount,
            issueCount);
    }

    private async void OnAutoRepairClick(object? sender, RoutedEventArgs e) =>
        await RunOperation(() => new AutoRepairPipeline());

    private async void OnRemoveDegenerateClick(object? sender, RoutedEventArgs e) =>
        await RunOperation(() => new RemoveDegenerateAndDuplicatesOperation());

    private async void OnResolveSelfIntersectionsClick(object? sender, RoutedEventArgs e) =>
        await RunOperation(() => new ResolveSelfIntersectionsOperation());

    private async void OnUnifyNormalsClick(object? sender, RoutedEventArgs e) =>
        await RunOperation(() => new UnifyNormalsOperation());

    private async void OnRemoveSmallShellsClick(object? sender, RoutedEventArgs e) =>
        await RunOperation(() =>
        {
            if (!double.TryParse(
                    MinVolumeFractionInput.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double minVolumeFraction))
            {
                throw new FormatException("Invalid minimum volume fraction.");
            }

            return new RemoveSmallShellsOperation(minVolumeFraction);
        });

    private async void OnFillHolesClick(object? sender, RoutedEventArgs e) =>
        await RunOperation(() => new FillHolesOperation(SelectedHoleFillMode()));

    private async void OnVoxelRemeshClick(object? sender, RoutedEventArgs e) =>
        await RunOperation(() =>
        {
            if (!int.TryParse(
                    VoxelResolutionInput.Text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int resolution))
            {
                throw new FormatException("Invalid voxel resolution.");
            }

            return new VoxelRemeshOperation(resolution);
        });

    private HoleFillMode SelectedHoleFillMode() => HoleFillModeCombo?.SelectedIndex switch
    {
        0 => HoleFillMode.Flat,
        2 => HoleFillMode.Smooth,
        _ => HoleFillMode.Planar,
    };

    /// <summary>
    /// Shared apply path for every button here: builds the operation (parameter parsing included,
    /// so a bad value is reported the same way a failed repair is), applies it through the
    /// document — off the UI thread, per §6.3/backlog item 13 — and reports the outcome. The
    /// document raises Changed once the (possibly long) operation finishes, which is what
    /// refreshes the viewport and diagnostics.
    /// </summary>
    private Task RunOperation(Func<IMeshOperation> buildOperation)
    {
        Task task = RunOperationCore(buildOperation);
        PendingOperationForTesting = task;
        return task;
    }

    private async Task RunOperationCore(Func<IMeshOperation> buildOperation)
    {
        if (_document?.Mesh is null)
        {
            SetResultMessage("No mesh loaded.");
            return;
        }

        try
        {
            IMeshOperation operation = buildOperation();

            // Captured before Apply: Apply raises MeshDocument.Changed once the operation
            // finishes, which MainWindow uses to refresh every panel's stats display from the
            // (now mutated) document, clobbering BeforeStats with post-operation figures.
            // Restoring the pre-operation snapshot below undoes that clobber.
            string before = DescribeCurrentMesh();

            OperationResult result = await _document.ApplyAsync(operation);

            BeforeStats.Text = before;
            AfterStats.Text = DescribeCurrentMesh();
            SetResultMessage(result.Changed
                ? result.Summary
                : $"{operation.Name}: nothing to change.");
        }
        catch (Exception ex)
        {
            SetResultMessage($"Error: {ex.Message}");
        }
    }

    private void SetResultMessage(string message)
    {
        if (ResultMessageText is not null)
        {
            ResultMessageText.Text = message;
        }
    }
}
