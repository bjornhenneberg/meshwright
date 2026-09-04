// ManifoldInterop.cs — P/Invoke declarations for libmanifoldc C API.
// Bindings for triangle-mesh boolean operations (union/difference/intersection).
//
// Manifold v3.5.2, MIT licensed (https://github.com/elalish/manifold).
// This interop layer calls into the C API via unmanaged P/Invoke, handling
// memory allocation/deallocation and conversion to/from g3.DMesh3.

using System;
using System.Runtime.InteropServices;

namespace Meshwright.Geometry.Native;

/// Low-level P/Invoke wrapper for Manifold's C API (libmanifoldc).
/// Handles memory allocation, struct conversions, and boolean operations.
public static class ManifoldInterop
{
    private const string LibraryName = "libmanifoldc";

    // --- Memory size queries (needed for stack allocation of opaque types) ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_manifold_size();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_meshgl64_size();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_box_size();

    // --- Manifold allocation and deletion ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldManifold manifold_alloc_manifold();

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void manifold_delete_manifold(ManifoldManifold m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void manifold_destruct_manifold(ManifoldManifold m);

    // --- MeshGL64 (double-precision mesh) construction and extraction ---

    /// Create a MeshGL64 from vertex properties (positions, densely packed as [x,y,z,...])
    /// and triangle indices (uint64 per vertex, 3 per triangle).
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldMeshGL64 manifold_meshgl64(
        nint mem,
        IntPtr vertProperties,  // double* flattened [x0, y0, z0, x1, y1, z1, ...]
        nuint nVerts,
        nuint nProps,           // 3 for position only
        IntPtr triVerts,        // uint64* flattened [v0, v1, v2, v0, v1, v2, ...]
        nuint nTris);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void manifold_delete_meshgl64(ManifoldMeshGL64 m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern void manifold_destruct_meshgl64(ManifoldMeshGL64 m);

    /// Extract MeshGL64 from a Manifold after boolean ops.
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldMeshGL64 manifold_get_meshgl64(nint mem, ManifoldManifold m);

    // --- Mesh info queries ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_meshgl64_num_vert(ManifoldMeshGL64 m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_meshgl64_num_tri(ManifoldMeshGL64 m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_meshgl64_vert_properties_length(ManifoldMeshGL64 m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_meshgl64_tri_length(ManifoldMeshGL64 m);

    /// Get vertex property array (copy): double* [x0, y0, z0, x1, y1, z1, ...]
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr manifold_meshgl64_vert_properties(nint mem, ManifoldMeshGL64 m);

    /// Get triangle index array (copy): uint64* [v0, v1, v2, ...]
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr manifold_meshgl64_tri_verts(nint mem, ManifoldMeshGL64 m);

    // --- Manifold operations ---

    /// Create a Manifold from a MeshGL64.
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldManifold manifold_of_meshgl64(nint mem, ManifoldMeshGL64 mesh);

    /// Boolean union of two manifolds.
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldManifold manifold_union(nint mem, ManifoldManifold a, ManifoldManifold b);

    /// Boolean difference (a - b).
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldManifold manifold_difference(nint mem, ManifoldManifold a, ManifoldManifold b);

    /// Boolean intersection of two manifolds.
    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldManifold manifold_intersection(nint mem, ManifoldManifold a, ManifoldManifold b);

    // --- Manifold info ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern int manifold_is_empty(ManifoldManifold m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldError manifold_status(ManifoldManifold m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_num_vert(ManifoldManifold m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern nuint manifold_num_tri(ManifoldManifold m);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern double manifold_volume(ManifoldManifold m);

    // --- Helper function to create a cube (for testing) ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    private static extern ManifoldManifold manifold_cube(nint mem, double x, double y, double z, int center);

    // --- Public C# wrapper methods ---

    /// Get the size of an uninitialized ManifoldManifold struct (for stack allocation).
    public static nuint GetManifoldSize() => manifold_manifold_size();

    /// Get the size of an uninitialized ManifoldMeshGL64 struct.
    public static nuint GetMeshGL64Size() => manifold_meshgl64_size();

    /// Get the size of a ManifoldBox struct.
    public static nuint GetBoxSize() => manifold_box_size();

    /// Create a cube manifold for testing (centered at origin).
    ///
    /// Note on memory ownership: manifold_cube (like manifold_union,
    /// manifold_of_meshgl64, etc.) placement-constructs its C++ object
    /// directly into the `mem` buffer we pass it - the returned handle *is*
    /// that same pointer, not a separate heap allocation copied from it. The
    /// buffer must stay alive for as long as the object is in use, so it is
    /// freed by DeleteManifold, not here.
    public static ManifoldManifold CreateCube(double sizeX, double sizeY, double sizeZ)
    {
        var mem = Marshal.AllocHGlobal((int)GetManifoldSize());
        return manifold_cube(mem, sizeX, sizeY, sizeZ, 1); // center=1
    }

    /// Create a MeshGL64 from raw vertex and triangle data.
    /// verts: flattened [x0, y0, z0, x1, y1, z1, ...]
    /// triangles: flattened [v0, v1, v2, v0, v1, v2, ...] (3 indices per triangle)
    ///
    /// The `mem` buffer backing the returned handle is owned by the caller
    /// for the object's lifetime (see the CreateCube note) - free it via
    /// DeleteMeshGL64, not here. vert_props/tri_verts are only read during
    /// the call (Manifold copies them into its own vectors), so those GC
    /// handles are safe to release once manifold_meshgl64 returns.
    public static ManifoldMeshGL64 CreateMeshGL64(double[] verts, ulong[] triangles)
    {
        var vertsHandle = GCHandle.Alloc(verts, GCHandleType.Pinned);
        var trisHandle = GCHandle.Alloc(triangles, GCHandleType.Pinned);

        try
        {
            var nVerts = (nuint)(verts.Length / 3);
            var nTris = (nuint)(triangles.Length / 3);
            var mem = Marshal.AllocHGlobal((int)GetMeshGL64Size());

            return manifold_meshgl64(
                mem,
                vertsHandle.AddrOfPinnedObject(),
                nVerts,
                3,  // 3 properties: x, y, z
                trisHandle.AddrOfPinnedObject(),
                nTris);
        }
        finally
        {
            vertsHandle.Free();
            trisHandle.Free();
        }
    }

    /// Convert a MeshGL64 to a Manifold object.
    public static ManifoldManifold MeshToManifold(ManifoldMeshGL64 mesh)
    {
        var mem = Marshal.AllocHGlobal((int)GetManifoldSize());
        return manifold_of_meshgl64(mem, mesh);
    }

    /// Extract a MeshGL64 from a Manifold.
    public static ManifoldMeshGL64 ManifoldToMesh(ManifoldManifold manifold)
    {
        var mem = Marshal.AllocHGlobal((int)GetMeshGL64Size());
        return manifold_get_meshgl64(mem, manifold);
    }

    /// Boolean union: a + b.
    public static ManifoldManifold Union(ManifoldManifold a, ManifoldManifold b)
    {
        var mem = Marshal.AllocHGlobal((int)GetManifoldSize());
        return manifold_union(mem, a, b);
    }

    /// Boolean difference: a - b.
    public static ManifoldManifold Difference(ManifoldManifold a, ManifoldManifold b)
    {
        var mem = Marshal.AllocHGlobal((int)GetManifoldSize());
        return manifold_difference(mem, a, b);
    }

    /// Boolean intersection: a ^ b.
    public static ManifoldManifold Intersection(ManifoldManifold a, ManifoldManifold b)
    {
        var mem = Marshal.AllocHGlobal((int)GetManifoldSize());
        return manifold_intersection(mem, a, b);
    }

    /// Get mesh info from a MeshGL64.
    ///
    /// Unlike the placement-construction functions above, manifold_meshgl64_vert_properties
    /// and manifold_meshgl64_tri_verts don't construct anything at `mem` - they just
    /// `memcpy` the requested data into it (see copy_data in bindings/c/conv.h), so
    /// `mem` must be a real caller-supplied buffer sized to hold the copy, not
    /// IntPtr.Zero (which made this a null-pointer memcpy destination and crashed).
    public static void ExtractMeshGL64(
        ManifoldMeshGL64 mesh,
        out double[] vertexPositions,
        out ulong[] triangleIndices)
    {
        // Vertex properties are packed as [x0, y0, z0, x1, y1, z1, ...] (3 per vertex)
        var vertPropsLen = manifold_meshgl64_vert_properties_length(mesh);
        var vertMemBuf = Marshal.AllocHGlobal((int)vertPropsLen * sizeof(double));
        try
        {
            var vertMem = manifold_meshgl64_vert_properties(vertMemBuf, mesh);
            vertexPositions = new double[vertPropsLen];
            Marshal.Copy(vertMem, vertexPositions, 0, (int)vertPropsLen);
        }
        finally
        {
            Marshal.FreeHGlobal(vertMemBuf);
        }

        // Triangle indices are packed as [v0, v1, v2, v0, v1, v2, ...] (3 per triangle)
        var triLen = manifold_meshgl64_tri_length(mesh);
        var triMemBuf = Marshal.AllocHGlobal((int)triLen * sizeof(ulong));
        try
        {
            var triMem = manifold_meshgl64_tri_verts(triMemBuf, mesh);
            triangleIndices = new ulong[triLen];
            Marshal.Copy(triMem, (long[])((object)triangleIndices), 0, (int)triLen);
        }
        finally
        {
            Marshal.FreeHGlobal(triMemBuf);
        }
    }

    /// Check if a Manifold is valid and get its status.
    public static ManifoldError GetStatus(ManifoldManifold m) => manifold_status(m);

    /// Check if a Manifold is empty (no triangles).
    public static bool IsEmpty(ManifoldManifold m) => manifold_is_empty(m) != 0;

    /// Get vertex count.
    public static nuint GetVertexCount(ManifoldManifold m) => manifold_num_vert(m);

    /// Get triangle count.
    public static nuint GetTriangleCount(ManifoldManifold m) => manifold_num_tri(m);

    /// Get volume (useful for verifying boolean results).
    public static double GetVolume(ManifoldManifold m) => manifold_volume(m);

    /// Safely delete a Manifold: run its destructor in place, then free the
    /// `mem` buffer we allocated for it in CreateCube/MeshToManifold/Union/etc.
    /// manifold_delete_manifold is NOT used here - it calls C++ `delete` on
    /// the pointer, which assumes a heap allocation from manifold_alloc_manifold,
    /// not memory we placement-constructed into via Marshal.AllocHGlobal.
    public static void DeleteManifold(ManifoldManifold m)
    {
        manifold_destruct_manifold(m);
        Marshal.FreeHGlobal(m.Handle);
    }

    /// Safely delete a MeshGL64 (see DeleteManifold for why manifold_delete_meshgl64 isn't used).
    public static void DeleteMeshGL64(ManifoldMeshGL64 m)
    {
        manifold_destruct_meshgl64(m);
        Marshal.FreeHGlobal(m.Handle);
    }
}
