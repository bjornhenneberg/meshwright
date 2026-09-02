using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class MeshStatisticsTests
{
    private static DMesh3 BuildUnitCube()
    {
        // Unit cube [0,1]^3, triangulated with outward-facing winding.
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v100 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v110 = mesh.AppendVertex(new Vector3d(1, 1, 0));
        int v010 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, 1));
        int v101 = mesh.AppendVertex(new Vector3d(1, 0, 1));
        int v111 = mesh.AppendVertex(new Vector3d(1, 1, 1));
        int v011 = mesh.AppendVertex(new Vector3d(0, 1, 1));

        void Quad(int a, int b, int c, int d)
        {
            mesh.AppendTriangle(a, b, c);
            mesh.AppendTriangle(a, c, d);
        }

        Quad(v000, v010, v110, v100); // bottom (z=0), outward normal -z
        Quad(v001, v101, v111, v011); // top (z=1), outward normal +z
        Quad(v000, v100, v101, v001); // front (y=0), outward normal -y
        Quad(v010, v011, v111, v110); // back (y=1), outward normal +y
        Quad(v000, v001, v011, v010); // left (x=0), outward normal -x
        Quad(v100, v110, v111, v101); // right (x=1), outward normal +x

        return mesh;
    }

    [Fact]
    public void Compute_UnitCube_ReportsExpectedVolumeAndArea()
    {
        DMesh3 mesh = BuildUnitCube();

        MeshStatistics stats = MeshStatistics.Compute(mesh);

        Assert.Equal(12, stats.TriangleCount);
        Assert.Equal(8, stats.VertexCount);
        Assert.Equal(1.0, stats.Volume, 6);
        Assert.Equal(6.0, stats.SurfaceArea, 6);
    }

    [Fact]
    public void Compute_UnitCube_ReportsExpectedBoundingBox()
    {
        DMesh3 mesh = BuildUnitCube();

        MeshStatistics stats = MeshStatistics.Compute(mesh);

        Assert.Equal(new Vector3d(0, 0, 0), stats.BoundingBox.Min);
        Assert.Equal(new Vector3d(1, 1, 1), stats.BoundingBox.Max);
    }

    [Fact]
    public void Compute_SingleCube_ReportsOneShell()
    {
        DMesh3 mesh = BuildUnitCube();

        MeshStatistics stats = MeshStatistics.Compute(mesh);

        Assert.Equal(1, stats.ShellCount);
    }

    [Fact]
    public void Compute_TwoDisjointCubes_ReportsTwoShells()
    {
        DMesh3 mesh = BuildUnitCube();
        DMesh3 second = BuildUnitCube();

        var remap = new Dictionary<int, int>();
        foreach (int vid in second.VertexIndices())
        {
            remap[vid] = mesh.AppendVertex(second.GetVertex(vid) + new Vector3d(10, 0, 0));
        }

        foreach (int tid in second.TriangleIndices())
        {
            Index3i tri = second.GetTriangle(tid);
            mesh.AppendTriangle(remap[tri.a], remap[tri.b], remap[tri.c]);
        }

        MeshStatistics stats = MeshStatistics.Compute(mesh);

        Assert.Equal(2, stats.ShellCount);
    }
}
