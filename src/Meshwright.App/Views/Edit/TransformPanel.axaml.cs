using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Rendering.Gizmos;

namespace Meshwright.App.Views.Edit;

public partial class TransformPanel : UserControl
{
    private MeshDocument? _document;
    private TransformGizmo? _gizmo;
    private bool _gizmoActive;
    private Action? _gizmoActivationCallback;
    private Action? _gizmoDeactivationCallback;

    public TransformPanel()
    {
        InitializeComponent();
        ModeCombo.SelectedIndex = 0; // Default to Move
        ModeCombo.SelectionChanged += (_, _) => OnModeChanged();
        UpdateStatsDisplay();
    }

    /// <summary>
    /// Sets the mesh document this panel operates on, exposed for testing.
    /// Called by the integrating view (or test harness) to bind this panel to a live document.
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
    public void SetGizmo(TransformGizmo gizmo)
    {
        if (_gizmoActive)
        {
            // The old gizmo is going away (e.g. a new mesh was loaded), so drop back to the
            // inactive UI state rather than leaving a stale "Done with gizmo" button.
            DeactivateGizmo();
        }

        _gizmo = gizmo;
        _gizmo.TransformChanged += (_, _) => SyncInputsFromGizmo();
        _gizmo.SetMode(ModeForIndex(ModeCombo.SelectedIndex) ?? TransformGizmo.TransformMode.Move);
    }

    /// <summary>
    /// Sets callbacks to activate/deactivate the gizmo on the viewport when the user
    /// clicks the gizmo button.
    /// </summary>
    public void SetGizmoActivationCallback(Action? onActivate, Action? onDeactivate)
    {
        _gizmoActivationCallback = onActivate;
        _gizmoDeactivationCallback = onDeactivate;
    }

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultText?.Text;

    private void OnModeChanged()
    {
        int selectedMode = ModeCombo.SelectedIndex;
        MovePanel.IsVisible = selectedMode == 0;
        RotatePanel.IsVisible = selectedMode == 1;
        ScalePanel.IsVisible = selectedMode == 2;
        MirrorPanel.IsVisible = selectedMode == 3;

        TransformGizmo.TransformMode? gizmoMode = ModeForIndex(selectedMode);
        ActivateGizmoButton.IsEnabled = gizmoMode is not null;

        if (gizmoMode is null)
        {
            // Mirror has no gizmo equivalent; fall back to the textboxes.
            if (_gizmoActive)
            {
                DeactivateGizmo();
            }
            GizmoStatusText.Text = "Mirror is set with the fields below.";
            return;
        }

        if (_gizmo is not null)
        {
            _gizmo.SetMode(gizmoMode.Value);
        }

        GizmoStatusText.Text = _gizmoActive ? GizmoHintForMode(gizmoMode.Value) : "";
    }

    private static TransformGizmo.TransformMode? ModeForIndex(int index) => index switch
    {
        0 => TransformGizmo.TransformMode.Move,
        1 => TransformGizmo.TransformMode.Rotate,
        2 => TransformGizmo.TransformMode.Scale,
        _ => null,
    };

    private static Vector3d AxisForIndex(int index) => index switch
    {
        0 => Vector3d.AxisX,
        1 => Vector3d.AxisY,
        2 => Vector3d.AxisZ,
        _ => Vector3d.AxisZ,
    };

    private static string GizmoHintForMode(TransformGizmo.TransformMode mode) => mode switch
    {
        TransformGizmo.TransformMode.Move => "Drag an axis arrow in the viewport, then Apply.",
        TransformGizmo.TransformMode.Rotate => "Drag a rotation ring in the viewport, then Apply.",
        _ => "Drag the centre handle in the viewport, then Apply.",
    };

    private void OnActivateGizmoClick(object? sender, RoutedEventArgs e)
    {
        if (_gizmo is null)
        {
            ShowResult("Gizmo not set up. Cannot activate.");
            return;
        }

        TransformGizmo.TransformMode? gizmoMode = ModeForIndex(ModeCombo.SelectedIndex);
        if (gizmoMode is null)
        {
            return;
        }

        if (!_gizmoActive)
        {
            _gizmoActive = true;
            _gizmo.SetMode(gizmoMode.Value);
            ActivateGizmoButton.Content = "Done with gizmo";
            GizmoStatusText.Text = GizmoHintForMode(gizmoMode.Value);
            _gizmoActivationCallback?.Invoke();
        }
        else
        {
            DeactivateGizmo();
        }
    }

    private void DeactivateGizmo()
    {
        _gizmoActive = false;
        ActivateGizmoButton.Content = "Use gizmo";
        GizmoStatusText.Text = "";
        _gizmoDeactivationCallback?.Invoke();
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
        ActivateGizmoButton.Content = "Use gizmo";
        GizmoStatusText.Text = "";
    }

    /// <summary>
    /// Mirrors the gizmo's live values into the visible textboxes so the displayed numbers always
    /// match what Apply is about to do.
    /// </summary>
    private void SyncInputsFromGizmo()
    {
        if (_gizmo is null)
        {
            return;
        }

        var transform = _gizmo.CurrentTransform;
        var center = _gizmo.Center;

        switch (_gizmo.Mode)
        {
            case TransformGizmo.TransformMode.Move:
                MoveXInput.Text = Format(transform.X);
                MoveYInput.Text = Format(transform.Y);
                MoveZInput.Text = Format(transform.Z);
                break;

            case TransformGizmo.TransformMode.Rotate:
                RotateAngleInput.Text = Format(transform.X);
                if (_gizmo.ActiveAxis is int axis)
                {
                    RotateAxisCombo.SelectedIndex = axis;
                }
                RotateCenterXInput.Text = Format(center.X);
                RotateCenterYInput.Text = Format(center.Y);
                RotateCenterZInput.Text = Format(center.Z);
                break;

            case TransformGizmo.TransformMode.Scale:
                ScaleFactorInput.Text = Format(transform.X);
                ScaleCenterXInput.Text = Format(center.X);
                ScaleCenterYInput.Text = Format(center.Y);
                ScaleCenterZInput.Text = Format(center.Z);
                break;
        }
    }

    /// <summary>True when the gizmo has been dragged in the mode the panel is currently showing,
    /// in which case its value wins over the textboxes on Apply.</summary>
    private bool UseGizmoValues(TransformGizmo.TransformMode mode) =>
        _gizmo is not null && _gizmo.HasTransform && _gizmo.Mode == mode;

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private void UpdateStatsDisplay()
    {
        if (_document?.Mesh is null)
        {
            BeforeStatsText.Text = "No mesh loaded";
            AfterStatsText.Text = "(not applied yet)";
            return;
        }

        var stats = MeshStatistics.Compute(_document.Mesh);
        var bounds = _document.Mesh.CachedBounds;

        BeforeStatsText.Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0} tris, {1:0.##} mm³, bounds: {2:0.#} × {3:0.#} × {4:0.#}",
            stats.TriangleCount,
            stats.Volume,
            bounds.Extents.x,
            bounds.Extents.y,
            bounds.Extents.z);

        AfterStatsText.Text = "(not applied yet)";
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_document?.Mesh is null)
        {
            ShowResult("No mesh loaded.");
            return;
        }

        try
        {
            int modeIndex = ModeCombo.SelectedIndex;
            IMeshOperation? operation = null;

            switch (modeIndex)
            {
                case 0: // Move
                    operation = ParseAndCreateMoveOperation();
                    break;
                case 1: // Rotate
                    operation = ParseAndCreateRotateOperation();
                    break;
                case 2: // Scale
                    operation = ParseAndCreateScaleOperation();
                    break;
                case 3: // Mirror
                    operation = ParseAndCreateMirrorOperation();
                    break;
            }

            if (operation == null)
            {
                ShowResult("Invalid input values.");
                return;
            }

            // Captured before Apply: Apply raises MeshDocument.Changed synchronously, which
            // MainWindow uses to refresh every panel's stats display from the (now mutated)
            // document, clobbering BeforeStatsText with post-operation figures. Restoring the
            // pre-operation snapshot below undoes that clobber.
            var statsBefore = MeshStatistics.Compute(_document.Mesh);
            var boundsBefore = _document.Mesh.CachedBounds;

            OperationResult result = _document.Apply(operation);

            var statsAfter = MeshStatistics.Compute(_document.Mesh);
            var boundsAfter = _document.Mesh.CachedBounds;
            BeforeStatsText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} tris, {1:0.##} mm³, bounds: {2:0.#} × {3:0.#} × {4:0.#}",
                statsBefore.TriangleCount,
                statsBefore.Volume,
                boundsBefore.Extents.x,
                boundsBefore.Extents.y,
                boundsBefore.Extents.z);
            AfterStatsText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} tris, {1:0.##} mm³, bounds: {2:0.#} × {3:0.#} × {4:0.#}",
                statsAfter.TriangleCount,
                statsAfter.Volume,
                boundsAfter.Extents.x,
                boundsAfter.Extents.y,
                boundsAfter.Extents.z);

            // The drag has been consumed; start the next one from a clean slate so the same
            // offset/angle/factor is not applied twice.
            if (_gizmo is not null)
            {
                _gizmo.ResetTransform();
                SyncInputsFromGizmo();
            }

            ShowResult(result.Summary);
        }
        catch (Exception ex)
        {
            ShowResult($"Error: {ex.Message}");
        }
    }

    private IMeshOperation? ParseAndCreateMoveOperation()
    {
        if (UseGizmoValues(TransformGizmo.TransformMode.Move) && _gizmo is not null)
        {
            var offset = _gizmo.CurrentTransform;
            return new TranslateOperation(new Vector3d(offset.X, offset.Y, offset.Z));
        }

        if (!TryParseDouble(MoveXInput.Text, out double x) ||
            !TryParseDouble(MoveYInput.Text, out double y) ||
            !TryParseDouble(MoveZInput.Text, out double z))
        {
            return null;
        }

        return new TranslateOperation(new Vector3d(x, y, z));
    }

    private IMeshOperation? ParseAndCreateRotateOperation()
    {
        if (UseGizmoValues(TransformGizmo.TransformMode.Rotate) && _gizmo is not null)
        {
            var center = _gizmo.Center;
            return new RotateOperation(
                _gizmo.CurrentTransform.X,
                AxisForIndex(_gizmo.ActiveAxis ?? 2),
                new Vector3d(center.X, center.Y, center.Z));
        }

        if (!TryParseDouble(RotateAngleInput.Text, out double angle))
            return null;

        int axisIndex = RotateAxisCombo.SelectedIndex;
        Vector3d axis = axisIndex switch
        {
            0 => Vector3d.AxisX,
            1 => Vector3d.AxisY,
            2 => Vector3d.AxisZ,
            _ => Vector3d.AxisZ,
        };

        if (!TryParseDouble(RotateCenterXInput.Text, out double cx) ||
            !TryParseDouble(RotateCenterYInput.Text, out double cy) ||
            !TryParseDouble(RotateCenterZInput.Text, out double cz))
        {
            return null;
        }

        return new RotateOperation(angle, axis, new Vector3d(cx, cy, cz));
    }

    private IMeshOperation? ParseAndCreateScaleOperation()
    {
        if (UseGizmoValues(TransformGizmo.TransformMode.Scale) && _gizmo is not null)
        {
            var center = _gizmo.Center;
            return new ScaleOperation(
                _gizmo.CurrentTransform.X,
                new Vector3d(center.X, center.Y, center.Z));
        }

        if (!TryParseDouble(ScaleFactorInput.Text, out double scale))
            return null;

        if (!TryParseDouble(ScaleCenterXInput.Text, out double cx) ||
            !TryParseDouble(ScaleCenterYInput.Text, out double cy) ||
            !TryParseDouble(ScaleCenterZInput.Text, out double cz))
        {
            return null;
        }

        return new ScaleOperation(scale, new Vector3d(cx, cy, cz));
    }

    private IMeshOperation? ParseAndCreateMirrorOperation()
    {
        if (!TryParseDouble(MirrorPointXInput.Text, out double px) ||
            !TryParseDouble(MirrorPointYInput.Text, out double py) ||
            !TryParseDouble(MirrorPointZInput.Text, out double pz))
        {
            return null;
        }

        if (!TryParseDouble(MirrorNormalXInput.Text, out double nx) ||
            !TryParseDouble(MirrorNormalYInput.Text, out double ny) ||
            !TryParseDouble(MirrorNormalZInput.Text, out double nz))
        {
            return null;
        }

        var normal = new Vector3d(nx, ny, nz);
        if (normal.LengthSquared < 1e-10)
        {
            return null; // Invalid normal vector
        }

        return new MirrorOperation(new Vector3d(px, py, pz), normal);
    }

    private void OnAlignToBedClick(object? sender, RoutedEventArgs e)
    {
        if (_document?.Mesh is null)
        {
            ShowResult("No mesh loaded.");
            return;
        }

        try
        {
            var operation = new AlignToBedOperation();

            // Captured before Apply: Apply raises MeshDocument.Changed synchronously, which
            // MainWindow uses to refresh every panel's stats display from the (now mutated)
            // document, clobbering BeforeStatsText with post-operation figures. Restoring the
            // pre-operation snapshot below undoes that clobber.
            var statsBefore = MeshStatistics.Compute(_document.Mesh);
            var boundsBefore = _document.Mesh.CachedBounds;

            OperationResult result = _document.Apply(operation);

            var statsAfter = MeshStatistics.Compute(_document.Mesh);
            var boundsAfter = _document.Mesh.CachedBounds;
            BeforeStatsText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} tris, {1:0.##} mm³, bounds: {2:0.#} × {3:0.#} × {4:0.#}",
                statsBefore.TriangleCount,
                statsBefore.Volume,
                boundsBefore.Extents.x,
                boundsBefore.Extents.y,
                boundsBefore.Extents.z);
            AfterStatsText.Text = string.Format(
                CultureInfo.InvariantCulture,
                "{0} tris, {1:0.##} mm³, bounds: {2:0.#} × {3:0.#} × {4:0.#}",
                statsAfter.TriangleCount,
                statsAfter.Volume,
                boundsAfter.Extents.x,
                boundsAfter.Extents.y,
                boundsAfter.Extents.z);

            ShowResult(result.Summary);
        }
        catch (Exception ex)
        {
            ShowResult($"Error: {ex.Message}");
        }
    }

    private void OnDropToZ0Click(object? sender, RoutedEventArgs e)
    {
        // DropToZ0 is an alias for AlignToBed
        OnAlignToBedClick(sender, e);
    }

    private void ShowResult(string message)
    {
        ResultText.Text = message;
        ResultText.IsVisible = true;
    }

    private static bool TryParseDouble(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
