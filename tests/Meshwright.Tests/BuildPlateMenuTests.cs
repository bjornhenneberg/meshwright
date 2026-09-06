using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using g3;
using Meshwright.App;
using Meshwright.App.Views;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Printing;
using Meshwright.IO.Stl;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// The build plate as a user meets it: pick a printer in the View menu, load a model, and read the
/// warning - or read no warning, which is the half that a broken implementation passes by
/// accident. These start at the menu item and end at the viewport or the warning text, because
/// §11 (2026-09-06) records three controls that parsed a value and never used it, each behind a
/// green suite that tested the logic and not the wiring.
/// </summary>
public class BuildPlateMenuTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"meshwright-plate-{Guid.NewGuid():N}");

    [AvaloniaFact]
    public void TheViewportGetsABedByDefault_AndTheSampleMeshFitsSilently()
    {
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;

        Assert.NotNull(viewport.BuildVolume);
        Assert.Equal(BuildVolume.Default, viewport.BuildVolume);
        Assert.True(viewport.ModelFitsBuildVolume);
        Assert.Null(window.BuildPlateWarning);
    }

    [AvaloniaFact]
    public void TheMenuOffersEveryPreset_AndPickingOneReachesTheViewport()
    {
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;
        MenuItem[] presetItems = PresetItems(window);

        Assert.Equal(BuildVolume.Presets.Count, presetItems.Length);

        foreach (MenuItem item in presetItems)
        {
            var volume = (BuildVolume)item.Tag!;
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(volume, window.SelectedBuildVolume);
            Assert.Equal(volume, viewport.BuildVolume);
        }
    }

    [AvaloniaFact]
    public void ChangingTheBed_ChangesTheVerdictOnAModelThatHasNotMoved()
    {
        // The model is loaded once and never touched again; only the configured printer changes.
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;
        window.LoadFileForTesting(WriteBox(240, 240, 200));

        SelectBed(window, "Large format (350 x 350 x 400)");
        Assert.Null(window.BuildPlateWarning);
        Assert.True(viewport.ModelFitsBuildVolume);

        SelectBed(window, "Prusa MINI (180 x 180 x 180)");
        Assert.NotNull(window.BuildPlateWarning);
        Assert.False(viewport.ModelFitsBuildVolume);
        Assert.Contains("past the", window.BuildPlateWarning);
        Assert.Contains("above the maximum height", window.BuildPlateWarning);

        SelectBed(window, "Large format (350 x 350 x 400)");
        Assert.Null(window.BuildPlateWarning);
        Assert.True(viewport.ModelFitsBuildVolume);
    }

    [AvaloniaFact]
    public void TheWarningNamesTheDirectionAndTheAmount()
    {
        var window = new MainWindow();
        SelectBed(window, "Ender 3 (220 x 220 x 250)");
        window.LoadFileForTesting(WriteBox(240, 100, 50));

        string warning = window.BuildPlateWarning!;

        Assert.Contains("10 mm past the left edge", warning);
        Assert.Contains("10 mm past the right edge", warning);
        Assert.DoesNotContain("front", warning);
        Assert.DoesNotContain("height", warning);
    }

    [AvaloniaFact]
    public void HidingThePlate_TakesTheWarningWithIt_AndShowingItBringsItBack()
    {
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;
        SelectBed(window, "Ender 3 (220 x 220 x 250)");
        window.LoadFileForTesting(WriteBox(400, 400, 50));
        Assert.NotNull(window.BuildPlateWarning);

        var toggle = (MenuItem)GetField(window, "ShowBuildPlateMenuItem")!;
        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Null(viewport.BuildVolume);
        Assert.Null(window.BuildPlateWarning);
        Assert.False(toggle.IsChecked, "The menu still claims the plate is shown.");

        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.NotNull(viewport.BuildVolume);
        Assert.NotNull(window.BuildPlateWarning);
        Assert.True(toggle.IsChecked);
    }

    [AvaloniaFact]
    public void MovingTheModelOntoTheBed_ClearsTheWarning()
    {
        // The verdict must follow edits, not just loads: every operation, undo and redo runs
        // through the same refresh.
        var window = new MainWindow();
        SelectBed(window, "Ender 3 (220 x 220 x 250)");
        window.LoadFileForTesting(WriteBox(100, 100, 100, zOffset: -60));
        Assert.Contains("below the bed", window.BuildPlateWarning);

        // The synchronous Apply, not ApplyAsync: this test runs on Avalonia's headless dispatcher
        // thread, and blocking it on a task whose continuations are posted back to it deadlocks.
        var document = (MeshDocument)GetField(window, "_document")!;
        document.Apply(new DropToZ0Operation());

        Assert.Null(window.BuildPlateWarning);
    }

    [AvaloniaFact]
    public void TheShortcutTogglesThePlate_NotJustTheMenuItem()
    {
        // Ctrl+Shift+B did nothing at all in the running app: the handler read the menu item's
        // IsChecked, which Avalonia updates on a click but not on a HotKey activation, so the
        // shortcut flipped a value that had not changed. A dead shortcut is invisible to any test
        // that raises Click directly, which is how this one got as far as the screenshots.
        var window = new MainWindow();
        window.Show();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;
        var toggle = (MenuItem)GetField(window, "ShowBuildPlateMenuItem")!;
        Assert.NotNull(viewport.BuildVolume);

        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.Null(viewport.BuildVolume);
        Assert.False(toggle.IsChecked);

        window.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.NotNull(viewport.BuildVolume);
        Assert.True(toggle.IsChecked);
    }

    [AvaloniaFact]
    public void ThePickedBed_IsTheOneCheckedInTheMenu()
    {
        var window = new MainWindow();
        SelectBed(window, "Bambu X1C (256 x 256 x 256)");

        foreach (MenuItem item in PresetItems(window))
        {
            bool isSelected = ((BuildVolume)item.Tag!) == window.SelectedBuildVolume;
            Assert.Equal(isSelected, item.IsChecked);
        }
    }

    private static void SelectBed(MainWindow window, string name)
    {
        MenuItem item = PresetItems(window).Single(i => ((BuildVolume)i.Tag!).Name == name);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static MenuItem[] PresetItems(MainWindow window)
    {
        var menu = (MenuItem)GetField(window, "BuildPlateMenuItem")!;
        return menu.Items.OfType<MenuItem>().Where(i => i.Tag is BuildVolume).ToArray();
    }

    /// <summary>Writes a temp STL of an axis-aligned box centred on X/Y and sitting at Z=zOffset.</summary>
    private string WriteBox(double width, double depth, double height, double zOffset = 0)
    {
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, $"box-{Guid.NewGuid():N}.stl");
        StlWriter.WriteFile(path, Box(width, depth, height, zOffset));
        return path;
    }

    private static DMesh3 Box(double width, double depth, double height, double zOffset)
    {
        var mesh = new DMesh3();
        var ids = new int[8];
        int i = 0;
        foreach (double x in new[] { -width / 2, width / 2 })
        {
            foreach (double y in new[] { -depth / 2, depth / 2 })
            {
                foreach (double z in new[] { zOffset, zOffset + height })
                {
                    ids[i++] = mesh.AppendVertex(new Vector3d(x, y, z));
                }
            }
        }

        int[][] faces =
        {
            new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 },
            new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 },
            new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 },
        };

        foreach (int[] face in faces)
        {
            mesh.AppendTriangle(ids[face[0]], ids[face[1]], ids[face[2]]);
            mesh.AppendTriangle(ids[face[0]], ids[face[2]], ids[face[3]]);
        }

        return mesh;
    }

    private static object? GetField(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return field.GetValue(instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
