using System.Runtime.CompilerServices;
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
}
