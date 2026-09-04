using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;

namespace Meshwright.App.Views.Edit;

public partial class TransformPanel : UserControl
{
    private MeshDocument? _document;

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

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultText?.Text;

    private void OnModeChanged()
    {
        int selectedMode = ModeCombo.SelectedIndex;
        MovePanel.IsVisible = selectedMode == 0;
        RotatePanel.IsVisible = selectedMode == 1;
        ScalePanel.IsVisible = selectedMode == 2;
        MirrorPanel.IsVisible = selectedMode == 3;
    }

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

            OperationResult result = _document.Apply(operation);

            var statsAfter = MeshStatistics.Compute(_document.Mesh);
            var boundsAfter = _document.Mesh.CachedBounds;
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

    private IMeshOperation? ParseAndCreateMoveOperation()
    {
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
            OperationResult result = _document.Apply(operation);

            var statsAfter = MeshStatistics.Compute(_document.Mesh);
            var boundsAfter = _document.Mesh.CachedBounds;
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
