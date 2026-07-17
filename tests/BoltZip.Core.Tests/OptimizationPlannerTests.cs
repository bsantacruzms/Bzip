using System.Numerics;
using System.Runtime.InteropServices;
using BoltZip.Core.Compression;
using BoltZip.Core.Hardware;
using Xunit;

namespace BoltZip.Core.Tests;

public class OptimizationPlannerTests
{
    private static HardwareProfile Hardware(int cores, long ramBytes, StorageKind storage, bool aes = true) => new()
    {
        Architecture = Architecture.X64,
        LogicalCores = cores,
        TotalMemoryBytes = ramBytes,
        AvailableMemoryBytes = ramBytes,
        SupportsHardwareAes = aes,
        SupportsAvx2 = true,
        SupportsAvx512 = false,
        SystemStorage = storage,
        Gpus = Array.Empty<GpuInfo>(),
    };

    [Fact]
    public void Plan_NeverExceedsLogicalCores()
    {
        var plan = OptimizationPlanner.Plan(Hardware(8, 16L << 30, StorageKind.Nvme), OptimizationGoal.Balanced, ArchiveFormat.Bz, 1L << 30);
        Assert.InRange(plan.WorkerThreads, 1, 8);
    }

    [Fact]
    public void Plan_HddCapsThreads()
    {
        var plan = OptimizationPlanner.Plan(Hardware(16, 32L << 30, StorageKind.Hdd), OptimizationGoal.Balanced, ArchiveFormat.Bz, 4L << 30);
        Assert.True(plan.WorkerThreads <= 4, $"expected <=4 threads on HDD, got {plan.WorkerThreads}");
    }

    [Fact]
    public void Plan_MaxRatioIsStrongerThanMaxSpeed()
    {
        var hw = Hardware(8, 16L << 30, StorageKind.Ssd);
        var fast = OptimizationPlanner.Plan(hw, OptimizationGoal.MaxSpeed, ArchiveFormat.Bz, 1L << 30);
        var small = OptimizationPlanner.Plan(hw, OptimizationGoal.MaxRatio, ArchiveFormat.Bz, 1L << 30);
        Assert.True(small.Level > fast.Level);
    }

    [Fact]
    public void Plan_WindowIsPowerOfTwoWithinBudget()
    {
        var hw = Hardware(8, 8L << 30, StorageKind.Ssd);
        var plan = OptimizationPlanner.Plan(hw, OptimizationGoal.MaxRatio, ArchiveFormat.Bz, 2L << 30);
        Assert.True(BitOperations.IsPow2(plan.WindowBytes));
        Assert.True(plan.WindowBytes >= 256 * 1024);
        var estimate = plan.WindowBytes * 10 * plan.WorkerThreads;
        Assert.True(estimate <= hw.AvailableMemoryBytes / 4 || plan.WindowBytes == (1L << 18));
    }

    [Fact]
    public void Plan_SmallInputReducesThreads()
    {
        var plan = OptimizationPlanner.Plan(Hardware(16, 32L << 30, StorageKind.Nvme), OptimizationGoal.Balanced, ArchiveFormat.Bz, 1 << 20);
        Assert.True(plan.WorkerThreads < 16);
    }

    [Fact]
    public void Plan_ProducesRationale()
    {
        var plan = OptimizationPlanner.Plan(Hardware(8, 16L << 30, StorageKind.Ssd), OptimizationGoal.Balanced, ArchiveFormat.Bz, 1L << 30);
        Assert.NotEmpty(plan.Rationale);
    }

    [Fact]
    public void Plan_HonorsHardwareAesFlag()
    {
        var withAes = OptimizationPlanner.Plan(Hardware(4, 8L << 30, StorageKind.Ssd, aes: true), OptimizationGoal.Balanced, ArchiveFormat.Bz, 1L << 28);
        var withoutAes = OptimizationPlanner.Plan(Hardware(4, 8L << 30, StorageKind.Ssd, aes: false), OptimizationGoal.Balanced, ArchiveFormat.Bz, 1L << 28);
        Assert.True(withAes.HardwareAes);
        Assert.False(withoutAes.HardwareAes);
    }
}
