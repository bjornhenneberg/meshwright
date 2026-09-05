using Xunit;

// The GPU tests each create their own real GLFW window + OpenGL context via
// GpuTestFixture (see GpuTestFixture.cs). GLFW/GLX window creation on Linux is not
// safe to call concurrently from multiple threads. xunit's default behavior treats
// every test class as its own collection and runs collections in parallel, so with
// three IClassFixture<GpuTestFixture> classes in this assembly (BrokenSampleRenderGpuTests,
// MeshRendererGpuTests, ViewportGizmoGpuTests), xunit was constructing multiple
// GpuTestFixture instances concurrently on different threads, each calling
// Glfw.CreateWindow at the same time. That races inside GLFW/X11 and can hang forever
// (observed: managed stacks of a live hung process showed two threads both blocked
// inside GpuTestFixture..ctor -> Glfw.CreateWindow, see reports/M4/gpu-hang/).
//
// Disabling parallelization for this assembly serializes fixture construction and
// eliminates the race. This assembly is small (8 tests) so serial execution is cheap.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
