namespace Meshwright.Rendering.Camera;

/// <summary>
/// How <see cref="OrbitCamera.GetProjectionMatrix"/> maps the view frustum to clip space.
/// </summary>
public enum ProjectionMode
{
    /// <summary>Perspective: distant geometry appears smaller. The default, and what an eye sees.</summary>
    Perspective,

    /// <summary>
    /// Orthographic: no perspective divide, so two objects of equal size project to equal screen
    /// size whatever their depth. What measuring and aligning want — a plane cut through a model
    /// seen edge-on is a straight line rather than a trapezoid.
    /// </summary>
    Orthographic,
}
