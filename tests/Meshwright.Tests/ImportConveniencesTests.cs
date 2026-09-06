using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using g3;
using Meshwright.App;
using Meshwright.Core.Settings;
using Meshwright.IO.Stl;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// The import conveniences as a user meets them: a recent-files menu that outlives the session, a
/// window you can drop a file onto, and a question about units that never answers itself.
///
/// <para>
/// Two things these are deliberately shaped around. The drag-and-drop tests raise the real routed
/// event on a <em>child</em> control and expect the window's handler to see it, because the
/// failure they exist to catch is a drop target attached to the one control that never receives
/// the event — on Linux the GL surface does not reliably take part in Avalonia's input routing,
/// which is why the viewport already needs a transparent overlay to get pointer input at all. And
/// the unit tests assert on the <em>mesh</em>, not just on the bar: the offer being visible is
/// only half the claim, and the other half is that nothing has been scaled yet.
/// </para>
/// </summary>
public class ImportConveniencesTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"meshwright-import-{Guid.NewGuid():N}");

    // ---- Recent files ------------------------------------------------------------------

    [AvaloniaFact]
    public void WithNothingOpenedYet_TheMenuSaysSoRatherThanBeingEmpty()
    {
        var window = NewWindow();

        Assert.Empty(window.RecentFileMenuPaths);
        Assert.Equal("No recent files", RecentMenuItems(window).Single().Header);
    }

    [AvaloniaFact]
    public void OpeningAFile_PutsItAtTheTopOfTheMenu()
    {
        var window = NewWindow();
        string first = WriteBox("first.stl", 30);
        string second = WriteBox("second.stl", 30);

        window.LoadFileForTesting(first);
        window.LoadFileForTesting(second);

        Assert.Equal(new[] { second, first }, window.RecentFileMenuPaths);
    }

    [AvaloniaFact]
    public void TheListOutlivesTheWindow()
    {
        // The whole point of the settings file. Asserted through a second window built on the same
        // store, which is the same path the app takes on its next launch.
        string path = WriteBox("remembered.stl", 30);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");

        var first = new MainWindow(new SettingsStore(settingsPath));
        first.LoadFileForTesting(path);

        var second = new MainWindow(new SettingsStore(settingsPath));

        Assert.Equal(new[] { path }, second.RecentFileMenuPaths);
    }

    [AvaloniaFact]
    public void ClickingARecentEntry_ActuallyOpensThatMesh()
    {
        // Ends at the loaded geometry, not at the status text: a menu that says it opened a file
        // and leaves the previous mesh on screen is the failure this repo keeps finding.
        var window = NewWindow();
        string small = WriteBox("small.stl", 10);
        string large = WriteBox("large.stl", 90);
        window.LoadFileForTesting(small);
        window.LoadFileForTesting(large);

        Assert.Equal(90.0, BoundsOf(window).Width, 3);

        window.ClickRecentFileForTesting(1);

        Assert.Equal(10.0, BoundsOf(window).Width, 3);
        Assert.Equal(new[] { small, large }, window.RecentFileMenuPaths);
    }

    [AvaloniaFact]
    public void AnEntryWhoseFileHasGone_SaysSoAndDropsItself()
    {
        var window = NewWindow();
        string path = WriteBox("deleted.stl", 30);
        window.LoadFileForTesting(path);
        File.Delete(path);

        window.ClickRecentFileForTesting(0);

        Assert.Empty(window.RecentFileMenuPaths);
        Assert.Contains("no longer", window.StatusMessage!);
    }

    [AvaloniaFact]
    public void AFileThatFailsToOpen_DoesNotStayInTheList()
    {
        // A file that exists but is not a mesh would otherwise sit at the top of the menu failing
        // every time it is picked.
        var window = NewWindow();
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "truncated.stl");
        File.WriteAllText(path, "not an stl");

        window.LoadFileForTesting(path);

        Assert.Empty(window.RecentFileMenuPaths);
        Assert.Contains("Failed to load", window.StatusMessage!);
    }

    [AvaloniaFact]
    public void ClearRecentFiles_EmptiesTheMenuAndTheFile()
    {
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var window = new MainWindow(new SettingsStore(settingsPath));
        window.LoadFileForTesting(WriteBox("a.stl", 30));

        ClearRecentItem(window).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Empty(window.RecentFileMenuPaths);
        Assert.Empty(new SettingsStore(settingsPath).Load().RecentFiles);
    }

    [AvaloniaFact]
    public void TwoFilesOfTheSameName_AreToldApartByTheirFolder()
    {
        var window = NewWindow();
        string here = WriteBox("model.stl", 30);
        Directory.CreateDirectory(Path.Combine(_tempDirectory, "other"));
        string there = WriteBox(Path.Combine("other", "model.stl"), 30);

        window.LoadFileForTesting(here);
        window.LoadFileForTesting(there);

        string[] headers = RecentMenuItems(window).Select(item => (string)item.Header!).ToArray();
        Assert.All(headers, header => Assert.Contains("model.stl", header));
        Assert.Equal(headers.Length, headers.Distinct().Count());
    }

    // ---- The rest of the settings file's customers -------------------------------------

    [AvaloniaFact]
    public void ThePrinterBedAndThePlateSwitch_OutliveTheWindowToo()
    {
        // Both shipped as in-session fields with a comment saying they belonged here (§11).
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        var first = new MainWindow(new SettingsStore(settingsPath));
        var mini = Meshwright.Geometry.Printing.BuildVolume.Presets[0];

        PresetItem(first, mini.Name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        ((MenuItem)GetField(first, "ShowBuildPlateMenuItem")!).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        var second = new MainWindow(new SettingsStore(settingsPath));

        Assert.Equal(mini, second.SelectedBuildVolume);
        Assert.False(((MenuItem)GetField(second, "ShowBuildPlateMenuItem")!).IsChecked);
    }

    [AvaloniaFact]
    public void ARememberedPrinterThatNoLongerExists_FallsBackToTheDefault()
    {
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        new SettingsStore(settingsPath).Save(new AppSettings { BuildVolumeName = "Some printer from 2031" });

        var window = new MainWindow(new SettingsStore(settingsPath));

        Assert.Equal(Meshwright.Geometry.Printing.BuildVolume.Default, window.SelectedBuildVolume);
    }

    [AvaloniaFact]
    public void ARememberedWindowSize_IsRestored()
    {
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        new SettingsStore(settingsPath).Save(new AppSettings
        {
            Window = new WindowPlacement(200, 150, 1024, 700, Maximized: false),
        });

        var window = new MainWindow(new SettingsStore(settingsPath));

        Assert.Equal(1024, window.Width);
        Assert.Equal(700, window.Height);
    }

    [AvaloniaFact]
    public void ANonsenseWindowRecord_IsIgnoredRatherThanRestored()
    {
        // Restoring a 4x4 window brings the app back invisible, with no way to fix it but to find
        // and delete the settings file.
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        new SettingsStore(settingsPath).Save(new AppSettings
        {
            Window = new WindowPlacement(0, 0, 4, 4, Maximized: false),
        });

        var window = new MainWindow(new SettingsStore(settingsPath));

        Assert.Equal(1400, window.Width);
        Assert.Equal(768, window.Height);
    }

    [AvaloniaFact]
    public void ASettingsFileThatCouldNotBeRead_IsSaidOutLoud()
    {
        Directory.CreateDirectory(_tempDirectory);
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        File.WriteAllText(settingsPath, "{ broken");

        var window = new MainWindow(new SettingsStore(settingsPath));

        Assert.Contains("Couldn't read settings", window.StatusMessage!);
    }

    // ---- Drag and drop -----------------------------------------------------------------

    [AvaloniaFact]
    public void TheWindowAcceptsDrops()
    {
        var window = NewWindow();

        Assert.True(DragDrop.GetAllowDrop(window));
    }

    [AvaloniaFact]
    public void DraggingAMeshOverTheViewport_IsOfferedAsAnOpen()
    {
        // Raised on the viewport's own overlay so it has to bubble to the window's handler. A
        // handler attached to MeshViewportControl would never see this - the same routing gap the
        // overlay itself exists to work around.
        var window = ShownWindow();
        string path = WriteBox("droppable.stl", 30);

        DragEventArgs args = RaiseOverViewport(window, DragDrop.DragOverEvent, FileTransfer(path));

        Assert.Equal(DragDropEffects.Copy, args.DragEffects);
        Assert.Equal("Open droppable.stl", window.DropHintMessage);
    }

    [AvaloniaFact]
    public void DraggingSomethingElse_IsRefusedInWords()
    {
        // A window that simply declines a drop leaves the user guessing whether the app is broken,
        // the file is wrong, or the drag missed.
        var window = ShownWindow();
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "notes.txt");
        File.WriteAllText(path, "hello");

        DragEventArgs args = RaiseOverViewport(window, DragDrop.DragOverEvent, FileTransfer(path));

        Assert.Equal(DragDropEffects.None, args.DragEffects);
        Assert.Contains("can't open notes.txt", window.DropHintMessage!);
        Assert.Contains("STL and OBJ", window.DropHintMessage!);
    }

    [AvaloniaFact]
    public void LeavingTheWindowMidDrag_TakesTheHintDown()
    {
        var window = ShownWindow();
        RaiseOverViewport(window, DragDrop.DragOverEvent, FileTransfer(WriteBox("hint.stl", 30)));
        Assert.NotNull(window.DropHintMessage);

        RaiseOverViewport(window, DragDrop.DragLeaveEvent, FileTransfer(WriteBox("hint2.stl", 30)));

        Assert.Null(window.DropHintMessage);
    }

    [AvaloniaFact]
    public void DroppingAMesh_OpensItAndRemembersIt()
    {
        var window = ShownWindow();
        string path = WriteBox("dropped.stl", 44);

        RaiseOverViewport(window, DragDrop.DropEvent, FileTransfer(path));

        Assert.Equal(44.0, BoundsOf(window).Width, 3);
        Assert.Equal(new[] { path }, window.RecentFileMenuPaths);
        Assert.Null(window.DropHintMessage);
    }

    [AvaloniaFact]
    public void AUriListDrop_WorksToo()
    {
        // What an X11 source that offers only text/uri-list sends, including the percent-escaping
        // that would otherwise make "broken cube.stl" arrive with an extension of ".stl" hidden
        // behind "%20" - or not arrive at all.
        var window = ShownWindow();
        string path = WriteBox("spaced name.stl", 26);
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText(new Uri(path).AbsoluteUri + "\r\n"));

        RaiseOverViewport(window, DragDrop.DropEvent, transfer);

        Assert.Equal(26.0, BoundsOf(window).Width, 3);
    }

    [AvaloniaFact]
    public void DroppingSeveralFiles_OpensOneAndSaysTheRestWereIgnored()
    {
        var window = ShownWindow();
        string first = WriteBox("one.stl", 33);
        string second = WriteBox("two.stl", 55);

        RaiseOverViewport(window, DragDrop.DropEvent, FileTransfer(first, second));

        Assert.Equal(33.0, BoundsOf(window).Width, 3);
        Assert.Contains("ignored", window.StatusMessage!);
    }

    [AvaloniaFact]
    public void DroppingSomethingUnopenable_ChangesNothingAndSaysWhy()
    {
        var window = ShownWindow();
        window.LoadFileForTesting(WriteBox("keep.stl", 70));
        Directory.CreateDirectory(_tempDirectory);
        string path = Path.Combine(_tempDirectory, "readme.md");
        File.WriteAllText(path, "# hello");

        RaiseOverViewport(window, DragDrop.DropEvent, FileTransfer(path));

        Assert.Equal(70.0, BoundsOf(window).Width, 3);
        Assert.Contains("can't open readme.md", window.StatusMessage!);
    }

    // ---- The unit question -------------------------------------------------------------

    [AvaloniaFact]
    public void AnOrdinarySizedImport_SaysNothingAtAll()
    {
        var window = NewWindow();

        window.LoadFileForTesting(WriteBox("normal.stl", 60));

        Assert.Null(window.UnitSuggestionMessage);
    }

    [AvaloniaFact]
    public void ASuspiciouslySmallImport_IsAskedAbout_AndIsNotTouched()
    {
        // The half that matters. Multiplying by 25.4 on open would look identical on screen - the
        // camera frames whatever it is given - and be discovered at the printer.
        var window = NewWindow();

        window.LoadFileForTesting(WriteBox("inches.stl", 2));

        Assert.NotNull(window.UnitSuggestionMessage);
        Assert.Contains("inches", window.UnitSuggestionMessage!);
        Assert.Equal(2.0, BoundsOf(window).Width, 4);
    }

    [AvaloniaFact]
    public async Task AcceptingTheOffer_ScalesByExactly254_AndIsUndoable()
    {
        var window = NewWindow();
        window.LoadFileForTesting(WriteBox("inches.stl", 2));

        await window.AcceptUnitSuggestionForTesting();

        Assert.Equal(50.8, BoundsOf(window).Width, 4);
        Assert.Null(window.UnitSuggestionMessage);

        window.TriggerUndoForTesting();

        Assert.Equal(2.0, BoundsOf(window).Width, 4);
    }

    [AvaloniaFact]
    public async Task AcceptingTheOffer_ScalesAboutTheOrigin_NotAboutTheModel()
    {
        // Reinterpreting a unit changes what every number in the file meant, including how far the
        // model sits from the origin. Scaling about the centroid would leave a part modelled 3
        // inches off centre sitting 3 mm off centre - a different model from the one in the file.
        var window = NewWindow();
        window.LoadFileForTesting(WriteBox("offset.stl", 2, zOffset: 3));

        await window.AcceptUnitSuggestionForTesting();

        Assert.Equal(3 * 25.4, BoundsOf(window).Min.z, 3);
    }

    [AvaloniaFact]
    public void DecliningTheOffer_LeavesTheModelAndTakesTheBarDown()
    {
        var window = NewWindow();
        window.LoadFileForTesting(WriteBox("inches.stl", 2));

        window.DismissUnitSuggestionForTesting();

        Assert.Null(window.UnitSuggestionMessage);
        Assert.Equal(2.0, BoundsOf(window).Width, 4);
    }

    [AvaloniaFact]
    public void OpeningAnOrdinaryFileAfterASuspiciousOne_TakesTheOfferDown()
    {
        // A bar left over from the previous model is a bar describing something that is no longer
        // on screen - the same defect the cross-section slider shipped with (§11).
        var window = NewWindow();
        window.LoadFileForTesting(WriteBox("inches.stl", 2));
        Assert.NotNull(window.UnitSuggestionMessage);

        window.LoadFileForTesting(WriteBox("normal.stl", 60));

        Assert.Null(window.UnitSuggestionMessage);
    }

    [AvaloniaFact]
    public void TheSampleMeshOnStartup_IsNotAskedAbout()
    {
        // It is a 1-unit tetrahedron nobody chose to open; asking about its units on every launch
        // would be asking a question with no answer.
        var window = NewWindow();

        Assert.Null(window.UnitSuggestionMessage);
    }

    [AvaloniaFact]
    public void TheOfferCanBeSwitchedOffInSettings()
    {
        string settingsPath = Path.Combine(_tempDirectory, "settings.json");
        new SettingsStore(settingsPath).Save(new AppSettings { OfferUnitScaling = false });
        var window = new MainWindow(new SettingsStore(settingsPath));

        window.LoadFileForTesting(WriteBox("inches.stl", 2));

        Assert.Null(window.UnitSuggestionMessage);
        Assert.Equal(2.0, BoundsOf(window).Width, 4);
    }

    // ---- Helpers -----------------------------------------------------------------------

    private MainWindow NewWindow() => new(new SettingsStore(Path.Combine(_tempDirectory, "settings.json")));

    private MainWindow ShownWindow()
    {
        MainWindow window = NewWindow();
        window.Show();
        return window;
    }

    private static AxisAlignedBox3d BoundsOf(MainWindow window) =>
        window.CurrentReport!.Statistics.BoundingBox;

    private static MenuItem[] RecentMenuItems(MainWindow window) =>
        ((MenuItem)GetField(window, "RecentFilesMenuItem")!).Items.OfType<MenuItem>()
            .Where(item => item.Header as string != "_Clear Recent Files")
            .ToArray();

    private static MenuItem ClearRecentItem(MainWindow window) =>
        ((MenuItem)GetField(window, "RecentFilesMenuItem")!).Items.OfType<MenuItem>()
            .First(item => item.Header as string == "_Clear Recent Files");

    private static MenuItem PresetItem(MainWindow window, string name) =>
        ((MenuItem)GetField(window, "BuildPlateMenuItem")!).Items.OfType<MenuItem>()
            .First(item => item.Tag is Meshwright.Geometry.Printing.BuildVolume volume && volume.Name == name);

    /// <summary>
    /// Raises a real drag event on the viewport's input overlay, so it has to route up to the
    /// window's handler to be seen at all. <see cref="DragEventArgs"/>' constructors are internal
    /// to Avalonia, hence the reflection: the alternative is calling the handler directly, which
    /// would pass just as happily with the handler attached to a control that never receives the
    /// event - the exact defect these tests exist for.
    /// </summary>
    private static DragEventArgs RaiseOverViewport(MainWindow window, RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data)
    {
        var overlay = (Control)GetField(window, "ViewportInputOverlay")!;
        ConstructorInfo constructor = typeof(DragEventArgs)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single(c => c.GetParameters().Length == 5 && c.GetParameters()[1].ParameterType == typeof(IDataTransfer));

        var args = (DragEventArgs)constructor.Invoke(new object[] { routedEvent, data, overlay, new Point(10, 10), KeyModifiers.None });
        overlay.RaiseEvent(args);
        return args;
    }

    private static IDataTransfer FileTransfer(params string[] paths)
    {
        var transfer = new DataTransfer();
        foreach (string path in paths)
        {
            transfer.Add(DataTransferItem.CreateFile(DroppedTestFile.At(path)));
        }

        return transfer;
    }

    private string WriteBox(string name, double size, double zOffset = 0)
    {
        string path = Path.Combine(_tempDirectory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        StlWriter.WriteFile(path, Box(size, size, size, zOffset));
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
