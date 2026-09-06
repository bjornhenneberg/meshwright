namespace Meshwright.Rendering.GL;

/// <summary>
/// How <see cref="MeshRenderer"/> draws the mesh surface. Orthogonal to the error highlight, which
/// is driven by the flags passed to <see cref="MeshRenderer.UploadMesh"/> and stays visible in
/// every mode.
/// </summary>
public enum MeshDisplayMode
{
    /// <summary>Opaque, Lambertian-shaded triangles. The default.</summary>
    Shaded,

    /// <summary>
    /// Triangle edges only, with no fill — the way to see topology, tessellation density and what
    /// a decimation actually did.
    /// </summary>
    Wireframe,

    /// <summary>
    /// Semi-transparent surface with depth writes off, so geometry inside or behind the model —
    /// a stray internal shell, the far wall of a hollowed part, a drain hole's far side — is
    /// visible through it.
    /// </summary>
    XRay,
}
