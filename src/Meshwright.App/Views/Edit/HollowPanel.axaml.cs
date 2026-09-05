using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;

namespace Meshwright.App.Views.Edit;

public partial class HollowPanel : UserControl
{
    private MeshDocument? _document;
    private HollowGizmo? _gizmo;
    private bool _gizmoActive;
    private Action? _gizmoActivationCallback;
    private Action? _gizmoDeactivationCallback;

    public HollowPanel()
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

    /// <summary>
    /// Sets the gizmo that this panel will control. Typically called by the integrating view
    /// to wire up the UI to the viewport gizmo.
    /// </summary>
    public void SetGizmo(HollowGizmo gizmo)
    {
        _gizmo = gizmo;
        _gizmo.Changed += (s, e) => UpdateGizmoStatusDisplay();
        UpdateGizmoStatusDisplay();
    }

    /// <summary>
    /// Sets callbacks to activate/deactivate the gizmo on the viewport when the user
    /// clicks the "Preview shell with gizmo" button.
    /// </summary>
    public void SetGizmoActivationCallback(Action? onActivate, Action? onDeactivate)
    {
        _gizmoActivationCallback = onActivate;
        _gizmoDeactivationCallback = onDeactivate;
    }

    /// <summary>Diagnostics report for the currently loaded mesh, exposed for testing.</summary>
    public MeshDiagnosticsReport? CurrentReport => _document?.Report;

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultMessageText?.Text;

    /// <summary>The in-flight Apply from the most recent click, exposed so tests can await real
    /// completion of an operation that now runs off the UI thread.</summary>
    public Task? PendingOperationForTesting { get; private set; }

    /// <summary>Whether the gizmo has been dragged and its wall thickness will win over the
    /// textbox on Apply, exposed for testing.</summary>
    public bool UsingGizmoValues => _gizmo?.WasTouched ?? false;

    private void UpdateStatsDisplay()
    {
        if (_document?.Mesh is null)
        {
            BeforeStats.Text = "(No mesh loaded)";
            AfterStats.Text = "(No mesh loaded)";
            return;
        }

        var stats = MeshStatistics.Compute(_document.Mesh);
        BeforeStats.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Triangles: {0}\nVolume: {1:0.###}",
            stats.TriangleCount,
            stats.Volume);
        AfterStats.Text = "(Not applied yet)";
    }

    private void UpdateGizmoStatusDisplay()
    {
        if (GizmoStatusText is null || _gizmo is null)
        {
            return;
        }

        GizmoStatusText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Wall thickness set via gizmo: {0:0.###}mm",
            _gizmo.WallThickness);
    }

    private void OnActivateGizmoClick(object? sender, RoutedEventArgs e)
    {
        if (_gizmo is null)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = "Gizmo not set up. Cannot activate.";
            }
            return;
        }

        if (!_gizmoActive)
        {
            _gizmoActive = true;
            ActivateGizmoButton.Content = "Done previewing shell";
            GizmoStatusText.Text = "Drag the inner marker to set wall thickness.";
            _gizmoActivationCallback?.Invoke();
        }
        else
        {
            _gizmoActive = false;
            ActivateGizmoButton.Content = "Preview shell with gizmo";
            UpdateGizmoStatusDisplay();
            _gizmoDeactivationCallback?.Invoke();
        }
    }

    /// <summary>
    /// Resets this panel's own "gizmo active" UI state without invoking the deactivation
    /// callback. For the integrating view (MainWindow) to call when it's handing the single
    /// viewport gizmo slot to a different panel - the panel whose gizmo is being displaced
    /// needs to know it's no longer active, but the callback loop (panel -> MainWindow ->
    /// Viewport.Gizmo) has already been handled by whoever is taking over.
    /// </summary>
    public void ForceDeactivateGizmo()
    {
        if (!_gizmoActive)
        {
            return;
        }

        _gizmoActive = false;
        ActivateGizmoButton.Content = "Preview shell with gizmo";
        UpdateGizmoStatusDisplay();
    }

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        Task task = OnApplyClickCore();
        PendingOperationForTesting = task;
        await task;
    }

    private async Task OnApplyClickCore()
    {
        if (_document is null)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = "No mesh loaded.";
            }
            return;
        }

        if (_document.Mesh is null)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = "No mesh loaded.";
            }
            return;
        }

        double wallThickness;
        if (_gizmo is not null && _gizmo.WasTouched)
        {
            // Gizmo-first: once the gizmo has been dragged, its value wins outright over
            // whatever is (possibly stale) in the textbox (SPECIFICATION.md §11, 2026-09-04).
            wallThickness = _gizmo.WallThickness;
        }
        else if (!double.TryParse(WallThicknessInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out wallThickness))
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = "Invalid wall thickness value.";
            }
            return;
        }

        try
        {
            var operation = new HollowOperation(wallThickness);

            // Captured before Apply: Apply raises MeshDocument.Changed once the operation
            // finishes, which MainWindow uses to refresh every panel's stats display from the
            // (now mutated) document, clobbering BeforeStats with post-operation figures.
            // Restoring the pre-operation snapshot below undoes that clobber.
            var statsBefore = MeshStatistics.Compute(_document.Mesh);

            OperationResult result = await _document.ApplyAsync(operation);

            var statsAfter = MeshStatistics.Compute(_document.Mesh);
            BeforeStats.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Triangles: {0}\nVolume: {1:0.###}",
                statsBefore.TriangleCount,
                statsBefore.Volume);
            AfterStats.Text = string.Format(
                CultureInfo.InvariantCulture,
                "Triangles: {0}\nVolume: {1:0.###}",
                statsAfter.TriangleCount,
                statsAfter.Volume);

            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = result.Summary;
            }
        }
        catch (Exception ex)
        {
            if (ResultMessageText is not null)
            {
                ResultMessageText.Text = $"Error: {ex.Message}";
            }
        }
    }
}
