using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Meshwright.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            // TEMPORARY (screenshot capture): load a mesh from argv, revert after.
            if (desktop.Args is { Length: > 0 } args && System.IO.File.Exists(args[0]))
            {
                window.Opened += (_, _) => window.LoadFileForTesting(args[0]);
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
