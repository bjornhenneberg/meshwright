using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Meshwright.Tests;

/// <summary>
/// Points every <c>MainWindow</c> the suite constructs at a throwaway settings file.
///
/// <para>
/// <c>MainWindow</c> now reads persisted state on construction — the printer bed, whether the
/// build plate is drawn, the recent-files list — and hundreds of tests build one. Without this,
/// every one of those tests would read the settings of whoever is running them, so a developer who
/// had hidden the build plate would see <c>BuildPlateMenuTests</c> fail on their machine and pass
/// in CI, and the suite would rewrite their recent-files list as it went. The environment variable
/// is read by <c>SettingsStore.DefaultFilePath</c>.
/// </para>
/// </summary>
internal static class SettingsIsolation
{
    [ModuleInitializer]
    internal static void RedirectSettingsFile()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"meshwright-test-settings-{Environment.ProcessId}-{Guid.NewGuid():N}",
            "settings.json");

        Environment.SetEnvironmentVariable("MESHWRIGHT_SETTINGS_FILE", path);
    }
}
