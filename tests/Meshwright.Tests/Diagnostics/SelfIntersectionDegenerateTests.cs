using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Spatial;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

/// <summary>
/// Regression tests for degenerate triangles in the self-intersection search. A real corpus file
/// (Thingi10K 84929) crashed the whole import with
/// <c>IntrLine2Triangle2.GetInterval: too many intersections!</c> — the vendored exact predicate
/// cannot handle a fully-collinear triangle, because all three of its vertices land on the
/// intersection line at once.
/// </summary>
public class SelfIntersectionDegenerateTests
{
    /// <summary>A zero-area (fully collinear) triangle, plus a normal triangle crossing its plane.</summary>
    private static DMesh3 CollinearTrianglePlusCrossingTriangle()
    {
        var mesh = new DMesh3();

        // Collinear: all three vertices on the x axis, so the triangle has no area at all.
        int a0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int a1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int a2 = mesh.AppendVertex(new Vector3d(2, 0, 0));
        mesh.AppendTriangle(a0, a1, a2);

        // A real triangle straddling that line, sharing no vertex with it.
        int b0 = mesh.AppendVertex(new Vector3d(1, -1, -1));
        int b1 = mesh.AppendVertex(new Vector3d(1, 1, -1));
        int b2 = mesh.AppendVertex(new Vector3d(1, 0, 1));
        mesh.AppendTriangle(b0, b1, b2);

        return mesh;
    }

    [Fact]
    public void FindPairs_DoesNotThrowOnACollinearTriangle()
    {
        // Enumerate fully: the search is lazy, so an exception would surface only on iteration.
        List<(int A, int B)> pairs = SelfIntersectionSearch.FindPairs(CollinearTrianglePlusCrossingTriangle()).ToList();

        // The degenerate triangle is excluded rather than intersected, so no pair is reported.
        Assert.Empty(pairs);
    }

    [Fact]
    public void Detector_DoesNotThrowOnACollinearTriangle()
    {
        var detector = new SelfIntersectionDetector();

        Assert.Empty(detector.Detect(CollinearTrianglePlusCrossingTriangle()));
    }

    [Fact]
    public void DegenerateTrianglesAreStillReportedByTheirOwnDetector()
    {
        // Skipping them in the intersection search must not make them invisible: the
        // degenerate-triangle detector is what reports them, and still does.
        var issues = new DegenerateTriangleDetector().Detect(CollinearTrianglePlusCrossingTriangle());

        Assert.Single(issues);
    }

    [Fact]
    public void GenuineIntersectionsAreStillFound()
    {
        // The guard must exclude only unusable triangles, not thin-but-real ones.
        var mesh = new DMesh3();
        int a0 = mesh.AppendVertex(new Vector3d(-1, 0, 0));
        int a1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int a2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a0, a1, a2);

        int b0 = mesh.AppendVertex(new Vector3d(0, 0.5, -1));
        int b1 = mesh.AppendVertex(new Vector3d(0, 0.5, 1));
        int b2 = mesh.AppendVertex(new Vector3d(0.5, -0.5, 0));
        mesh.AppendTriangle(b0, b1, b2);

        Assert.Single(SelfIntersectionSearch.FindPairs(mesh).ToList());
    }
}
