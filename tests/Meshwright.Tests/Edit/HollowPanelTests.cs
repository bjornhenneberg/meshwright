using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using g3;
using Meshwright.App.Views.Edit;
using Meshwright.Core;
using Xunit;

namespace Meshwright.Tests.Edit;

public class HollowPanelTests
{
    /// <summary>Helper to build a simple closed cube for panel testing.</summary>
    private static DMesh3 BuildCube(double size)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(1, 0, 0) * size);
        int v110 = mesh.AppendVertex(new Vector3d(1, 1, 0) * size);
        int v010 = mesh.AppendVertex(new Vector3d(0, 1, 0) * size);
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, 1) * size);
        int v101 = mesh.AppendVertex(new Vector3d(1, 0, 1) * size);
        int v111 = mesh.AppendVertex(new Vector3d(1, 1, 1) * size);
        int v011 = mesh.AppendVertex(new Vector3d(0, 1, 1) * size);

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
        }

        Quad(v000, v010, v110, v100);
        Quad(v001, v101, v111, v011);
        Quad(v000, v100, v101, v001);
        Quad(v010, v011, v111, v110);
        Quad(v000, v001, v011, v010);
        Quad(v100, v110, v111, v101);

        return mesh;
    }

    [AvaloniaFact]
    public void Constructing_HollowPanel_DoesNotThrow()
    {
        var panel = new HollowPanel();

        Assert.NotNull(panel);
    }

    [AvaloniaFact]
    public void SetDocument_LoadsMesh_UpdatesBeforeStats()
    {
        var panel = new HollowPanel();
        var document = new MeshDocument();
        var mesh = BuildCube(20.0);
        document.Load(mesh);

        panel.SetDocument(document);

        Assert.NotNull(panel.CurrentReport);
        Assert.Contains("Triangles: 12", panel.FindControl<TextBlock>("BeforeStats")?.Text ?? "");
    }

    [AvaloniaFact]
    public void BeforeStats_DisplaysTriangleCountAndVolume()
    {
        var panel = new HollowPanel();
        var document = new MeshDocument();
        var mesh = BuildCube(10.0);
        document.Load(mesh);

        panel.SetDocument(document);

        var beforeStatsTextBlock = panel.FindControl<TextBlock>("BeforeStats");
        Assert.NotNull(beforeStatsTextBlock);
        Assert.Contains("Triangles:", beforeStatsTextBlock.Text);
        Assert.Contains("Volume:", beforeStatsTextBlock.Text);
    }

    [AvaloniaFact]
    public void WallThicknessInputField_IsPopulatedWithDefault()
    {
        var panel = new HollowPanel();

        var input = panel.FindControl<TextBox>("WallThicknessInput");
        Assert.NotNull(input);
        Assert.Equal("2.0", input.Text);
    }
}
