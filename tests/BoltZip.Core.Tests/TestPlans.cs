using BoltZip.Core.Compression;
using BoltZip.Core.Hardware;

namespace BoltZip.Core.Tests;

internal static class TestPlans
{
    public static CompressionPlan For(ArchiveFormat format, OptimizationGoal goal = OptimizationGoal.Balanced)
        => OptimizationPlanner.Plan(HardwareProbe.DetectFast(), goal, format, null, StorageKind.Ssd);
}
