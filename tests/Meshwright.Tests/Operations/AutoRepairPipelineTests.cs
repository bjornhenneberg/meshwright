using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using g3;
using Meshwright.Core;
using Meshwright.Core.Operations;
using Meshwright.Geometry.Diagnostics;
using Meshwright.IO.Stl;
using Xunit;

namespace Meshwright.Tests.Operations;

public class AutoRepairPipelineTests
{
    private sealed class FakeOperation : IMeshOperation
    {
        private readonly OperationResult _result;

        public FakeOperation(string name, OperationResult result)
        {
            Name = name;
            _result = result;
        }

        public string Name { get; }

        public OperationResult Preview(DMesh3 mesh) => _result;

        public OperationResult Apply(DMesh3 mesh) => _result;
    }

    [Fact]
    public void Execute_RunsStepsInOrderAndJoinsOnlyChangedSummaries()
    {
        var pipeline = new AutoRepairPipeline(new IMeshOperation[]
        {
            new FakeOperation("A", new OperationResult(true, "did A.")),
            new FakeOperation("B", new OperationResult(false, "B was a no-op.")),
            new FakeOperation("C", new OperationResult(true, "did C.")),
        });

        OperationResult result = pipeline.Apply(new DMesh3());

        Assert.True(result.Changed);
        Assert.Equal("did A. did C.", result.Summary);
    }

    [Fact]
    public void Execute_NoStepChanged_ReportsNoRepairsNeeded()
    {
        var pipeline = new AutoRepairPipeline(new IMeshOperation[]
        {
            new FakeOperation("A", new OperationResult(false, "nothing here.")),
        });

        OperationResult result = pipeline.Apply(new DMesh3());

        Assert.False(result.Changed);
        Assert.Equal("No repairs needed.", result.Summary);
    }

    [Fact]
    public void Preview_DoesNotMutateTheCallersMesh()
    {
        var pipeline = new AutoRepairPipeline(new IMeshOperation[]
        {
            new FakeOperation("A", new OperationResult(true, "did A.")),
        });
        var mesh = new DMesh3();
        int a = mesh.AppendVertex(new Vector3d(0, 0, 0));
        int b = mesh.AppendVertex(new Vector3d(1, 0, 0));
        int c = mesh.AppendVertex(new Vector3d(0, 1, 0));
        mesh.AppendTriangle(a, b, c);
        int triangleCountBefore = mesh.TriangleCount;

        pipeline.Preview(mesh);

        Assert.Equal(triangleCountBefore, mesh.TriangleCount);
    }

    /// <summary>
    /// End-to-end proof that the default pipeline actually repairs a real broken mesh, using the
    /// same fixture M1's acceptance test used (a hole, a stray shell, and a flipped face) — not
    /// just the fake-operation sequencing logic above.
    /// </summary>
    [Fact]
    public void Apply_DefaultPipeline_RepairsBrokenSampleThroughMeshDocument()
    {
        DMesh3 mesh = StlReader.ReadFile(GetFixturePath("BrokenSample.stl"));
        var document = new MeshDocument();
        document.Load(mesh);
        Assert.NotEmpty(document.Report!.Issues);

        OperationResult result = document.Apply(new AutoRepairPipeline());

        Assert.True(result.Changed);
        Assert.DoesNotContain(document.Report!.Issues, issue => issue.Category == "BoundaryHole");
        Assert.DoesNotContain(document.Report.Issues, issue => issue.Category == "InvertedNormal");
        Assert.DoesNotContain(document.Report.Issues, issue => issue.Category == "DisconnectedShell");

        Assert.True(document.CanUndo);
        bool undone = document.Undo();
        Assert.True(undone);
        Assert.Contains(document.Report!.Issues, issue => issue.Category == "BoundaryHole");
    }

    private static string GetFixturePath(string fileName, [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "Fixtures", fileName);

    /// <summary>
    /// The pipeline is the one operation that can report real, non-faked progress (backlog item
    /// 13): each step is a genuine checkpoint, so the fraction reported must actually correspond
    /// to "steps completed / steps total", not just be present.
    /// </summary>
    [Fact]
    public void Apply_WithProgress_ReportsOneUpdatePerStepInIncreasingOrder()
    {
        var pipeline = new AutoRepairPipeline(new IMeshOperation[]
        {
            new FakeOperation("A", new OperationResult(true, "did A.")),
            new FakeOperation("B", new OperationResult(true, "did B.")),
            new FakeOperation("C", new OperationResult(true, "did C.")),
        });
        var updates = new List<OperationProgress>();
        var progress = new SynchronousProgress<OperationProgress>(updates.Add);

        pipeline.Apply(new DMesh3(), progress, CancellationToken.None);

        // 3 step-start reports plus a final "Done" report.
        Assert.Equal(4, updates.Count);
        Assert.Equal(new double?[] { 0.0, 1.0 / 3, 2.0 / 3, 1.0 }, updates.Select(u => u.FractionComplete));
        Assert.Contains("A (1/3)", updates[0].Description);
        Assert.Contains("C (3/3)", updates[2].Description);
    }

    /// <summary>
    /// A cancellation request is honoured only between steps (§4: no faked mid-step stop), and
    /// the steps that did complete stay applied — a partial repair is a valid, reportable outcome,
    /// not a failure to be rolled back by this method.
    /// </summary>
    [Fact]
    public void Apply_CancelledAfterFirstStep_StopsBeforeLaterStepsAndSaysSo()
    {
        using var cts = new CancellationTokenSource();
        bool secondStepRan = false;
        var pipeline = new AutoRepairPipeline(new IMeshOperation[]
        {
            new DelegateOperation("A", mesh =>
            {
                cts.Cancel();
                return new OperationResult(true, "did A.");
            }),
            new DelegateOperation("B", mesh =>
            {
                secondStepRan = true;
                return new OperationResult(true, "did B.");
            }),
        });

        OperationResult result = pipeline.Apply(new DMesh3(), new SynchronousProgress<OperationProgress>(_ => { }), cts.Token);

        Assert.False(secondStepRan);
        Assert.True(result.Changed);
        Assert.Contains("Cancelled after 1 of 2", result.Summary);
        Assert.Contains("did A.", result.Summary);
    }

    private sealed class DelegateOperation : IMeshOperation
    {
        private readonly Func<DMesh3, OperationResult> _apply;

        public DelegateOperation(string name, Func<DMesh3, OperationResult> apply)
        {
            Name = name;
            _apply = apply;
        }

        public string Name { get; }

        public OperationResult Preview(DMesh3 mesh) => _apply(mesh);

        public OperationResult Apply(DMesh3 mesh) => _apply(mesh);
    }

    /// <summary>Reports synchronously and inline rather than via <see cref="Progress{T}"/>'s
    /// SynchronizationContext post, so ordering assertions above don't depend on a message pump
    /// being present in this plain xUnit test.</summary>
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public SynchronousProgress(Action<T> report) => _report = report;

        public void Report(T value) => _report(value);
    }
}
