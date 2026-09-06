using System;
using System.Threading.Tasks;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Xunit;

namespace Meshwright.Tests;

/// <summary>
/// A refused operation costs the user nothing (backlog item 27).
///
/// <para>
/// Every refusal path in the app — a registration pin that will not fit, a drain hole too big for
/// the surface it is placed on, a plane cut through no geometry — returns the mesh untouched with
/// <see cref="OperationResult.Changed"/> false. <see cref="MeshDocument"/> used to refresh
/// regardless, so the refusal still pushed an undo step, still cleared the redo stack, and still
/// raised <see cref="MeshDocument.Changed"/> — which makes the window rebuild every gizmo and
/// empty its gizmo slot. The user-visible cost was losing a pin they had positioned by hand while
/// being told the pin could not be placed.
/// </para>
/// </summary>
public class RefusedOperationTests
{
    [Fact]
    public void ARefusalIsNotAnUndoStep()
    {
        MeshDocument document = LoadedDocument();

        OperationResult result = document.Apply(new RefusingOperation());

        Assert.False(result.Changed);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void ARefusalDoesNotDisturbTheHistoryAroundIt()
    {
        // The redo stack is the half that is easy to miss: a refusal between an undo and a redo
        // would silently throw the redo away.
        MeshDocument document = LoadedDocument();
        document.Apply(new GrowingOperation());
        document.Undo();
        Assert.True(document.CanRedo);

        document.Apply(new RefusingOperation());

        Assert.True(document.CanRedo);
        Assert.True(document.Redo());
    }

    [Fact]
    public void ARefusalRaisesNoChange_SoNothingOnScreenIsRebuilt()
    {
        MeshDocument document = LoadedDocument();
        int changes = 0;
        document.Changed += (_, _) => changes++;

        document.Apply(new RefusingOperation());

        Assert.Equal(0, changes);
    }

    [Fact]
    public void ARefusalStillTellsTheCallerWhy()
    {
        // Suppressing the refresh must not suppress the explanation - the panel shows this text.
        MeshDocument document = LoadedDocument();

        OperationResult result = document.Apply(new RefusingOperation());

        Assert.Equal("Nothing doing.", result.Summary);
    }

    [Fact]
    public async Task TheSameHoldsForTheAsyncPath()
    {
        // Every Edit panel applies through ApplyAsync, so a fix that only covered Apply would fix
        // nothing a user can reach.
        MeshDocument document = LoadedDocument();
        int changes = 0;
        document.Changed += (_, _) => changes++;

        OperationResult result = await document.ApplyAsync(new RefusingOperation());

        Assert.False(result.Changed);
        Assert.False(document.CanUndo);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void AnOperationThatDoesChangeTheMeshIsStillUndoable()
    {
        // The guard against fixing item 27 by never recording anything.
        MeshDocument document = LoadedDocument();
        int before = document.Mesh!.TriangleCount;

        document.Apply(new GrowingOperation());

        Assert.True(document.CanUndo);
        Assert.True(document.Mesh!.TriangleCount > before);

        document.Undo();

        Assert.Equal(before, document.Mesh!.TriangleCount);
    }

    private static MeshDocument LoadedDocument()
    {
        var document = new MeshDocument();
        document.Load(Tetrahedron());
        return document;
    }

    private static DMesh3 Tetrahedron()
    {
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(10, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 10, 0));
        int d = mesh.AppendVertex(new Vector3d(0, 0, 10));
        mesh.AppendTriangle(a, c, b);
        mesh.AppendTriangle(a, b, d);
        mesh.AppendTriangle(b, c, d);
        mesh.AppendTriangle(c, a, d);
        return mesh;
    }

    private sealed class RefusingOperation : IMeshOperation
    {
        public string Name => "Refuse";

        public OperationResult Apply(DMesh3 mesh) => new(Changed: false, Summary: "Nothing doing.");

        public OperationResult Preview(DMesh3 mesh) => new(Changed: false, Summary: "Nothing doing.");
    }

    private sealed class GrowingOperation : IMeshOperation
    {
        public string Name => "Grow";

        public OperationResult Apply(DMesh3 mesh)
        {
            int a = mesh.AppendVertex(new Vector3d(20, 20, 20));
            int b = mesh.AppendVertex(new Vector3d(21, 20, 20));
            int c = mesh.AppendVertex(new Vector3d(20, 21, 20));
            mesh.AppendTriangle(a, b, c);
            return new OperationResult(Changed: true, Summary: "Grew.");
        }

        public OperationResult Preview(DMesh3 mesh) => new(Changed: true, Summary: "Would grow.");
    }
}
