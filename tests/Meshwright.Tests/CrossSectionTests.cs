using System;
using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using g3;
using Meshwright.App;
using Meshwright.App.Views;
using Meshwright.IO.Stl;
using Meshwright.Rendering.GL;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// The cross-section as a user meets it: switch it on, drag the slider, flip it, change the axis.
///
/// <para>
/// Every assertion here ends at <see cref="MainWindow.ActiveCrossSection"/> - the plane the
/// viewport is actually drawing - rather than at the slider's own value. A control that holds a
/// number nothing reads is the failure §11 (2026-09-06) records three of, and a slider is a
/// particularly easy place to repeat it.
/// </para>
/// </summary>
public class CrossSectionTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"meshwright-section-{Guid.NewGuid():N}");

    [AvaloniaFact]
    public void ItIsOffUntilAskedFor_AndItsBarIsNotOnScreen()
    {
        var window = new MainWindow();

        Assert.Null(window.ActiveCrossSection);
        Assert.False(window.CrossSectionBarVisible);
    }

    [AvaloniaFact]
    public void CtrlShiftC_TurnsItOnAndOff_AndTheMenuMarkFollows()
    {
        // Driven by the keyboard, not by raising Click: Avalonia does not update a menu item's
        // IsChecked when it is activated by its HotKey, so a handler reading that value makes the
        // shortcut a silent no-op - which is what Ctrl+Shift+B did until 2026-09-06. A test that
        // raises Click cannot see this class of bug at all.
        var window = new MainWindow();
        window.Show();
        var toggle = (MenuItem)GetField(window, "ShowCrossSectionMenuItem")!;

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.NotNull(window.ActiveCrossSection);
        Assert.True(window.CrossSectionBarVisible);
        Assert.True(toggle.IsChecked);

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.Null(window.ActiveCrossSection);
        Assert.False(window.CrossSectionBarVisible);
        Assert.False(toggle.IsChecked);
    }

    [AvaloniaFact]
    public void SwitchingItOn_OpensTheModelDownTheMiddleOfWhateverIsLoadedNow()
    {
        // The position is a world millimetre, so one left over from a previous model means nothing
        // here. A 40 mm cube inherited 0 mm from the sample mesh and the first thing the section
        // ever showed was an almost-empty viewport - correct arithmetic, and indistinguishable
        // from the feature having deleted the model. Found by looking at the running app.
        var window = new MainWindow();
        EnableSection(window);
        window.SetCrossSectionSliderForTesting(0.0);
        Toggle(window);

        window.LoadFileForTesting(WriteBox(40, 40, 40, zOffset: 0));
        Toggle(window);

        Assert.Equal(20f, window.ActiveCrossSection!.Value.Position, 4);
    }

    [AvaloniaFact]
    public void TheSliderTravelsTheModel_AndStartsInTheMiddleOfIt()
    {
        // A 40 mm tall box based at z = 10: the slider must span 10..50 and open the box halfway
        // up, not span 0..1 or 0..40.
        var window = new MainWindow();
        window.LoadFileForTesting(WriteBox(20, 20, 40, zOffset: 10));
        EnableSection(window);
        var slider = (Slider)GetField(window, "CrossSectionSlider")!;

        Assert.Equal(10.0, slider.Minimum, 6);
        Assert.Equal(50.0, slider.Maximum, 6);
        Assert.Equal(30.0, slider.Value, 6);
        Assert.Equal(30f, window.ActiveCrossSection!.Value.Position, 4);
    }

    [AvaloniaFact]
    public void DraggingTheSlider_MovesThePlaneTheViewportDraws()
    {
        // Starts at a typed slider value and ends at a geometric question about the plane, so
        // nothing in between can be inert: at z = 12 the box's base must survive and its top must
        // not.
        var window = new MainWindow();
        window.LoadFileForTesting(WriteBox(20, 20, 40, zOffset: 10));
        EnableSection(window);

        window.SetCrossSectionSliderForTesting(12.0);

        CrossSectionPlane plane = window.ActiveCrossSection!.Value;
        Assert.Equal(12f, plane.Position, 4);
        Assert.False(plane.Hides(new System.Numerics.Vector3(0, 0, 10.5f)));
        Assert.True(plane.Hides(new System.Numerics.Vector3(0, 0, 49.5f)));
        Assert.Contains("12", window.CrossSectionPositionLabel);
    }

    [AvaloniaFact]
    public void FlipSide_SwapsWhichHalfSurvives_AndSaysWhichInWords()
    {
        var window = new MainWindow();
        window.LoadFileForTesting(WriteBox(20, 20, 40, zOffset: 10));
        EnableSection(window);
        window.SetCrossSectionSliderForTesting(30.0);

        var bottom = new System.Numerics.Vector3(0, 0, 11f);
        var top = new System.Numerics.Vector3(0, 0, 49f);

        Assert.False(window.ActiveCrossSection!.Value.Hides(bottom));
        Assert.True(window.ActiveCrossSection!.Value.Hides(top));
        Assert.Contains("≤", window.CrossSectionPositionLabel);

        Flip(window);

        Assert.True(window.ActiveCrossSection!.Value.Hides(bottom));
        Assert.False(window.ActiveCrossSection!.Value.Hides(top));

        // The readout has to name the surviving half. "Z = 30 mm" is equally true of both sides of
        // the plane, so on its own it cannot tell a user what Flip Side just did.
        Assert.Contains("≥", window.CrossSectionPositionLabel);
    }

    [AvaloniaFact]
    public void ChangingTheAxis_ReRangesTheSliderAndTurnsThePlane()
    {
        // The box is 200 mm wide and 40 mm tall, so a Z position carried across to X unchanged
        // would leave the plane inside the model and look like it worked. It must re-centre.
        var window = new MainWindow();
        window.LoadFileForTesting(WriteBox(200, 20, 40, zOffset: 10));
        EnableSection(window);
        var slider = (Slider)GetField(window, "CrossSectionSlider")!;
        var combo = (ComboBox)GetField(window, "CrossSectionAxisCombo")!;

        Assert.Equal(CrossSectionAxis.Z, window.ActiveCrossSection!.Value.Axis);

        combo.SelectedItem = CrossSectionAxis.X;

        Assert.Equal(CrossSectionAxis.X, window.ActiveCrossSection!.Value.Axis);
        Assert.Equal(-100.0, slider.Minimum, 6);
        Assert.Equal(100.0, slider.Maximum, 6);
        Assert.Equal(0f, window.ActiveCrossSection!.Value.Position, 4);

        // ... and the plane really turned: it now hides by X and ignores Z entirely.
        CrossSectionPlane plane = window.ActiveCrossSection!.Value;
        Assert.True(plane.Hides(new System.Numerics.Vector3(50f, 0f, 11f)));
        Assert.False(plane.Hides(new System.Numerics.Vector3(-50f, 0f, 49f)));
    }

    [AvaloniaFact]
    public void LoadingASmallerModel_PullsThePlaneBackInsideIt()
    {
        // The section survives a file being opened, but its position cannot: a plane left at
        // z = 300 after a 2 mm part is loaded would hide the whole model and read as a blank
        // viewport with no explanation.
        var window = new MainWindow();
        window.LoadFileForTesting(WriteBox(20, 20, 600, zOffset: 0));
        EnableSection(window);
        window.SetCrossSectionSliderForTesting(300.0);
        Assert.Equal(300f, window.ActiveCrossSection!.Value.Position, 4);

        window.LoadFileForTesting(WriteBox(2, 2, 2, zOffset: 0));

        CrossSectionPlane plane = window.ActiveCrossSection!.Value;
        Assert.InRange(plane.Position, 0f, 2f);
        Assert.False(plane.Hides(new System.Numerics.Vector3(0, 0, 0.01f)));
        Assert.True(plane.Hides(new System.Numerics.Vector3(0, 0, 1.99f)));
    }

    [AvaloniaFact]
    public void TheSectionDoesNotTouchTheMeshOrTheUndoStack()
    {
        // The whole justification for doing this in the shader is that it is a way of looking,
        // not an edit. If it ever became one, undo would fill with view changes.
        var window = new MainWindow();
        window.LoadFileForTesting(WriteBox(20, 20, 40, zOffset: 10));
        var document = (Meshwright.Core.MeshDocument)GetField(window, "_document")!;
        int trianglesBefore = document.Mesh!.TriangleCount;
        bool couldUndoBefore = document.CanUndo;

        EnableSection(window);
        window.SetCrossSectionSliderForTesting(20.0);
        Flip(window);

        Assert.Equal(trianglesBefore, document.Mesh!.TriangleCount);
        Assert.Equal(couldUndoBefore, document.CanUndo);
    }

    [AvaloniaFact]
    public void AModelFlatAlongTheSectionAxis_StillGivesTheSliderSomewhereToGo()
    {
        // A plate has zero extent along one axis; without a floor the slider would have Minimum
        // equal to Maximum and could not be dragged at all.
        var window = new MainWindow();
        window.LoadFileForTesting(WriteBox(50, 50, 0, zOffset: 0));
        EnableSection(window);
        var slider = (Slider)GetField(window, "CrossSectionSlider")!;

        Assert.True(slider.Maximum > slider.Minimum, "A flat model left the slider with no travel.");
    }

    private static void EnableSection(MainWindow window) => Toggle(window);

    private static void Toggle(MainWindow window)
    {
        var toggle = (MenuItem)GetField(window, "ShowCrossSectionMenuItem")!;
        toggle.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static void Flip(MainWindow window)
    {
        var button = (Button)GetField(window, "CrossSectionFlipButton")!;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    /// <summary>Writes a temp STL of an axis-aligned box centred on X/Y, based at Z=zOffset.</summary>
    private string WriteBox(double width, double depth, double height, double zOffset)
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
