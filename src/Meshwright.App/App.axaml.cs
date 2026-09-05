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

            // "meshwright model.stl" — also what a file manager passes for "Open with".
            // Deferred to Opened so a load failure reports through the window's own status
            // line rather than taking the process down before anything is on screen.
            if (desktop.Args is { Length: > 0 } args && !string.IsNullOrWhiteSpace(args[0]))
            {
                string path = args[0];
                window.Opened += (_, _) => window.OpenFileFromPath(path);
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
