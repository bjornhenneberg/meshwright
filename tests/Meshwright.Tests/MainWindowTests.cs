using System.Reflection;
using Avalonia.Headless.XUnit;
using Meshwright.App;
using Meshwright.IO.Stl;
using Xunit;

namespace Meshwright.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public void Constructing_MainWindow_DoesNotThrow()
    {
        var window = new MainWindow();

        Assert.NotNull(window);
    }

    [Fact]
    public void SampleMeshResource_IsEmbeddedAndParsesAsStl()
    {
        var assembly = typeof(MainWindow).Assembly;
        using var stream = assembly.GetManifestResourceStream("Meshwright.App.Assets.SampleMesh.stl");

        Assert.NotNull(stream);
        var mesh = StlReader.Read(stream!);
        Assert.Equal(4, mesh.TriangleCount);
    }
}
