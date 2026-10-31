using System;
using Unity.Profiling;
namespace MG.GodSurface
{
    public sealed partial class MGSurfaceWorld
    {
        static readonly ProfilerMarker rebuildMarker = new ProfilerMarker("MGSurface.Rebuild");
        public int RebuildCount { get; private set; }
        public int CullCalls { get; private set; }
        public double RebuildMilliseconds { get; private set; }
        public double CullMilliseconds { get; private set; }
        readonly struct MeasuredScope : IDisposable
        {
            readonly MGSurfaceWorld world; readonly bool rebuild; readonly long start;
            public MeasuredScope(MGSurfaceWorld world, bool rebuild) { this.world=world; this.rebuild=rebuild; start=System.Diagnostics.Stopwatch.GetTimestamp(); }
            public void Dispose()
            {
                double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
                if(rebuild){world.RebuildCount++;world.RebuildMilliseconds+=ms;}else{world.CullCalls++;world.CullMilliseconds+=ms;}
            }
        }
    }
}
