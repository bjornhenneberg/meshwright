using System.Numerics;
using g3;
using Meshwright.App.Gizmos;
using Meshwright.Rendering.Gizmos;
using Xunit;

namespace Meshwright.Tests.Gizmos;

/// <summary>
/// Tests for <see cref="HollowGizmo"/> (M4-8: gizmo coverage for Hollow). Per
/// SPECIFICATION.md §11 (2026-09-04), interaction is driven through a real
/// <see cref="Meshwright.Rendering.Camera.OrbitCamera"/> and the production unprojection via
/// <see cref="ViewportHarness"/>, never a hand-built ray — every gizmo interaction defect found so
/// far escaped a green suite because a synthetic ray silently fixed camera distance and display
/// scaling, the two variables the bugs actually depended on.
/// </summary>
public class HollowGizmoTests
{
    /// <summary>A cube with correct outward-facing winding on every face (unlike the plain
    /// unit-cube fixtures used elsewhere for picking tests, which don't care about orientation).
    /// <see cref="HollowGizmo.ComputeSurfaceAnchor"/> needs real outward normals to test against.</summary>
    private static DMesh3 CreateOutwardWoundCube(double size = 1.0)
    {
        var mesh = new DMesh3();
        int v000 = mesh.AppendVertex(new Vector3d(0, 0, 0) * size);
        int v100 = mesh.AppendVertex(new Vector3d(1, 0, 0) * size);
        int v110 = mesh.AppendVertex(new Vector3d(1, 1, 0) * size);
        int v010 = mesh.AppendVertex(new Vector3d(0, 1, 0) * size);
        int v001 = mesh.AppendVertex(new Vector3d(0, 0, 1) * size);
        int v101 = mesh.AppendVertex(new Vector3d(1, 0, 1) * size);
        int v111 = mesh.AppendVertex(new Vector3d(1, 1, 1) * size);
        int v011 = mesh.AppendVertex(new Vector3d(0, 1, 1) * size);

        void Quad(int p, int q, int r, int s)
        {
            mesh.AppendTriangle(p, q, r);
            mesh.AppendTriangle(p, r, s);
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
    public void NewGizmo_WasNotTouched()
    {
        var gizmo = new HollowGizmo(Vector3.Zero, Vector3.UnitY);

        Assert.False(gizmo.WasTouched);
    }

    [Fact]
    public void ComputeSurfaceAnchor_OnCube_FindsTopFaceWithUpwardNormalAlongZ()
    {
        // Z is up in this app (SPECIFICATION.md §11, 2026-09-05): STL/3MF and the print bed put
        // the build direction along +Z, not the realtime-graphics Y-up convention.
        DMesh3 cube = CreateOutwardWoundCube();

        (Vector3 point, Vector3 normal) = HollowGizmo.ComputeSurfaceAnchor(cube);

        // The top face (z=1) has outward normal +z; a ray cast straight down (-Z) from above the
        // cube should land there first, with that upward-facing normal. If ComputeSurfaceAnchor
        // regresses to casting along -Y instead, it reports the y=1 face's normal (+Y, not +Z),
        // which fails the second assertion below.
        Assert.Equal(1f, point.Z, 3);
        Assert.True(normal.Z > 0.9f, $"Expected an upward (+Z) normal, got {normal}.");
    }

    [Fact]
    public void ComputeSurfaceAnchor_OnEmptyMesh_FallsBackWithoutThrowing()
    {
        (Vector3 point, Vector3 normal) = HollowGizmo.ComputeSurfaceAnchor(new DMesh3());

        Assert.Equal(Vector3.Zero, point);
        Assert.Equal(Vector3.UnitZ, normal);
    }

    /// <summary>
    /// Regression: a Menger-sponge-shaped model has a hole straight through the middle of every
    /// face, so a single ray through the bounding-box centre misses and must not be allowed to
    /// silently fall back to a synthetic point floating in space — that point rendered visibly
    /// detached from the model when this was checked in the real GUI (Menger_sponge_sample.stl).
    /// This fixture reproduces the minimal version: a square "picture frame" standing in for the
    /// top face, with a hole exactly centred where the naive centre ray would land. The anchor
    /// found must sit on the mesh surface (distance to the surface ~= 0 via the same
    /// <see cref="DMeshAABBTree3"/> the raycaster itself uses), not merely somewhere near it.
    /// </summary>
    [Fact]
    public void ComputeSurfaceAnchor_OnMeshWithHoleThroughTopFaceCentre_LandsOnTheSurfaceNotMidAir()
    {
        DMesh3 frame = CreateTopFaceWithCentreHole(outerHalfExtent: 5.0, innerHalfExtent: 1.0, z: 10.0);

        (Vector3 point, Vector3 normal) = HollowGizmo.ComputeSurfaceAnchor(frame);

        var tree = new DMeshAABBTree3(frame, autoBuild: true);
        var queryPoint = new Vector3d(point.X, point.Y, point.Z);
        double distanceToSurface = MeshQueries.NearestPointDistance(frame, tree, queryPoint);

        Assert.True(distanceToSurface < 1e-3, $"Anchor {point} is {distanceToSurface} units from the nearest surface point — expected it to land on the surface.");
        Assert.Equal(10.0, point.Z, 3);
        Assert.True(normal.Z > 0.9f, $"Expected an upward (+Z) normal for the frame's top, got {normal}.");
    }

    /// <summary>
    /// A flat square "picture frame" at height <paramref name="z"/>: a solid band from
    /// ±<paramref name="outerHalfExtent"/> down to a square hole of
    /// ±<paramref name="innerHalfExtent"/> at the centre. Bounding-box centre sits exactly over
    /// the hole, so a ray straight down the centre misses; the frame material is only reachable by
    /// sampling away from the centre, exactly like the top-down-cutout case
    /// <see cref="HollowGizmo.ComputeSurfaceAnchor"/> must handle.
    /// </summary>
    private static DMesh3 CreateTopFaceWithCentreHole(double outerHalfExtent, double innerHalfExtent, double z)
    {
        var mesh = new DMesh3();

        // Outer corners (counter-clockwise viewed from +Z, so triangles wind with outward/+Z
        // normals per g3's convention).
        int a = mesh.AppendVertex(new Vector3d(-outerHalfExtent, -outerHalfExtent, z));
        int b = mesh.AppendVertex(new Vector3d(outerHalfExtent, -outerHalfExtent, z));
        int c = mesh.AppendVertex(new Vector3d(outerHalfExtent, outerHalfExtent, z));
        int d = mesh.AppendVertex(new Vector3d(-outerHalfExtent, outerHalfExtent, z));

        // Inner (hole) corners, same winding order.
        int ia = mesh.AppendVertex(new Vector3d(-innerHalfExtent, -innerHalfExtent, z));
        int ib = mesh.AppendVertex(new Vector3d(innerHalfExtent, -innerHalfExtent, z));
        int ic = mesh.AppendVertex(new Vector3d(innerHalfExtent, innerHalfExtent, z));
        int id = mesh.AppendVertex(new Vector3d(-innerHalfExtent, innerHalfExtent, z));

        void Band(int outer0, int outer1, int inner1, int inner0)
        {
            mesh.AppendTriangle(outer0, outer1, inner1);
            mesh.AppendTriangle(outer0, inner1, inner0);
        }

        Band(a, b, ib, ia);
        Band(b, c, ic, ib);
        Band(c, d, id, ic);
        Band(d, a, ia, id);

        return mesh;
    }

    /// <summary>
    /// Model radii spanning the range Meshwright targets — a small part through a large print —
    /// exercised at two camera distances as the task requires, using the real
    /// <see cref="ViewportHarness"/>/<see cref="Meshwright.Rendering.Camera.OrbitCamera"/> pipeline
    /// rather than a hand-built ray.
    /// </summary>
    public static TheoryData<float> Scales()
    {
        var data = new TheoryData<float>();
        foreach (float radius in new[] { 5f, 50f })
        {
            data.Add(radius);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void ClickingTheInnerHandle_ClaimsTheDrag(float radius)
    {
        var anchor = new Vector3(2f, -1f, 3f);
        var normal = Vector3.UnitY;
        var harness = ViewportHarness.Framed(anchor, radius);
        var gizmo = new HollowGizmo(anchor, normal, initialWallThickness: radius * 0.1f);

        Assert.True(harness.PressAtWorld(gizmo, gizmo.InnerPoint),
            $"Clicking the inner handle must claim the drag (radius={radius}).");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void ClickingFarFromTheHandle_LeavesTheClickForTheCamera(float radius)
    {
        var anchor = new Vector3(2f, -1f, 3f);
        var harness = ViewportHarness.Framed(anchor, radius);
        var gizmo = new HollowGizmo(anchor, Vector3.UnitY, initialWallThickness: radius * 0.1f);

        Assert.False(harness.PressAtPixel(gizmo, new Vector2(4f, 4f)),
            $"A click in the viewport corner must fall through to camera orbit (radius={radius}).");
    }

    [Theory]
    [MemberData(nameof(Scales))]
    public void HandleIsBigEnoughOnScreenToHit(float radius)
    {
        var anchor = new Vector3(2f, -1f, 3f);
        var harness = ViewportHarness.Framed(anchor, radius);

        int grab = harness.GrabRadiusPixels(
            () => new HollowGizmo(anchor, Vector3.UnitY, initialWallThickness: radius * 0.1f),
            anchor - Vector3.UnitY * (radius * 0.1f),
            Vector2.UnitX);

        Assert.True(grab >= 6, $"Hollow handle grab radius is {grab}px (need >= 6px) at radius={radius}.");
    }

    /// <summary>
    /// Dragging the handle further from the anchor (deeper along the inward normal) must increase
    /// wall thickness by exactly the amount the drag implies, not merely change it. Verified at two
    /// camera distances via the production unprojection: the drag target is a specific world point
    /// on the gizmo's own axis, projected to the pixel it appears at, and the resulting thickness is
    /// checked against that point's exact offset from the anchor.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scales))]
    public void DraggingHandleDeeper_IncreasesWallThicknessByTheDragAmount(float radius)
    {
        var anchor = new Vector3(2f, -1f, 3f);
        var normal = Vector3.UnitY;
        float startThickness = radius * 0.05f;
        float targetThickness = radius * 0.2f;

        var harness = ViewportHarness.Framed(anchor, radius);
        var gizmo = new HollowGizmo(anchor, normal, initialWallThickness: startThickness);

        Assert.True(harness.PressAtWorld(gizmo, gizmo.InnerPoint));

        Vector3 targetPoint = anchor - normal * targetThickness;
        Vector2 targetPixel = harness.RequireProjectToPixel(targetPoint);
        Assert.True(harness.MoveToPixel(gizmo, targetPixel));

        Assert.True(gizmo.WasTouched);
        AssertWithinDragTolerance(targetThickness, gizmo.WallThickness, radius);
    }

    /// <summary>The inverse drag: pulling the handle back toward the anchor must decrease wall
    /// thickness by the amount implied, not just change it in some direction.</summary>
    [Theory]
    [MemberData(nameof(Scales))]
    public void DraggingHandleShallower_DecreasesWallThicknessByTheDragAmount(float radius)
    {
        var anchor = new Vector3(2f, -1f, 3f);
        var normal = Vector3.UnitY;
        float startThickness = radius * 0.25f;
        float targetThickness = radius * 0.08f;

        var harness = ViewportHarness.Framed(anchor, radius);
        var gizmo = new HollowGizmo(anchor, normal, initialWallThickness: startThickness);

        Assert.True(harness.PressAtWorld(gizmo, gizmo.InnerPoint));

        Vector3 targetPoint = anchor - normal * targetThickness;
        Vector2 targetPixel = harness.RequireProjectToPixel(targetPoint);
        Assert.True(harness.MoveToPixel(gizmo, targetPixel));

        Assert.True(gizmo.WasTouched);
        Assert.True(gizmo.WallThickness < startThickness);
        AssertWithinDragTolerance(targetThickness, gizmo.WallThickness, radius);
    }

    /// <summary>
    /// Asserts the dragged thickness matches the world-space target the drag aimed at, within the
    /// float32 round-trip error the pixel-project/unproject pipeline itself accumulates (observed
    /// up to ~0.3% of the framed radius at larger camera distances) — tight enough to catch a
    /// wrong-direction or wrong-magnitude drag, loose enough not to flake on floating-point noise
    /// that has nothing to do with gizmo correctness.
    /// </summary>
    private static void AssertWithinDragTolerance(float expected, float actual, float radius)
    {
        float tolerance = MathF.Max(0.01f, radius * 0.01f);
        Assert.True(MathF.Abs(expected - actual) <= tolerance,
            $"Expected wall thickness {expected} (±{tolerance}) but got {actual} at radius={radius}.");
    }

    [Fact]
    public void DraggingHandle_RaisesChangedEvent()
    {
        var anchor = new Vector3(0f, 0f, 0f);
        var normal = Vector3.UnitY;
        var harness = ViewportHarness.Framed(anchor, 10f);
        var gizmo = new HollowGizmo(anchor, normal, initialWallThickness: 1f);

        int raised = 0;
        gizmo.Changed += (_, _) => raised++;

        Assert.True(harness.PressAtWorld(gizmo, gizmo.InnerPoint));
        harness.MoveToPixel(gizmo, harness.RequireProjectToPixel(anchor - normal * 2f));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void ReleasingHandle_EndsTheDrag()
    {
        var anchor = new Vector3(0f, 0f, 0f);
        var normal = Vector3.UnitY;
        var harness = ViewportHarness.Framed(anchor, 10f);
        var gizmo = new HollowGizmo(anchor, normal, initialWallThickness: 1f);

        Assert.True(harness.PressAtWorld(gizmo, gizmo.InnerPoint));
        Assert.True(harness.ReleaseAtPixel(gizmo, harness.RequireProjectToPixel(gizmo.InnerPoint)));

        // After release, further pointer moves should not affect the thickness.
        float thicknessAfterRelease = gizmo.WallThickness;
        harness.MoveToPixel(gizmo, harness.RequireProjectToPixel(anchor - normal * 5f));
        Assert.Equal(thicknessAfterRelease, gizmo.WallThickness);
    }

    [Fact]
    public void WallThickness_NeverDragsBelowMinimum()
    {
        var anchor = new Vector3(0f, 0f, 0f);
        var normal = Vector3.UnitY;
        var harness = ViewportHarness.Framed(anchor, 10f);
        var gizmo = new HollowGizmo(anchor, normal, initialWallThickness: 1f);

        Assert.True(harness.PressAtWorld(gizmo, gizmo.InnerPoint));
        // Drag past the anchor, to the outward side — an attempt to go negative.
        harness.MoveToPixel(gizmo, harness.RequireProjectToPixel(anchor + normal * 3f));

        Assert.True(gizmo.WallThickness > 0f);
    }
}
