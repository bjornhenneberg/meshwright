using System;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Meshwright.Tests.Gpu;

/// <summary>Thrown when a real, current OpenGL context could not be created on this machine.</summary>
public sealed class GpuUnavailableException : Exception
{
    public GpuUnavailableException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Creates a real (hidden) GLFW window and OpenGL 3.3 core context via
/// <see cref="Silk.NET.Windowing"/>, bypassing Avalonia entirely, so
/// <see cref="Meshwright.Rendering.GL.MeshRenderer"/> can be exercised against an actual driver.
/// Falls back to <see cref="IsAvailable"/> = false rather than throwing out of the constructor
/// so tests can skip gracefully on machines/CI without a GPU or display.
/// </summary>
public sealed class GpuTestFixture : IDisposable
{
    public const int Width = 64;
    public const int Height = 64;

    private IWindow? _window;

    public bool IsAvailable { get; }

    public string? UnavailableReason { get; }

    public GL? GL { get; }

    public GpuTestFixture()
    {
        try
        {
            var options = WindowOptions.Default;
            options.IsVisible = false;
            options.Size = new Vector2D<int>(Width, Height);
            options.Title = "Meshwright GPU Test";
            options.API = new GraphicsAPI(
                ContextAPI.OpenGL,
                ContextProfile.Core,
                ContextFlags.Default,
                new APIVersion(3, 3));

            _window = Window.Create(options);
            _window.Initialize();

            if (_window.GLContext is null)
            {
                throw new GpuUnavailableException("Window.GLContext was null after Initialize().");
            }

            _window.GLContext.MakeCurrent();
            GL = GL.GetApi(_window);
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            UnavailableReason = ex.Message;
            _window?.Dispose();
            _window = null;
            GL = null;
        }
    }

    public void Dispose()
    {
        GL?.Dispose();
        _window?.Dispose();
        _window = null;
    }
}
