namespace Meshwright.Geometry.Diagnostics;

/// <summary>How much a detected issue matters for printing/slicing.</summary>
public enum MeshIssueSeverity
{
    /// <summary>Cosmetic or informational only; the mesh will still print/slice fine.</summary>
    Info,

    /// <summary>Minor/cosmetic problem; the mesh will likely still print/slice.</summary>
    Warning,

    /// <summary>The mesh will likely fail to print/slice unless this is fixed.</summary>
    Error,
}
