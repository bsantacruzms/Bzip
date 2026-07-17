using BoltZip.Core.Hardware;
using Xunit;

namespace BoltZip.Core.Tests;

public class HardwareProbeTests
{
    [Fact]
    public void DetectFast_ReturnsSaneValues()
    {
        var profile = HardwareProbe.DetectFast();

        Assert.Equal(Environment.ProcessorCount, profile.LogicalCores);
        Assert.True(profile.LogicalCores >= 1);
        Assert.True(profile.TotalMemoryBytes >= 0);
        Assert.NotNull(profile.Gpus);
        Assert.False(string.IsNullOrWhiteSpace(profile.Summary()));
    }

    [Fact]
    public void ParseGpuJson_ParsesArray()
    {
        const string json =
            "[{\"Name\":\"NVIDIA GeForce RTX 5090\",\"AdapterRAM\":4293918720,\"DriverVersion\":\"31.0.15\"}," +
            "{\"Name\":\"Intel UHD Graphics\",\"AdapterRAM\":1073741824,\"DriverVersion\":\"27.0.1\"}]";

        var gpus = HardwareProbe.ParseGpuJson(json);

        Assert.Equal(2, gpus.Count);
        Assert.Equal(GpuVendor.Nvidia, gpus[0].Vendor);
        Assert.True(gpus[0].IsDiscrete);
        Assert.Equal(GpuVendor.Intel, gpus[1].Vendor);
    }

    [Fact]
    public void ParseGpuJson_ParsesSingleObject()
    {
        const string json = "{\"Name\":\"AMD Radeon RX 7900 XTX\",\"AdapterRAM\":4293918720,\"DriverVersion\":null}";

        var gpus = HardwareProbe.ParseGpuJson(json);

        Assert.Single(gpus);
        Assert.Equal(GpuVendor.Amd, gpus[0].Vendor);
        Assert.True(gpus[0].IsDiscrete);
    }

    [Fact]
    public void ParseGpuJson_EmptyInputYieldsNoGpus()
    {
        Assert.Empty(HardwareProbe.ParseGpuJson(string.Empty));
        Assert.Empty(HardwareProbe.ParseGpuJson("not json"));
    }
}
