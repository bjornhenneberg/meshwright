using System;
using System.Collections.Generic;
using g3;

namespace Meshwright.Tests.Edit;

/// <summary>
/// A Menger sponge, generated rather than loaded: 2,112 triangles at level 2, closed, one shell,
/// riddled with square tunnels straight through it. The generator emits only the faces of solid
/// cells that have no solid neighbour, welded on a shared vertex grid.
///
/// <para>
/// This reproduces <c>Menger_sponge_sample.stl</c> exactly — same triangle count, vertex count,
/// bounding box, shell count and volume to the precision the STL's float32 coordinates can carry —
/// without committing a third-party binary, which §11 (2026-09-04) rules out for test meshes.
/// </para>
///
/// <para>
/// It is shared because it is the fixture that keeps breaking optimistic assumptions: a cut through
/// it produces dozens of separate cross-section loops, and a ray through its centre misses the
/// surface entirely. <c>PlaneCutCrossSectionTests.MengerSpongeFixture_IsTheExpectedClosedSolid</c>
/// pins its properties.
/// </para>
/// </summary>
internal static class MengerSponge
{
    /// <summary>The level-2 sponge spanning -1..1, the one the sample file matches.</summary>
    internal static DMesh3 BuildLevel2() => Build(2, 1.0);

    internal static DMesh3 Build(int level, double half)
    {
        int n = (int)Math.Round(Math.Pow(3, level));
        double step = 2 * half / n;
        var mesh = new DMesh3();
        var vertexIds = new Dictionary<(int, int, int), int>();

        int V(int x, int y, int z)
        {
            if (!vertexIds.TryGetValue((x, y, z), out int id))
            {
                id = mesh.AppendVertex(new Vector3d(-half + (x * step), -half + (y * step), -half + (z * step)));
                vertexIds[(x, y, z)] = id;
            }

            return id;
        }

        bool Solid(int i, int j, int k)
        {
            if (i < 0 || j < 0 || k < 0 || i >= n || j >= n || k >= n)
            {
                return false;
            }

            for (int d = 0; d < level; d++)
            {
                int ones = (i % 3 == 1 ? 1 : 0) + (j % 3 == 1 ? 1 : 0) + (k % 3 == 1 ? 1 : 0);
                if (ones >= 2)
                {
                    return false;
                }

                i /= 3;
                j /= 3;
                k /= 3;
            }

            return true;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                for (int k = 0; k < n; k++)
                {
                    if (!Solid(i, j, k))
                    {
                        continue;
                    }

                    if (!Solid(i - 1, j, k))
                    {
                        Quad(mesh, V(i, j, k), V(i, j, k + 1), V(i, j + 1, k + 1), V(i, j + 1, k));
                    }

                    if (!Solid(i + 1, j, k))
                    {
                        Quad(mesh, V(i + 1, j, k), V(i + 1, j + 1, k), V(i + 1, j + 1, k + 1), V(i + 1, j, k + 1));
                    }

                    if (!Solid(i, j - 1, k))
                    {
                        Quad(mesh, V(i, j, k), V(i + 1, j, k), V(i + 1, j, k + 1), V(i, j, k + 1));
                    }

                    if (!Solid(i, j + 1, k))
                    {
                        Quad(mesh, V(i, j + 1, k), V(i, j + 1, k + 1), V(i + 1, j + 1, k + 1), V(i + 1, j + 1, k));
                    }

                    if (!Solid(i, j, k - 1))
                    {
                        Quad(mesh, V(i, j, k), V(i, j + 1, k), V(i + 1, j + 1, k), V(i + 1, j, k));
                    }

                    if (!Solid(i, j, k + 1))
                    {
                        Quad(mesh, V(i, j, k + 1), V(i + 1, j, k + 1), V(i + 1, j + 1, k + 1), V(i, j + 1, k + 1));
                    }
                }
            }
        }

        return mesh;
    }

    private static void Quad(DMesh3 mesh, int a, int b, int c, int d)
    {
        mesh.AppendTriangle(a, b, c);
        mesh.AppendTriangle(a, c, d);
    }
}
