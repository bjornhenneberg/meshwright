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
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;

namespace Meshwright.App.Views.Edit;

public partial class PlaneCutPanel : UserControl
{
    private MeshDocument? _document;
    private PlaneCutGizmo? _gizmo;
    private Vector3d _currentPlanePoint = Vector3d.Zero;
    private Vector3d _currentPlaneNormal = Vector3d.AxisZ;
    private bool _gizmoActive;
    private Action? _gizmoActivationCallback;
    private Action? _gizmoDeactivationCallback;

    public PlaneCutPanel()
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
    public void SetGizmo(PlaneCutGizmo gizmo)
    {
        _gizmo = gizmo;
        _gizmo.Changed += (s, e) => UpdateGizmoStatusDisplay();
    }

    /// <summary>
    /// Sets callbacks to activate/deactivate the gizmo on the viewport when the user
    /// clicks the "Set Plane Via Gizmo" button.
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

    /// <summary>Current plane point, exposed for testing.</summary>
    public Vector3d CurrentPlanePoint => _currentPlanePoint;

    /// <summary>Current plane normal, exposed for testing.</summary>
    public Vector3d CurrentPlaneNormal => _currentPlaneNormal;

    /// <summary>Whether the gizmo has been dragged and its values will win over the textboxes
    /// on Apply, exposed for testing.</summary>
    public bool UsingGizmoValues => _gizmo?.WasTouched ?? false;

    /// <summary>The in-flight Apply from the most recent click, exposed so tests can await real
    /// completion of an operation that now runs off the UI thread.</summary>
    public Task? PendingOperationForTesting { get; private set; }

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
            "Plane set via gizmo: point ({0:0.##}, {1:0.##}, {2:0.##}), normal ({3:0.##}, {4:0.##}, {5:0.##})",
            _gizmo.PlanePosition.X,
            _gizmo.PlanePosition.Y,
            _gizmo.PlanePosition.Z,
            _gizmo.PlaneNormal.X,
            _gizmo.PlaneNormal.Y,
            _gizmo.PlaneNormal.Z);
    }

    private void OnSetViaGizmoClick(object? sender, RoutedEventArgs e)
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
            SetViaGizmoButton.Content = "Done setting plane";
            GizmoStatusText.Text = "Click and drag in the viewport to position and orient the plane.";
            _gizmoActivationCallback?.Invoke();
        }
        else
        {
            _gizmoActive = false;
            SetViaGizmoButton.Content = "Set Plane Via Gizmo";
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
        SetViaGizmoButton.Content = "Set Plane Via Gizmo";
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

        if (_gizmo is not null && _gizmo.WasTouched)
        {
            // Gizmo-first: once the gizmo has been dragged, its values win outright over
            // whatever is (possibly stale) in the textboxes.
            _currentPlanePoint = new Vector3d(_gizmo.PlanePosition.X, _gizmo.PlanePosition.Y, _gizmo.PlanePosition.Z);
            _currentPlaneNormal = new Vector3d(_gizmo.PlaneNormal.X, _gizmo.PlaneNormal.Y, _gizmo.PlaneNormal.Z).Normalized;
        }
        else
        {
            // Parse plane point
            if (!double.TryParse(PlanePointXInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double px) ||
                !double.TryParse(PlanePointYInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double py) ||
                !double.TryParse(PlanePointZInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double pz))
            {
                if (ResultMessageText is not null)
                {
                    ResultMessageText.Text = "Invalid plane point coordinates.";
                }
                return;
            }

            // Parse plane normal
            if (!double.TryParse(PlaneNormalXInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double nx) ||
                !double.TryParse(PlaneNormalYInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double ny) ||
                !double.TryParse(PlaneNormalZInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double nz))
            {
                if (ResultMessageText is not null)
                {
                    ResultMessageText.Text = "Invalid plane normal coordinates.";
                }
                return;
            }

            _currentPlanePoint = new Vector3d(px, py, pz);
            _currentPlaneNormal = new Vector3d(nx, ny, nz).Normalized;
        }

        // Get selected mode and cap mode
        int modeIndex = ModeComboBox?.SelectedIndex ?? 0;
        int capModeIndex = CapModeComboBox?.SelectedIndex ?? 1;
        bool addCap = AddCapCheckBox?.IsChecked ?? true;

        var cutMode = modeIndex switch
        {
            1 => CutMode.Discard,
            2 => CutMode.Split,
            _ => CutMode.Keep,
        };

        var capMode = capModeIndex switch
        {
            0 => HoleFillMode.Flat,
            2 => HoleFillMode.Smooth,
            _ => HoleFillMode.Planar,
        };

        try
        {
            IMeshOperation operation = cutMode switch
            {
                CutMode.Keep => new PlaneCutKeepSideOperation(_currentPlanePoint, _currentPlaneNormal, capMode),
                CutMode.Discard => new PlaneCutDiscardSideOperation(_currentPlanePoint, _currentPlaneNormal, capMode),
                CutMode.Split => new PlaneCutSplitOperation(_currentPlanePoint, _currentPlaneNormal, capMode),
                _ => new PlaneCutKeepSideOperation(_currentPlanePoint, _currentPlaneNormal, capMode),
            };

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
