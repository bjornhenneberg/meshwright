using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Xunit;

namespace Meshwright.Tests.Operations;

/// <summary>
/// Exercises <see cref="MeshDocument.ApplyAsync"/> (backlog item 13: long operations run off the
/// UI thread). The point of every test here is that it would fail if <c>ApplyAsync</c> ran its
/// operation inline on the calling thread instead of on a background one — a test that merely
/// awaits the result and checks it would pass either way and prove nothing about responsiveness.
/// </summary>
public class MeshDocumentApplyAsyncTests
{
    [Fact]
    public async Task ApplyAsync_DoesNotBlockTheCallingThreadWhileTheOperationRuns()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        using var release = new ManualResetEventSlim(false);
        var operation = new BlockingOperation(release);

        Task<OperationResult> applyTask = document.ApplyAsync(operation);

        // If ApplyAsync ran the operation inline on this thread, the call above would already be
        // blocked inside BlockingOperation.Execute waiting on `release`, and this line would never
        // be reached until `release.Set()` below — i.e. this test would hang and time out rather
        // than pass. Getting here at all is the proof the calling thread was freed immediately.
        Assert.True(operation.EnteredExecute.Wait(TimeSpan.FromSeconds(5)), "operation never started");
        Assert.False(applyTask.IsCompleted);
        Assert.True(document.IsBusy);

        release.Set();
        OperationResult result = await applyTask;

        Assert.True(result.Changed);
        Assert.False(document.IsBusy);
    }

    /// <summary>
    /// The busy indicator names the operation the user is waiting on. Reading
    /// <see cref="MeshDocument.LastChangeDescription"/> instead labels it with the *previous*
    /// completed change, which showed "Working: Loaded..." while a hollow was running.
    /// </summary>
    [Fact]
    public async Task CurrentOperationName_NamesTheRunningOperation_NotThePreviousChange()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        using var release = new ManualResetEventSlim(false);
        var operation = new BlockingOperation(release);

        Assert.Null(document.CurrentOperationName);
        Assert.Equal("Loaded", document.LastChangeDescription);

        Task<OperationResult> applyTask = document.ApplyAsync(operation);
        Assert.True(operation.EnteredExecute.Wait(TimeSpan.FromSeconds(5)), "operation never started");

        Assert.Equal(operation.Name, document.CurrentOperationName);
        Assert.NotEqual(document.LastChangeDescription, document.CurrentOperationName);

        release.Set();
        await applyTask;

        Assert.Null(document.CurrentOperationName);
    }

    [Fact]
    public async Task ApplyAsync_WhileBusy_RejectsASecondCallInsteadOfRunningConcurrently()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        using var release = new ManualResetEventSlim(false);
        var first = new BlockingOperation(release);

        Task<OperationResult> firstTask = document.ApplyAsync(first);
        Assert.True(first.EnteredExecute.Wait(TimeSpan.FromSeconds(5)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => document.ApplyAsync(new RemoveOneTriangleOperation()));

        release.Set();
        await firstTask;
    }

    [Fact]
    public async Task Undo_WhileBusy_ReturnsFalseRatherThanRacingTheBackgroundMutation()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        document.Apply(new RemoveOneTriangleOperation());
        Assert.True(document.CanUndo);

        using var release = new ManualResetEventSlim(false);
        var operation = new BlockingOperation(release);
        Task<OperationResult> applyTask = document.ApplyAsync(operation);
        Assert.True(operation.EnteredExecute.Wait(TimeSpan.FromSeconds(5)));

        Assert.False(document.CanUndo);
        Assert.False(document.Undo());

        release.Set();
        await applyTask;
    }

    [Fact]
    public async Task ApplyAsync_RaisesBusyChangedBeforeStartingAndAfterFinishing()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        var busyStates = new List<bool>();
        document.BusyChanged += (_, _) => busyStates.Add(document.IsBusy);

        await document.ApplyAsync(new RemoveOneTriangleOperation());

        Assert.Equal(new[] { true, false }, busyStates);
    }

    [Fact]
    public async Task ApplyAsync_RaisesChangedOnceFinished_SoTheUiCanRefresh()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        int triangleCountBefore = document.Report!.Statistics.TriangleCount;
        var reasons = new List<string?>();
        document.Changed += (_, _) => reasons.Add(document.LastChangeDescription);

        OperationResult result = await document.ApplyAsync(new RemoveOneTriangleOperation());

        Assert.True(result.Changed);
        Assert.Equal(new[] { "Remove one triangle (test double)" }, reasons);
        Assert.Equal(triangleCountBefore - 1, document.Report!.Statistics.TriangleCount);
    }

    [Fact]
    public async Task ApplyAsync_ThenUndo_RestoresThePreOperationMesh()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        int triangleCountBefore = document.Report!.Statistics.TriangleCount;

        await document.ApplyAsync(new RemoveOneTriangleOperation());
        Assert.True(document.CanUndo);

        bool undone = document.Undo();

        Assert.True(undone);
        Assert.Equal(triangleCountBefore, document.Report!.Statistics.TriangleCount);
    }

    /// <summary>Only a step-based operation can honour cancellation (§4: no faked cancel button);
    /// a plain one-shot operation reports it cannot, and asking anyway is a no-op that still runs
    /// to completion.</summary>
    [Fact]
    public async Task CanCancelCurrentOperation_IsFalseForAnOperationWithNoCheckpoints()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        using var release = new ManualResetEventSlim(false);
        var operation = new BlockingOperation(release);

        Task<OperationResult> applyTask = document.ApplyAsync(operation);
        Assert.True(operation.EnteredExecute.Wait(TimeSpan.FromSeconds(5)));

        Assert.False(document.CanCancelCurrentOperation);

        release.Set();
        await applyTask;
        Assert.False(document.CanCancelCurrentOperation);
    }

    [Fact]
    public async Task CancelCurrentOperation_OnAPipeline_StopsItBetweenStepsAndStillRefreshesTheDocument()
    {
        MeshDocument document = NewDocumentWithTetrahedron();
        var pipeline = new AutoRepairPipeline(new IMeshOperation[]
        {
            new CancellingStepOperation(document),
            new NeverReachedOperation(),
        });

        OperationResult result = await document.ApplyAsync(pipeline);

        Assert.True(result.Changed);
        Assert.Contains("Cancelled after 1 of 2", result.Summary);
        Assert.False(document.IsBusy);
        // The document still refreshed from the (partially) mutated mesh — the whole point of
        // MeshDocument.Changed is that the UI never renders a stale copy after an Apply returns.
        Assert.NotNull(document.Report);
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

    /// <summary>Blocks inside Execute until told to release, so tests can prove the calling
    /// thread was not the one running it.</summary>
    private sealed class BlockingOperation : MeshOperationBase
    {
        private readonly ManualResetEventSlim _release;

        public BlockingOperation(ManualResetEventSlim release) => _release = release;

        public ManualResetEventSlim EnteredExecute { get; } = new(false);

        public override string Name => "Blocking (test double)";

        protected override OperationResult Execute(DMesh3 mesh)
        {
            EnteredExecute.Set();
            _release.Wait();
            return new OperationResult(true, "Blocked, then ran.");
        }
    }

    /// <summary>Simulates the user clicking Cancel while this step is running: calls the real
    /// <see cref="MeshDocument.CancelCurrentOperation"/> path from inside the background work
    /// itself, which is the only way to reach it deterministically in a single-threaded test.</summary>
    private sealed class CancellingStepOperation : IMeshOperation
    {
        private readonly MeshDocument _document;

        public CancellingStepOperation(MeshDocument document) => _document = document;

        public string Name => "Cancel-triggering step (test double)";

        public OperationResult Preview(DMesh3 mesh) => Apply(mesh);

        public OperationResult Apply(DMesh3 mesh)
        {
            foreach (int tid in mesh.TriangleIndices())
            {
                mesh.RemoveTriangle(tid, bRemoveIsolatedVertices: false);
                break;
            }

            _document.CancelCurrentOperation();
            return new OperationResult(true, "Removed 1 triangle, then the pipeline was asked to cancel.");
        }
    }

    private sealed class NeverReachedOperation : IMeshOperation
    {
        public string Name => "Should never run (test double)";

        public OperationResult Preview(DMesh3 mesh) => Apply(mesh);

        public OperationResult Apply(DMesh3 mesh) =>
            throw new InvalidOperationException("This step must not run once cancellation was requested.");
    }
}
