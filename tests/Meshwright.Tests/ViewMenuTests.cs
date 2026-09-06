using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Meshwright.App;
using Meshwright.App.Views;
using Meshwright.Rendering.Camera;
using Meshwright.Rendering.GL;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// The View menu's camera and display entries, driven the way a user drives them: by clicking the
/// menu item and then reading the viewport's state.
///
/// <para>
/// §11 (2026-09-06) records three controls that parsed a value and never used it, each of which
/// shipped behind a green suite because the tests exercised the operation directly and nothing
/// asserted the wiring. These tests start at the menu item and end at the camera or the renderer,
/// so a preset that is never handed to the viewport fails here.
/// </para>
/// </summary>
public class ViewMenuTests
{
    /// <summary>Menu item field name, the <see cref="StandardView"/> it claims, and its hotkey.</summary>
    public static TheoryData<string, StandardView, Key> Presets() => new()
    {
        { "FrontViewMenuItem", StandardView.Front, Key.D1 },
        { "BackViewMenuItem", StandardView.Back, Key.D2 },
        { "LeftViewMenuItem", StandardView.Left, Key.D3 },
        { "RightViewMenuItem", StandardView.Right, Key.D4 },
        { "TopViewMenuItem", StandardView.Top, Key.D5 },
        { "BottomViewMenuItem", StandardView.Bottom, Key.D6 },
        { "IsometricViewMenuItem", StandardView.Isometric, Key.D7 },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Presets))]
    public void ClickingAPresetMenuItem_PointsTheCameraTheWayThePresetClaims(string menuItemName, StandardView view, Key _)
    {
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;
        var expected = new OrbitCamera();
        expected.Frame(viewport.Camera.Target, 1f);
        expected.SetStandardView(view);
        Vector3 expectedForward = Vector3.Normalize(expected.Target - expected.Position);

        Click(window, menuItemName);

        Vector3 actualForward = Vector3.Normalize(viewport.Camera.Target - viewport.Camera.Position);
        Assert.True(Vector3.Distance(expectedForward, actualForward) < 1e-4f,
            $"{menuItemName} pointed the camera along {actualForward}, not {expectedForward}.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Presets))]
    public void EveryPresetHotKey_ParsesToARealKey(string menuItemName, StandardView view, Key expectedKey)
    {
        // Regression guard for the Ctrl+0 defect: "Ctrl+1" parses to Key.None because Avalonia's
        // gesture parser wants the digit key's enum name ("D1"), so the shortcut binds to nothing
        // at all and fails silently. One assertion per new shortcut, since each is its own typo.
        _ = view;
        var window = new MainWindow();
        var menuItem = (MenuItem)GetField(window, menuItemName)!;

        KeyGesture? hotKey = HotKeyManager.GetHotKey(menuItem);

        Assert.NotNull(hotKey);
        Assert.Equal(expectedKey, hotKey!.Key);
        Assert.Equal(KeyModifiers.Control, hotKey.KeyModifiers);
    }

    [AvaloniaFact]
    public void PresetShortcut_PointsTheCamera_LikeTheMenuItemDoes()
    {
        // A separate entry point (Avalonia's HotKey routing) into the same handler.
        var window = new MainWindow();
        window.Show();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;

        window.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.Control);

        Vector3 forward = Vector3.Normalize(viewport.Camera.Target - viewport.Camera.Position);
        Assert.True(Vector3.Distance(new Vector3(0f, 0f, -1f), forward) < 1e-4f,
            $"Ctrl+5 must look straight down for the top view; it looked along {forward}.");
    }

    [AvaloniaFact]
    public void ClickingAPreset_KeepsTheFraming_AndResetViewStillReturnsToIt()
    {
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;
        Vector3 framedPosition = viewport.Camera.Position;
        Vector3 framedTarget = viewport.Camera.Target;
        float framedDistance = viewport.Camera.Distance;

        Click(window, "TopViewMenuItem");

        Assert.Equal(framedTarget, viewport.Camera.Target);
        Assert.Equal(framedDistance, viewport.Camera.Distance);
        Assert.NotEqual(framedPosition, viewport.Camera.Position);

        Click(window, "ResetViewMenuItem");

        Assert.Equal(framedPosition.X, viewport.Camera.Position.X, 4);
        Assert.Equal(framedPosition.Y, viewport.Camera.Position.Y, 4);
        Assert.Equal(framedPosition.Z, viewport.Camera.Position.Z, 4);
    }

    [AvaloniaFact]
    public void ClickingOrthographic_SwitchesTheProjectionTheViewportRenders()
    {
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;

        Assert.Equal(ProjectionMode.Perspective, viewport.ProjectionMode);
        Assert.Equal(-1f, viewport.Camera.GetProjectionMatrix(1.5f).M34);

        Click(window, "OrthographicMenuItem");

        Assert.Equal(ProjectionMode.Orthographic, viewport.ProjectionMode);

        // The state that matters is the matrix the renderer and the pick path are handed, not the
        // enum: M34 is the perspective-divide term, zero only for a true orthographic projection.
        Assert.Equal(0f, viewport.Camera.GetProjectionMatrix(1.5f).M34);

        Click(window, "PerspectiveMenuItem");

        Assert.Equal(ProjectionMode.Perspective, viewport.ProjectionMode);
        Assert.Equal(-1f, viewport.Camera.GetProjectionMatrix(1.5f).M34);
    }

    [AvaloniaTheory]
    [InlineData("WireframeDisplayMenuItem", MeshDisplayMode.Wireframe)]
    [InlineData("XRayDisplayMenuItem", MeshDisplayMode.XRay)]
    [InlineData("ShadedDisplayMenuItem", MeshDisplayMode.Shaded)]
    public void ClickingADisplayModeMenuItem_SetsTheViewportsDisplayMode(string menuItemName, MeshDisplayMode expected)
    {
        var window = new MainWindow();
        var viewport = (MeshViewportControl)GetField(window, "Viewport")!;

        Click(window, menuItemName);

        Assert.Equal(expected, viewport.DisplayMode);
    }

    [AvaloniaFact]
    public void ProjectionAndDisplayMenuItems_AreRadioGroupsSoTheCheckmarksTrackTheRealState()
    {
        var window = new MainWindow();

        foreach (string name in new[] { "PerspectiveMenuItem", "OrthographicMenuItem" })
        {
            Assert.Equal(MenuItemToggleType.Radio, ((MenuItem)GetField(window, name)!).ToggleType);
        }

        foreach (string name in new[] { "ShadedDisplayMenuItem", "WireframeDisplayMenuItem", "XRayDisplayMenuItem" })
        {
            Assert.Equal(MenuItemToggleType.Radio, ((MenuItem)GetField(window, name)!).ToggleType);
        }

        Assert.True(((MenuItem)GetField(window, "PerspectiveMenuItem")!).IsChecked);
        Assert.True(((MenuItem)GetField(window, "ShadedDisplayMenuItem")!).IsChecked);
    }

    [AvaloniaFact]
    public void EveryViewMenuHotKey_IsUniqueAcrossTheWholeMenu()
    {
        // Seven presets, two projections and three display modes were added at once; a duplicate
        // gesture would leave one of them dead with nothing to show for it.
        var window = new MainWindow();
        var seen = new HashSet<string>();

        foreach (MenuItem item in AllMenuItems(window))
        {
            KeyGesture? hotKey = HotKeyManager.GetHotKey(item);
            if (hotKey is null)
            {
                continue;
            }

            Assert.NotEqual(Key.None, hotKey.Key);
            Assert.True(seen.Add(hotKey.ToString()), $"Duplicate hotkey {hotKey} on '{item.Header}'.");
        }
    }

    private static IEnumerable<MenuItem> AllMenuItems(MainWindow window)
    {
        foreach (FieldInfo field in typeof(MainWindow).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (field.GetValue(window) is MenuItem item)
            {
                yield return item;
            }
        }
    }

    private static void Click(MainWindow window, string menuItemName)
    {
        var item = (MenuItem)GetField(window, menuItemName)!;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static object? GetField(object instance, string name)
    {
        FieldInfo field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingFieldException(instance.GetType().FullName, name);
        return field.GetValue(instance);
    }
}
