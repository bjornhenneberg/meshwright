using Avalonia;
using Avalonia.Headless;
using Meshwright.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Meshwright.Tests;

/// <summary>Builds an Avalonia app configured for headless test execution (no real GPU/display).</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Meshwright.App.App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
