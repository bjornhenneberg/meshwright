using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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

        // The parameter fields drive the gizmo, not only the Apply button: a hole is placed at the
        // size in the box, the viewport marker is drawn at that size, and the Placed Holes list
        // names it. Before this, placement hard-coded 2mm and the list described a hole nobody had
        // asked for (SPECIFICATION.md §11, 2026-09-06).
        // Watching the Text property rather than the TextChanged routed event: the routed event only
        // reaches a control that is live in a visual tree, so a panel exercised outside a window (as
        // the wiring tests do) would silently never see it — the same "green suite, dead wiring" shape
        // of failure this change exists to remove.
        DiameterInput.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                PushParametersToGizmo();
            }
        };
        CountersinkInput.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                PushParametersToGizmo();
            }
        };
    }

    /// <summary>
    /// Copies the parameter fields into the gizmo and onto every placed hole, so one number governs
    /// what is placed, what is drawn and what Apply cuts.
    /// </summary>
    private void PushParametersToGizmo()
    {
        if (_gizmo is null)
        {
            return;
        }

        if (TryReadDiameter(out double diameter))
        {
            _gizmo.Diameter = diameter;
            foreach (var hole in _gizmo.Holes)
            {
                hole.Diameter = diameter;
            }
        }

        if (TryReadCountersink(out double countersink))
        {
            _gizmo.CountersinkDepth = countersink;
            foreach (var hole in _gizmo.Holes)
            {
                hole.CountersinkDepth = countersink;
            }
        }

        UpdateHolesList();
    }

    private bool TryReadDiameter(out double diameter) =>
        double.TryParse(DiameterInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out diameter)
        && diameter > 0.0;

    private bool TryReadCountersink(out double countersink) =>
        double.TryParse(CountersinkInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out countersink)
        && countersink >= 0.0;

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
        PushParametersToGizmo();
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

    /// <summary>Types a value into the Diameter field exactly as a user would, exposed for testing.
    /// It deliberately only sets the text: whether that reaches the gizmo is the thing under test.</summary>
    public void SetDiameterTextForTesting(string text) => DiameterInput.Text = text;

    /// <summary>Types a value into the Countersink Depth field, exposed for testing.</summary>
    public void SetCountersinkTextForTesting(string text) => CountersinkInput.Text = text;

    /// <summary>The Placed Holes list as the user sees it, exposed for testing.</summary>
    public IReadOnlyList<string> PlacedHoleEntriesForTesting => _holesDisplay;

    /// <summary>Runs "Apply to all holes" as the button does, exposed for testing.</summary>
    public Task InvokeApplyAllForTesting() => OnApplyAllClickCore();

    /// <summary>Runs "Apply to selected hole" as the button does, exposed for testing.</summary>
    public Task InvokeApplySelectedForTesting() => OnApplySelectedClickCore();

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultMessageText?.Text;

    /// <summary>The in-flight Apply from the most recent click, exposed so tests can await real
    /// completion of an operation that now runs off the UI thread.</summary>
    public Task? PendingOperationForTesting { get; private set; }

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
        // Rebuilding the list drops the ListBox's selection, and the selection is what "Apply to
        // selected hole" acts on — so typing in the Diameter box must not silently deselect the hole
        // the user is working on.
        int selected = HolesList.SelectedIndex;

        _holesDisplay.Clear();
        if (_gizmo is null)
        {
            return;
        }

        int index = 1;
        foreach (var hole in _gizmo.Holes)
        {
            string countersink = hole.CountersinkDepth > 0.0
                ? string.Format(CultureInfo.InvariantCulture, ", {0:0.##}mm countersink", hole.CountersinkDepth)
                : "";
            string entry = string.Format(
                CultureInfo.InvariantCulture,
                "Hole {0}: Ø{1:0.##}mm{2} @ ({3:0.#}, {4:0.#}, {5:0.#})",
                index++,
                hole.Diameter,
                countersink,
                hole.SurfacePoint.x,
                hole.SurfacePoint.y,
                hole.SurfacePoint.z);
            _holesDisplay.Add(entry);
        }

        if (selected >= 0 && selected < _holesDisplay.Count)
        {
            HolesList.SelectedIndex = selected;
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

    private async void OnApplySelectedClick(object? sender, RoutedEventArgs e)
    {
        Task task = OnApplySelectedClickCore();
        PendingOperationForTesting = task;
        await task;
    }

    private async Task OnApplySelectedClickCore()
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

            OperationResult result = await _document.ApplyAsync(operation);
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

    private async void OnApplyAllClick(object? sender, RoutedEventArgs e)
    {
        Task task = OnApplyAllClickCore();
        PendingOperationForTesting = task;
        await task;
    }

    private async Task OnApplyAllClickCore()
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
            var refusals = new List<string>();

            foreach (var hole in _gizmo.Holes)
            {
                hole.Diameter = diameter;
                hole.CountersinkDepth = countersink;

                var operation = new PlaceDrainHoleOperation(
                    hole.SurfacePoint,
                    hole.SurfaceNormal,
                    diameter,
                    countersink);

                OperationResult result = await _document.ApplyAsync(operation);
                if (result.Changed)
                {
                    appliedCount++;
                    lastMessage = result.Summary;
                }
                else
                {
                    refusals.Add(result.Summary);
                }
            }

            UpdateStatsDisplay();
            UpdateHolesList();

            if (ResultMessageText is not null)
            {
                // A hole that could not be drilled has to be said out loud: reporting only the
                // successes would be the "reports success while being wrong" failure this feature
                // was rebuilt to remove (§4, "Honest diagnostics").
                var lines = new List<string>();
                if (appliedCount > 0)
                {
                    lines.Add($"Drilled {appliedCount} of {appliedCount + refusals.Count} hole(s). Last: {lastMessage}");
                }

                if (refusals.Count > 0)
                {
                    lines.Add($"{refusals.Count} hole(s) not drilled: {refusals[0]}");
                }

                ResultMessageText.Text = lines.Count > 0
                    ? string.Join(" ", lines)
                    : "No holes could be applied.";
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
