using g3;
using Meshwright.Geometry.Diagnostics;
using Xunit;

namespace Meshwright.Tests.Diagnostics;

public class InvertedNormalDetectorTests
{
    // Tetrahedron with vertices at the origin and the three unit axes.
    // Faces below are wound so every triangle's normal, via (b-a) x (c-a),
    // points away from the opposite vertex (i.e. consistently outward).
    private static DMesh3 BuildConsistentTetrahedron()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(v0, v2, v1); // opposite v3, normal -z
        mesh.AppendTriangle(v0, v1, v3); // opposite v2, normal -y
        mesh.AppendTriangle(v0, v3, v2); // opposite v1, normal -x
        mesh.AppendTriangle(v1, v2, v3); // opposite v0, normal +x+y+z

        return mesh;
    }

    // Same tetrahedron, but the face opposite v0 has its vertex order
    // reversed, so it disagrees with all three of its neighbors.
    private static DMesh3 BuildTetrahedronWithOneFlippedFace()
    {
        var mesh = new DMesh3();
        int v0 = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int v1 = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int v2 = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int v3 = mesh.AppendVertex(new Vector3d(0, 0, 1));

        mesh.AppendTriangle(v0, v2, v1);
        mesh.AppendTriangle(v0, v1, v3);
        mesh.AppendTriangle(v0, v3, v2);
        mesh.AppendTriangle(v1, v3, v2); // flipped: was (v1, v2, v3)

        return mesh;
    }

    [Fact]
    public void Detect_ConsistentTetrahedron_ReportsNoIssues()
    {
        DMesh3 mesh = BuildConsistentTetrahedron();
        var detector = new InvertedNormalDetector();

        IReadOnlyList<MeshIssue> issues = detector.Detect(mesh);

        Assert.Empty(issues);
    }

    [Fact]
    public void Detect_OneFlippedFace_ReportsOneIssuePerSharedEdge()
    {
        DMesh3 mesh = BuildTetrahedronWithOneFlippedFace();
        var detector = new InvertedNormalDetector();

        IReadOnlyList<MeshIssue> issues = detector.Detect(mesh);

        // The flipped face shares an edge with each of the other 3 faces,
        // and disagrees with all of them.
        Assert.Equal(3, issues.Count);
        Assert.All(issues, issue =>
        {
            Assert.Equal("InvertedNormal", issue.Category);
            Assert.Equal(MeshIssueSeverity.Error, issue.Severity);
            Assert.Equal(2, issue.TriangleIds.Count);
            Assert.Single(issue.EdgeIds);
        });
    }
}
