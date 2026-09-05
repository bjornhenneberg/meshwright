using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;

namespace Meshwright.App.Views.Edit;

public partial class DecimatePanel : UserControl
{
    private DMesh3? _mesh;
    private MeshDocument? _document;

    public DecimatePanel()
    {
        InitializeComponent();
        ModeCombo.SelectedIndex = 0; // Default to TriangleCount
        TargetInput.Text = "100";
        UpdateLivePreview();

        // Wire up input change handlers for live preview
        ModeCombo.SelectionChanged += (_, _) => OnModeOrTargetChanged();
        TargetInput.TextChanged += (_, _) => OnModeOrTargetChanged();
    }

    /// <summary>Binds this panel to a MeshDocument for integration with the main UI.</summary>
    public void SetDocument(MeshDocument doc)
    {
        _document = doc;
        Mesh = doc.Mesh;
    }

    /// <summary>The mesh this panel will operate on. Exposed for UI integration and testing.</summary>
    public DMesh3? Mesh
    {
        get => _mesh;
        set
        {
            _mesh = value;
            UpdateLivePreview();
        }
    }

    /// <summary>Current triangle count of <see cref="Mesh"/>, exposed for testing.</summary>
    public int CurrentTriangleCount => _mesh?.TriangleCount ?? 0;

    /// <summary>The resolved target triangle count for the current mode/value, exposed for testing.</summary>
    public int ResolvedTargetTriangleCount
    {
        get
        {
            if (_mesh is null || CurrentTriangleCount == 0)
                return 0;

            return GetOperation()?.TargetTriangleCount(CurrentTriangleCount) ?? 0;
        }
    }

    private void OnModeOrTargetChanged()
    {
        UpdateLivePreview();
    }

    private void UpdateLivePreview()
    {
        int current = CurrentTriangleCount;

        if (current == 0)
        {
            LivePreviewText.Text = "No mesh loaded";
            TargetUnitLabel.Text = "triangles";
            return;
        }

        int target = ResolvedTargetTriangleCount;
        double percentOfCurrent = current > 0 ? 100.0 * target / current : 0.0;

        LivePreviewText.Text = string.Format(
            "Current: {0} triangles, Target: {1} triangles ({2:0.#}% of current)",
            current,
            target,
            percentOfCurrent);
    }

    /// <summary>The in-flight Apply from the most recent click, exposed so tests can await real
    /// completion of an operation that now runs off the UI thread.</summary>
    public Task? PendingOperationForTesting { get; private set; }

    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        Task task = OnApplyClickCore();
        PendingOperationForTesting = task;
        await task;
    }

    private async Task OnApplyClickCore()
    {
        if (_mesh is null || CurrentTriangleCount == 0)
        {
            ResultText.Text = "No mesh loaded.";
            ResultText.IsVisible = true;
            return;
        }

        try
        {
            var operation = GetOperation();
            if (operation is null)
            {
                ResultText.Text = "Invalid target value.";
                ResultText.IsVisible = true;
                return;
            }

            // Decimation of a large mesh is the canonical slow operation (§6.3, backlog item 13),
            // so route it through the document off the UI thread whenever one is bound. The
            // synchronous fallback below only exists for the handful of unit tests that drive
            // this panel via the Mesh setter without ever calling SetDocument.
            OperationResult result = _document is not null
                ? await _document.ApplyAsync(operation)
                : operation.Apply(_mesh);

            ResultText.Text = result.Summary;
            ResultText.IsVisible = true;

            // Refresh the live preview to reflect the new mesh state
            UpdateLivePreview();
        }
        catch (Exception ex)
        {
            ResultText.Text = $"Error: {ex.Message}";
            ResultText.IsVisible = true;
        }
    }

    private DecimateOperation? GetOperation()
    {
        if (!int.TryParse(TargetInput.Text, out int targetValue) || targetValue < 1)
        {
            return null;
        }

        int modeIndex = ModeCombo.SelectedIndex;
        return modeIndex switch
        {
            1 => // Percentage mode
                DecimateOperation.ToPercentage(targetValue / 100.0),
            _ => // TriangleCount mode (default / index 0)
                DecimateOperation.ToTriangleCount(targetValue),
        };
    }
}
