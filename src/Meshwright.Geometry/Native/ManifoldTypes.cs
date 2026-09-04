// ManifoldTypes.cs — C# structs mirroring Manifold's C API opaque types and value types
// from bindings/c/include/manifold/types.h, for use with P/Invoke.

using System.Runtime.InteropServices;

namespace Meshwright.Geometry.Native;

/// Opaque pointer to a Manifold mesh object (allocated/freed by manifold_* functions).
[StructLayout(LayoutKind.Sequential)]
public struct ManifoldManifold
{
    public nint Handle;
}

/// Opaque pointer to a MeshGL64 object (double-precision mesh).
[StructLayout(LayoutKind.Sequential)]
public struct ManifoldMeshGL64
{
    public nint Handle;
}

/// Opaque pointer to an error/execution context.
[StructLayout(LayoutKind.Sequential)]
public struct ManifoldExecutionContext
{
    public nint Handle;
}

/// 3D double-precision vector (from manifold/types.h ManifoldVec3).
[StructLayout(LayoutKind.Sequential)]
public struct ManifoldVec3
{
    public double X;
    public double Y;
    public double Z;

    public ManifoldVec3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}

/// 3D bounding box.
[StructLayout(LayoutKind.Sequential)]
public struct ManifoldBox
{
    public nint Handle;
}

/// Boolean operation type (Union, Difference, Intersection).
public enum ManifoldOpType : int
{
    Add = 0,        // Union
    Subtract = 1,   // Difference
    Intersect = 2   // Intersection
}

/// Manifold error status codes.
public enum ManifoldError : int
{
    NoError = 0,
    NonFiniteVertex = 1,
    NotManifold = 2,
    VertexIndexOutOfBounds = 3,
    PropertiesWrongLength = 4,
    MissingPositionProperties = 5,
    MergeVectorsDifferentLengths = 6,
    MergeIndexOutOfBounds = 7,
    TransformWrongLength = 8,
    RunIndexWrongLength = 9,
    FaceIdWrongLength = 10,
    InvalidConstruction = 11,
    ResultTooLarge = 12,
    InvalidTangents = 13,
    Cancelled = 14
}
