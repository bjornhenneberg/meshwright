using System.Numerics;

namespace Meshwright.Rendering.GL;

/// <summary>Which world axis a <see cref="CrossSectionPlane"/> cuts along.</summary>
public enum CrossSectionAxis
{
    X,
    Y,
    Z,
}

/// <summary>
/// A non-destructive section plane for the viewport: the model is drawn with everything on one
/// side of it hidden, so the inside is visible without changing a single triangle.
///
/// <para>
/// It is deliberately <em>not</em> a plane cut. A cut rewrites the mesh, pushes an undo entry and
/// caps the opening; this only decides which fragments reach the screen, so it costs the same on a
/// 14-triangle cube and a 140,000-triangle tower and can be dragged continuously.
/// </para>
///
/// <para>
/// <see cref="Position"/> is a world coordinate in millimetres along <see cref="Axis"/>, not a
/// 0-1 fraction, so the number under the slider is the number a user would measure with callipers
/// and stays meaningful when the model is moved or scaled.
/// </para>
/// </summary>
public readonly record struct CrossSectionPlane(CrossSectionAxis Axis, float Position, bool Flipped)
{
    /// <summary>The unit world-space normal pointing into the hidden half.</summary>
    public Vector3 HiddenNormal
    {
        get
        {
            Vector3 axis = Axis switch
            {
                CrossSectionAxis.X => Vector3.UnitX,
                CrossSectionAxis.Y => Vector3.UnitY,
                _ => Vector3.UnitZ,
            };

            return Flipped ? -axis : axis;
        }
    }

    /// <summary>
    /// The plane as <c>(nx, ny, nz, d)</c>: a world point <c>p</c> is hidden when
    /// <c>dot(n, p) + d &gt; 0</c>. This is the exact expression the fragment shaders evaluate, so
    /// the CPU-side prediction of what is visible and the GPU's decision come from one formula.
    /// </summary>
    public Vector4 HiddenHalfSpace
    {
        get
        {
            Vector3 normal = HiddenNormal;
            return new Vector4(normal, -Vector3.Dot(normal, AxisPoint));
        }
    }

    /// <summary>Whether <paramref name="point"/> is hidden by this plane.</summary>
    public bool Hides(Vector3 point)
    {
        Vector4 plane = HiddenHalfSpace;
        return (plane.X * point.X) + (plane.Y * point.Y) + (plane.Z * point.Z) + plane.W > 0f;
    }

    private Vector3 AxisPoint => Axis switch
    {
        CrossSectionAxis.X => new Vector3(Position, 0f, 0f),
        CrossSectionAxis.Y => new Vector3(0f, Position, 0f),
        _ => new Vector3(0f, 0f, Position),
    };
}

/// <summary>
/// The slider's travel: how far a <see cref="CrossSectionPlane"/> can move along each axis for a
/// given model. Lives beside <see cref="CrossSectionPlane"/> so the axis-to-component mapping is
/// written once - a range taken along Y while the normal points along X is a plausible bug that
/// would put the slider's numbers and the plane on screen in different places.
/// </summary>
public static class CrossSectionRange
{
    /// <summary>The model's extent along <paramref name="axis"/>, in world millimetres.</summary>
    public static (double Min, double Max) Along(g3.AxisAlignedBox3d bounds, CrossSectionAxis axis) => axis switch
    {
        CrossSectionAxis.X => (bounds.Min.x, bounds.Max.x),
        CrossSectionAxis.Y => (bounds.Min.y, bounds.Max.y),
        _ => (bounds.Min.z, bounds.Max.z),
    };
}
