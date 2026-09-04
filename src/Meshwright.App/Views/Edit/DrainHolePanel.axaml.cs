using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;

namespace Meshwright.App.Views.Edit;

public partial class DrainHolePanel : UserControl
{
    private MeshDocument? _document;
    private DrainHoleGizmo? _gizmo;
    private readonly ObservableCollection<string> _holesDisplay = new();
    private bool _gizmoActive;
    private Action? _gizmoActivationCallback;
    private Action? _gizmoDeactivationCallback;

    public DrainHolePanel()
    {
        InitializeComponent();
        HolesList.ItemsSource = _holesDisplay;
    }

    /// <summary>
    /// Sets the mesh document this panel operates on, exposed for testing.
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
    public void SetGizmo(DrainHoleGizmo gizmo)
    {
        _gizmo = gizmo;
        _gizmo.HolePlaced += (s, e) => UpdateHolesList();
        _gizmo.HoleRemoved += (s, e) => UpdateHolesList();
        UpdateHolesList();
    }

    /// <summary>
    /// Sets callbacks to activate/deactivate the gizmo on the viewport when the user
    /// clicks the placement button.
    /// </summary>
    public void SetGizmoActivationCallback(Action? onActivate, Action? onDeactivate)
    {
        _gizmoActivationCallback = onActivate;
        _gizmoDeactivationCallback = onDeactivate;
    }

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultMessageText?.Text;

    private void UpdateStatsDisplay()
    {
        if (_document?.Mesh is null)
        {
            StatsText.Text = "(No mesh loaded)";
            return;
        }

        var stats = MeshStatistics.Compute(_document.Mesh);
        StatsText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "Triangles: {0}\nVolume: {1:0.###}",
            stats.TriangleCount,
            stats.Volume);
    }

    private void UpdateHolesList()
    {
        _holesDisplay.Clear();
        if (_gizmo is null)
        {
            return;
        }

        int index = 1;
        foreach (var hole in _gizmo.Holes)
        {
            string entry = string.Format(
                CultureInfo.InvariantCulture,
                "Hole {0}: Ø{1:0.##}mm @ ({2:0.#}, {3:0.#}, {4:0.#})",
                index++,
                hole.Diameter,
                hole.SurfacePoint.x,
                hole.SurfacePoint.y,
                hole.SurfacePoint.z);
            _holesDisplay.Add(entry);
        }
    }

    private void OnActivateGizmoClick(object? sender, RoutedEventArgs e)
    {
        if (_gizmo is null)
        {
            ResultMessageText.Text = "Gizmo not set up. Cannot activate.";
            return;
        }

        if (!_gizmoActive)
        {
            _gizmoActive = true;
            ActivateGizmoButton.Content = "Done placing holes";
            GizmoStatusText.Text = "Click on the mesh surface to place drain holes.";
            _gizmoActivationCallback?.Invoke();
        }
        else
        {
            _gizmoActive = false;
            ActivateGizmoButton.Content = "Place holes with gizmo";
            GizmoStatusText.Text = "";
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
        ActivateGizmoButton.Content = "Place holes with gizmo";
        GizmoStatusText.Text = "";
    }

    private void OnHoleSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (HolesList.SelectedIndex >= 0 && _gizmo is not null && _gizmo.Holes.Count > HolesList.SelectedIndex)
        {
            var selectedHole = _gizmo.Holes[HolesList.SelectedIndex];
            _gizmo.SelectedHoleId = selectedHole.Id;
        }
    }

    private void OnApplySelectedClick(object? sender, RoutedEventArgs e)
    {
        if (_document is null || _gizmo is null || _gizmo.SelectedHoleId is null)
        {
            ResultMessageText.Text = "No hole selected or no mesh loaded.";
            return;
        }

        if (!double.TryParse(DiameterInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double diameter))
        {
            ResultMessageText.Text = "Invalid diameter value.";
            return;
        }

        if (!double.TryParse(CountersinkInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double countersink))
        {
            ResultMessageText.Text = "Invalid countersink depth value.";
            return;
        }

        var selectedHole = _gizmo.Holes.FirstOrDefault(h => h.Id == _gizmo.SelectedHoleId);
        if (selectedHole is null)
        {
            ResultMessageText.Text = "Selected hole not found.";
            return;
        }

        try
        {
            // Update hole parameters from UI
            selectedHole.Diameter = diameter;
            selectedHole.CountersinkDepth = countersink;

            var operation = new PlaceDrainHoleOperation(
                selectedHole.SurfacePoint,
                selectedHole.SurfaceNormal,
                diameter,
                countersink);

            OperationResult result = _document.Apply(operation);
            UpdateStatsDisplay();

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

    private void OnApplyAllClick(object? sender, RoutedEventArgs e)
    {
        if (_document is null || _gizmo is null || _gizmo.Holes.Count == 0)
        {
            ResultMessageText.Text = "No holes to apply or no mesh loaded.";
            return;
        }

        if (!double.TryParse(DiameterInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double diameter))
        {
            ResultMessageText.Text = "Invalid diameter value.";
            return;
        }

        if (!double.TryParse(CountersinkInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double countersink))
        {
            ResultMessageText.Text = "Invalid countersink depth value.";
            return;
        }

        try
        {
            int appliedCount = 0;
            string lastMessage = "";

            foreach (var hole in _gizmo.Holes)
            {
                hole.Diameter = diameter;
                hole.CountersinkDepth = countersink;

                var operation = new PlaceDrainHoleOperation(
                    hole.SurfacePoint,
                    hole.SurfaceNormal,
                    diameter,
                    countersink);

                OperationResult result = _document.Apply(operation);
                if (result.Changed)
                {
                    appliedCount++;
                    lastMessage = result.Summary;
                }
            }

            UpdateStatsDisplay();

            if (ResultMessageText is not null)
            {
                if (appliedCount > 0)
                {
                    ResultMessageText.Text = $"Applied {appliedCount} drain hole(s). Last: {lastMessage}";
                }
                else
                {
                    ResultMessageText.Text = "No holes could be applied.";
                }
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

    private void OnClearAllClick(object? sender, RoutedEventArgs e)
    {
        if (_gizmo is null)
        {
            return;
        }

        _gizmo.ClearHoles();
        UpdateHolesList();
        ResultMessageText.Text = "All holes cleared.";
    }
}
