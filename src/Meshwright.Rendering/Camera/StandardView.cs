namespace Meshwright.Rendering.Camera;

/// <summary>
/// The canonical camera orientations offered by the View menu. Names are read in the print-bed
/// frame this project uses throughout (Z up, +Y "back", so the front of a model faces -Y): a
/// <see cref="Front"/> camera sits on -Y looking toward +Y, and <see cref="Top"/> looks straight
/// down -Z with +Y up the screen.
/// </summary>
public enum StandardView
{
    Top,
    Bottom,
    Front,
    Back,
    Left,
    Right,

    /// <summary>
    /// A three-quarter view showing three faces at once. Deliberately identical to the orientation
    /// <see cref="OrbitCamera.Frame"/> restores, so Isometric and Reset View do not differ by a few
    /// degrees for no reason a user could name.
    /// </summary>
    Isometric,
}
