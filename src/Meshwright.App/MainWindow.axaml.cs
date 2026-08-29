using System;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Meshwright.IO.Stl;

namespace Meshwright.App;

public partial class MainWindow : Window
{
    private const string SampleMeshResourceName = "Meshwright.App.Assets.SampleMesh.stl";

    public MainWindow()
    {
        InitializeComponent();
        LoadSampleMesh();
    }

    private void LoadSampleMesh()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using Stream? stream = assembly.GetManifestResourceStream(SampleMeshResourceName);
        if (stream is null)
        {
            StatusText.Text = "Sample mesh resource not found.";
            return;
        }

        var mesh = StlReader.Read(stream);
        Viewport.Mesh = mesh;
        StatusText.Text = $"Loaded sample tetrahedron ({mesh.TriangleCount} triangles)";
    }

    private async void OnOpenFileClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storageProvider)
        {
            return;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open STL file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("STL files") { Patterns = new[] { "*.stl" } },
            },
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            var mesh = StlReader.Read(stream);
            Viewport.Mesh = mesh;
            StatusText.Text = $"Loaded {files[0].Name} ({mesh.TriangleCount} triangles)";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load {files[0].Name}: {ex.Message}";
        }
    }

    private void OnViewportPointerPressed(object? sender, PointerPressedEventArgs e) =>
        Viewport.HandleExternalPointerPressed(e);

    private void OnViewportPointerMoved(object? sender, PointerEventArgs e) =>
        Viewport.HandleExternalPointerMoved(e);

    private void OnViewportPointerReleased(object? sender, PointerReleasedEventArgs e) =>
        Viewport.HandleExternalPointerReleased(e);

    private void OnViewportPointerWheelChanged(object? sender, PointerWheelEventArgs e) =>
        Viewport.HandleExternalPointerWheelChanged(e);
}
