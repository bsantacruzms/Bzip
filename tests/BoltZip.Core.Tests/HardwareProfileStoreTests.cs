using System.Runtime.InteropServices;
using BoltZip.Core.Hardware;
using Xunit;

namespace BoltZip.Core.Tests;

public class HardwareProfileStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsProfile()
    {
        var profile = new HardwareProfile
        {
            Architecture = Architecture.X64,
            LogicalCores = 24,
            TotalMemoryBytes = 32L * 1024 * 1024 * 1024,
            AvailableMemoryBytes = 20L * 1024 * 1024 * 1024,
            SupportsHardwareAes = true,
            SupportsAvx2 = true,
            SupportsAvx512 = false,
            SystemStorage = StorageKind.Ssd,
            Gpus = new[]
            {
                new GpuInfo("Test GPU", GpuVendor.Nvidia, 8L * 1024 * 1024 * 1024, "1.2.3"),
            },
        };

        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "boltzip-tests", Guid.NewGuid().ToString("N"), "hw.json");
        try
        {
            HardwareProfileStore.TrySave(path, profile);
            var loaded = HardwareProfileStore.TryLoad(path);

            Assert.NotNull(loaded);
            Assert.Equal(profile.Architecture, loaded!.Architecture);
            Assert.Equal(profile.LogicalCores, loaded.LogicalCores);
            Assert.Equal(profile.TotalMemoryBytes, loaded.TotalMemoryBytes);
            Assert.Equal(profile.SupportsHardwareAes, loaded.SupportsHardwareAes);
            Assert.Equal(profile.SupportsAvx512, loaded.SupportsAvx512);
            Assert.Equal(profile.SystemStorage, loaded.SystemStorage);
            Assert.Single(loaded.Gpus);
            Assert.Equal("Test GPU", loaded.Gpus[0].Name);
            Assert.Equal(GpuVendor.Nvidia, loaded.Gpus[0].Vendor);
        }
        finally
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (dir is not null && System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsNull()
    {
        var missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "nope.json");
        Assert.Null(HardwareProfileStore.TryLoad(missing));
    }
}
