using g3;
using Meshwright.Geometry.Diagnostics;
using Meshwright.Geometry.Repair;
using Xunit;

namespace Meshwright.Tests.Repair;

/// <summary>
/// Invariant-based tests for backlog item 15: <see cref="HoleFillMode.Smooth"/> must actually
/// continue the surrounding surface's curvature across a hole, not merely reproduce
/// <see cref="HoleFillMode.Planar"/>'s flat ear-clip fill with a cosmetic extra vertex.
///
/// Per SPECIFICATION.md §11 (2026-09-05), these assert geometric invariants -- bounding box,
/// volume, shell count, issue count, before and after -- rather than "the operation ran" facts
/// that would pass equally well against a Smooth that silently collapses into Planar.
/// </summary>
public class HoleFillSmoothTests
{
    /// <summary>
    /// A UV sphere of the given radius with the cap above <paramref name="capRingIndex"/> removed:
    /// rings <c>capRingIndex..latSteps-1</c> are built as a normal latitude/longitude grid, closed
    /// at the south pole with a triangle fan, but the north pole and the ring above
    /// <paramref name="capRingIndex"/> are simply never added -- so the mesh has exactly one open
    /// boundary loop, sitting exactly on the sphere at that latitude (a circle, hence itself
    /// planar), with well-defined curvature in every other direction. This is built by hand
    /// (rather than via marching cubes) so the boundary loop's vertices sit at an *exact* radius,
    /// letting deviation-from-sphere be measured precisely.
    /// </summary>
    private static DMesh3 BuildSphereWithCapRemoved(double radius, int latSteps, int lonSteps, int capRingIndex)
    {
        var mesh = new DMesh3();
        var rings = new int[latSteps][];

        for (int i = capRingIndex; i < latSteps; i++)
        {
            double theta = i * Math.PI / latSteps;
            rings[i] = new int[lonSteps];
            for (int j = 0; j < lonSteps; j++)
            {
                double phi = j * 2.0 * Math.PI / lonSteps;
                var p = new Vector3d(
                    radius * Math.Sin(theta) * Math.Cos(phi),
                    radius * Math.Sin(theta) * Math.Sin(phi),
                    radius * Math.Cos(theta));
                rings[i][j] = mesh.AppendVertex(p);
            }
        }

        int southPole = mesh.AppendVertex(new Vector3d(0, 0, -radius));

        for (int i = capRingIndex; i < latSteps - 1; i++)
        {
            for (int j = 0; j < lonSteps; j++)
            {
                int jNext = (j + 1) % lonSteps;
                int a = rings[i][j];
                int b = rings[i][jNext];
                int c = rings[i + 1][jNext];
                int d = rings[i + 1][j];
                mesh.AppendTriangle(a, c, b);
                mesh.AppendTriangle(a, d, c);
            }
        }

        for (int j = 0; j < lonSteps; j++)
        {
            int jNext = (j + 1) % lonSteps;
            int a = rings[latSteps - 1][j];
            int b = rings[latSteps - 1][jNext];
            mesh.AppendTriangle(a, southPole, b);
        }

        return mesh;
    }

    /// <summary>
    /// A flat grid (z = 0 everywhere) with its i and j indices wrapped -- same trick as
    /// <c>HoleFillRepairTests.BuildGridWithNonConvexInteriorHole</c> -- so the mesh has no outer
    /// boundary, plus a rectangular block of squares omitted from its interior. That leaves a
    /// single boundary loop entirely inside the flat face: a plate with a punched hole, not an
    /// edge notch, with real (flat) surface on every side of it to source curvature from.
    /// </summary>
    private static DMesh3 BuildFlatPlateWithHole(int n, int holeStart, int holeSize)
    {
        var mesh = new DMesh3();
        var verts = new int[n, n];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                verts[i, j] = mesh.AppendVertex(new Vector3d(i, j, 0));
            }
        }

        bool InHole(int i, int j) => i >= holeStart && i < holeStart + holeSize && j >= holeStart && j < holeStart + holeSize;

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (InHole(i, j))
                {
                    continue;
                }

                int a = verts[i, j];
                int b = verts[(i + 1) % n, j];
                int c = verts[(i + 1) % n, (j + 1) % n];
                int d = verts[i, (j + 1) % n];
                mesh.AppendTriangle(a, b, c);
                mesh.AppendTriangle(a, c, d);
            }
        }

        return mesh;
    }

    private static double MaxInteriorDeviationFromSphere(DMesh3 mesh, int vertsBefore, double radius)
    {
        double maxDeviation = 0;
        for (int vid = vertsBefore; vid < mesh.VertexCount; vid++)
        {
            if (!mesh.IsVertex(vid))
            {
                continue;
            }
            double deviation = Math.Abs(mesh.GetVertex(vid).Length - radius);
            maxDeviation = Math.Max(maxDeviation, deviation);
        }
        return maxDeviation;
    }

    private static double MaxInteriorDeviationFromPlane(DMesh3 mesh, int vertsBefore)
    {
        double maxDeviation = 0;
        for (int vid = vertsBefore; vid < mesh.VertexCount; vid++)
        {
            if (!mesh.IsVertex(vid))
            {
                continue;
            }
            maxDeviation = Math.Max(maxDeviation, Math.Abs(mesh.GetVertex(vid).z));
        }
        return maxDeviation;
    }

    // --- Invariant 1: Smooth follows curvature on a sphere cap, clearly beating Planar --------

    [Fact]
    public void Smooth_SphereCap_InteriorVerticesLieCloserToSphereThanPlanarDoes()
    {
        const double radius = 10.0;
        const int latSteps = 16;
        const int capRingIndex = 2;
        DMesh3 smoothMesh = BuildSphereWithCapRemoved(radius, latSteps: latSteps, lonSteps: 32, capRingIndex: capRingIndex);
        int vertsBefore = smoothMesh.VertexCount;
        Assert.Equal(1, new MeshBoundaryLoops(smoothMesh).Count);

        HoleFillResult smoothResult = HoleFillRepair.Fill(smoothMesh, HoleFillMode.Smooth);

        Assert.Equal(1, smoothResult.HolesFilled);

        // Planar adds no new vertices at all (ear-clip only) -- its fill is the flat disk spanned
        // by the boundary ring, so its worst-case deviation from the sphere is analytic: the
        // distance from the sphere surface to that disk's own center point (0, 0, z1). Any vertex
        // appended by Smooth is a genuine interior degree of freedom the refinement step
        // introduced, letting it do better.
        Assert.True(smoothMesh.VertexCount > vertsBefore, "Smooth must add interior vertices to have room to follow curvature.");

        double theta1 = capRingIndex * Math.PI / latSteps;
        double z1 = radius * Math.Cos(theta1);
        double planarDeviation = radius - z1;

        double smoothDeviation = MaxInteriorDeviationFromSphere(smoothMesh, vertsBefore, radius);

        // Sanity check on the test setup itself, not the code under test.
        Assert.True(planarDeviation > radius * 0.05, $"Test setup sanity check: planar deviation was only {planarDeviation}.");

        // Smooth must beat planar by a clear margin (at least 2x), not just nudge it slightly --
        // this is what fails if Smooth silently becomes Planar again.
        Assert.True(
            smoothDeviation * 2.0 < planarDeviation,
            $"Smooth fill (max deviation {smoothDeviation:F4}) did not clearly beat Planar (analytic max deviation {planarDeviation:F4}) on a sphere cap.");
    }

    [Fact]
    public void Smooth_SphereCap_VolumeIsCloserToTrueSphereThanPlanarIs()
    {
        const double radius = 10.0;
        double trueSphereVolume = (4.0 / 3.0) * Math.PI * radius * radius * radius;

        DMesh3 planarMesh = BuildSphereWithCapRemoved(radius, latSteps: 16, lonSteps: 32, capRingIndex: 2);
        DMesh3 smoothMesh = BuildSphereWithCapRemoved(radius, latSteps: 16, lonSteps: 32, capRingIndex: 2);

        HoleFillRepair.Fill(planarMesh, HoleFillMode.Planar);
        HoleFillRepair.Fill(smoothMesh, HoleFillMode.Smooth);

        double planarVolume = MeshStatistics.Compute(planarMesh).Volume;
        double smoothVolume = MeshStatistics.Compute(smoothMesh).Volume;

        double planarError = Math.Abs(planarVolume - trueSphereVolume);
        double smoothError = Math.Abs(smoothVolume - trueSphereVolume);

        Assert.True(
            smoothError < planarError,
            $"Smooth fill volume error ({smoothError:F2}) did not beat Planar's ({planarError:F2}); true volume {trueSphereVolume:F2}, planar {planarVolume:F2}, smooth {smoothVolume:F2}.");
    }

    // --- Invariant 2: Smooth must not invent curvature on an already-flat patch ----------------

    [Fact]
    public void Smooth_FlatPlateHole_InteriorVerticesStayInThePlane()
    {
        DMesh3 mesh = BuildFlatPlateWithHole(n: 8, holeStart: 3, holeSize: 2);
        int vertsBefore = mesh.VertexCount;
        Assert.Equal(1, new MeshBoundaryLoops(mesh).Count);

        HoleFillResult result = HoleFillRepair.Fill(mesh, HoleFillMode.Smooth);

        Assert.Equal(1, result.HolesFilled);
        double maxDeviation = MaxInteriorDeviationFromPlane(mesh, vertsBefore);

        Assert.True(maxDeviation < 1e-9, $"Smooth fill bulged {maxDeviation:E} out of a flat plate's plane.");
    }

    // --- Invariant 3: the filled mesh stays valid geometry -------------------------------------

    [Fact]
    public void Smooth_SphereCap_ProducesClosedManifoldMeshWithoutSelfIntersections()
    {
        const double radius = 10.0;
        DMesh3 mesh = BuildSphereWithCapRemoved(radius, latSteps: 16, lonSteps: 32, capRingIndex: 2);
        int shellCountBefore = MeshStatistics.Compute(mesh).ShellCount;

        HoleFillResult result = HoleFillRepair.Fill(mesh, HoleFillMode.Smooth);

        Assert.Equal(1, result.HolesFilled);
        Assert.Equal(0, new MeshBoundaryLoops(mesh).Count);
        Assert.Empty(new NonManifoldDetector().Detect(mesh));
        Assert.Empty(new SelfIntersectionDetector().Detect(mesh));

        MeshStatistics after = MeshStatistics.Compute(mesh);
        Assert.Equal(shellCountBefore, after.ShellCount);
    }

    // --- Invariant 4: the three modes are actually different ------------------------------------

    [Fact]
    public void FlatPlanarAndSmooth_ProduceMeasurablyDifferentResultsOnASphereCap()
    {
        const double radius = 10.0;
        DMesh3 flatMesh = BuildSphereWithCapRemoved(radius, latSteps: 16, lonSteps: 32, capRingIndex: 2);
        DMesh3 planarMesh = BuildSphereWithCapRemoved(radius, latSteps: 16, lonSteps: 32, capRingIndex: 2);
        DMesh3 smoothMesh = BuildSphereWithCapRemoved(radius, latSteps: 16, lonSteps: 32, capRingIndex: 2);

        HoleFillResult flatResult = HoleFillRepair.Fill(flatMesh, HoleFillMode.Flat);
        HoleFillResult planarResult = HoleFillRepair.Fill(planarMesh, HoleFillMode.Planar);
        HoleFillResult smoothResult = HoleFillRepair.Fill(smoothMesh, HoleFillMode.Smooth);

        double flatVolume = MeshStatistics.Compute(flatMesh).Volume;
        double planarVolume = MeshStatistics.Compute(planarMesh).Volume;
        double smoothVolume = MeshStatistics.Compute(smoothMesh).Volume;

        // Flat fans from the centroid (one interior vertex) while Planar ear-clips flush with the
        // loop (no interior vertices) -- a topological difference in triangle/vertex count even
        // though, for a loop this symmetric, the centroid happens to land exactly in the loop's
        // own plane, so both give the same flat disk and hence the same volume.
        Assert.NotEqual(flatResult.TrianglesAdded, planarResult.TrianglesAdded);
        Assert.NotEqual(planarMesh.VertexCount, smoothMesh.VertexCount);

        // Smooth is the one that must NOT collapse into either flat fill -- it has real interior
        // degrees of freedom and must use them to bulge toward the sphere.
        const double meaningfulVolumeGap = 1.0; // sphere volume here is ~4189; this is not noise.
        Assert.True(Math.Abs(smoothVolume - planarVolume) > meaningfulVolumeGap, $"Smooth ({smoothVolume:F2}) and Planar ({planarVolume:F2}) produced essentially the same volume.");
        Assert.True(Math.Abs(smoothVolume - flatVolume) > meaningfulVolumeGap, $"Smooth ({smoothVolume:F2}) and Flat ({flatVolume:F2}) produced essentially the same volume.");
    }
}
