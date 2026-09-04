using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;

namespace Meshwright.App.Views.Edit;

public partial class HollowPanel : UserControl
{
    private MeshDocument? _document;

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

    /// <summary>Diagnostics report for the currently loaded mesh, exposed for testing.</summary>
    public MeshDiagnosticsReport? CurrentReport => _document?.Report;

    /// <summary>Current operation result message text, exposed for testing.</summary>
    public string? OperationResultMessage => ResultMessageText?.Text;

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

        if (!double.TryParse(WallThicknessInput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double wallThickness))
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
