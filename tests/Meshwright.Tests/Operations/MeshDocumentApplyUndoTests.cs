using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Xunit;

namespace Meshwright.Tests.Operations;

/// <summary>
/// Exercises <see cref="MeshDocument.Apply"/>/<see cref="MeshDocument.Undo"/>/<see cref="MeshDocument.Redo"/>
/// through a trivial deterministic operation, since the real repair operations (M2) are tested against
/// this same contract independently.
/// </summary>
public class MeshDocumentApplyUndoTests
{
    [Fact]
    public void Apply_MutatesMeshAndRecomputesReport()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        int triangleCountBefore = document.Report!.Statistics.TriangleCount;

        OperationResult result = document.Apply(new RemoveOneTriangleOperation());

        Assert.True(result.Changed);
        Assert.Equal(triangleCountBefore - 1, document.Report!.Statistics.TriangleCount);
    }

    [Fact]
    public void Undo_AfterApply_RestoresPriorMeshAndReport()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        int triangleCountBefore = document.Report!.Statistics.TriangleCount;

        document.Apply(new RemoveOneTriangleOperation());
        Assert.True(document.CanUndo);

        bool undone = document.Undo();

        Assert.True(undone);
        Assert.Equal(triangleCountBefore, document.Report!.Statistics.TriangleCount);
        Assert.False(document.CanUndo);
        Assert.True(document.CanRedo);
    }

    [Fact]
    public void Redo_AfterUndo_ReappliesTheOperation()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        int triangleCountBefore = document.Report!.Statistics.TriangleCount;

        document.Apply(new RemoveOneTriangleOperation());
        document.Undo();
        bool redone = document.Redo();

        Assert.True(redone);
        Assert.Equal(triangleCountBefore - 1, document.Report!.Statistics.TriangleCount);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void Preview_DoesNotMutateTheDocumentsMesh()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        int triangleCountBefore = document.Report!.Statistics.TriangleCount;

        OperationResult result = new RemoveOneTriangleOperation().Preview(document.Mesh!);

        Assert.True(result.Changed);
        Assert.Equal(triangleCountBefore, document.Report!.Statistics.TriangleCount);
    }

    /// <summary>
    /// Every mesh change has to announce itself. The Edit panels apply operations straight to the
    /// document, so if <see cref="MeshDocument.Changed"/> stops firing they mutate the mesh with
    /// nothing on screen moving — the viewport keeps rendering its uploaded copy and the
    /// diagnostics panel keeps showing the pre-operation report, which reads to a user as the
    /// button doing nothing at all.
    /// </summary>
    [Fact]
    public void Apply_RaisesChangedNamingTheOperation()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        var reasons = new List<string?>();
        document.Changed += (_, _) => reasons.Add(document.LastChangeDescription);

        document.Apply(new RemoveOneTriangleOperation());

        Assert.Equal(new[] { "Remove one triangle (test double)" }, reasons);
    }

    [Fact]
    public void LoadUndoAndRedo_EachRaiseChangedWithTheirOwnReason()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        document.Apply(new RemoveOneTriangleOperation());

        var reasons = new List<string?>();
        document.Changed += (_, _) => reasons.Add(document.LastChangeDescription);

        document.Undo();
        document.Redo();
        document.Load(new DMesh3(document.Mesh!));

        Assert.Equal(new[] { "Undo", "Redo", "Loaded" }, reasons);
    }

    [Fact]
    public void Changed_ReportsTheAlreadyRecomputedReport_NotTheStaleOne()
    {
        // The handler runs the UI refresh, so the report must already reflect the operation by
        // the time it fires — otherwise the panel repaints with pre-operation numbers.
        MeshDocument document = NewDocumentWithTetrahedron();
        int triangleCountBefore = document.Report!.Statistics.TriangleCount;
        int? triangleCountSeenByHandler = null;
        document.Changed += (_, _) => triangleCountSeenByHandler = document.Report!.Statistics.TriangleCount;

        document.Apply(new RemoveOneTriangleOperation());

        Assert.Equal(triangleCountBefore - 1, triangleCountSeenByHandler);
    }

    private static MeshDocument NewDocumentWithTetrahedron()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        int d = mesh.AppendVertex(new Vector3d(0, 0, 1));
        mesh.AppendTriangle(a, c, b);
        mesh.AppendTriangle(a, b, d);
        mesh.AppendTriangle(b, c, d);
        mesh.AppendTriangle(c, a, d);

        var document = new MeshDocument();
        document.Load(mesh);
        return document;
    }

    private sealed class RemoveOneTriangleOperation : MeshOperationBase
    {
        public override string Name => "Remove one triangle (test double)";

        protected override OperationResult Execute(DMesh3 mesh)
        {
            foreach (int tid in mesh.TriangleIndices())
            {
                mesh.RemoveTriangle(tid, bRemoveIsolatedVertices: false);
                return new OperationResult(true, "Removed 1 triangle.");
            }

            return new OperationResult(false, "No triangles to remove.");
        }
    }
}
