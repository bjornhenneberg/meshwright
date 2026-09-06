using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Platform.Storage;

namespace Meshwright.Tests;

/// <summary>
/// Builds the <see cref="IStorageFile"/> a file drag carries.
///
/// <para>
/// Avalonia refuses to let application code implement <see cref="IStorageFile"/> — the interface
/// carries a member whose name is the sentence "This interface or abstract class is -not-
/// implementable by user code !" — and the headless backend has no storage provider to ask. What
/// it does have is the same <c>BclStorageFile</c> its desktop backends wrap a real path in, which
/// is exactly what an X11 file drop arrives as; it is internal, so this reaches it by reflection
/// rather than substituting a different shape of object and testing something the app never sees.
/// </para>
/// </summary>
internal static class DroppedTestFile
{
    private static readonly Type BclStorageFile =
        typeof(IStorageFile).Assembly.GetType("Avalonia.Platform.Storage.FileIO.BclStorageFile", throwOnError: true)!;

    public static IStorageFile At(string path)
    {
        ConstructorInfo constructor = BclStorageFile
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(c => c.GetParameters() is [{ ParameterType.Name: nameof(FileInfo) }]);

        return (IStorageFile)constructor.Invoke(new object[] { new FileInfo(path) });
    }
}
