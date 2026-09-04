using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Edit;
using Meshwright.Geometry.Repair;

namespace Meshwright.App.Views.Edit;

public partial class PlaneCutPanel : UserControl
{
    private MeshDocument? _document;
    private Vector3d _currentPlanePoint = Vector3d.Zero;
    private Vector3d _currentPlaneNormal = Vector3d.AxisZ;

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

    /// <summary>Diagnostics report for the currently loaded mesh, exposed for testing.</summary>
    public MeshDiagnosticsReport? CurrentReport => _document?.Report;

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultMessageText?.Text;

    /// <summary>Current plane point, exposed for testing.</summary>
    public Vector3d CurrentPlanePoint => _currentPlanePoint;

    /// <summary>Current plane normal, exposed for testing.</summary>
    public Vector3d CurrentPlaneNormal => _currentPlaneNormal;

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

    private void OnSetViaGizmoClick(object? sender, RoutedEventArgs e)
    {
        if (ResultMessageText is not null)
        {
            ResultMessageText.Text = "Gizmo interaction not yet implemented in this batch.";
        }
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
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
                CutMode.Split => new PlaneCutKeepSideOperation(_currentPlanePoint, _currentPlaneNormal, capMode), // For now, just use Keep for Split mode
                _ => new PlaneCutKeepSideOperation(_currentPlanePoint, _currentPlaneNormal, capMode),
            };

            OperationResult result = _document.Apply(operation);

            var statsAfter = MeshStatistics.Compute(_document.Mesh);
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
