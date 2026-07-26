using System.Runtime.InteropServices;
using BoltZip.Core.Hardware;
using BoltZip.Core.Media;
using Xunit;

namespace BoltZip.Core.Tests;

public class VideoCompressorTests
{
    private static HardwareProfile Hw(params GpuInfo[] gpus) => new()
    {
        Architecture = Architecture.X64,
        LogicalCores = 24,
        TotalMemoryBytes = 32L << 30,
        AvailableMemoryBytes = 16L << 30,
        SupportsHardwareAes = true,
        SupportsAvx2 = true,
        SupportsAvx512 = false,
        SystemStorage = StorageKind.Nvme,
        Gpus = gpus,
    };

    private static GpuInfo Nvidia => new("NVIDIA GeForce RTX 5090", GpuVendor.Nvidia, 32L << 30, "610.74");
    private static GpuInfo Amd => new("AMD Radeon RX 7900 XTX", GpuVendor.Amd, 24L << 30, null);
    private static GpuInfo Intel => new("Intel Arc A770", GpuVendor.Intel, 16L << 30, null);

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var i = args.ToList().IndexOf(flag);
        Assert.True(i >= 0 && i + 1 < args.Count, $"flag {flag} not found with a value");
        return args[i + 1];
    }

    [Fact]
    public void PlanEncode_NvidiaAuto_UsesHevcNvenc_WithCpuFallback()
    {
        var plan = VideoCompressor.PlanEncode(Hw(Nvidia), VideoCodec.Auto, VideoQuality.VisuallyLossless, macOs: false);
        Assert.Equal("hevc_nvenc", plan.Primary.FfmpegName);
        Assert.True(plan.Primary.IsHardware);
        Assert.Equal("libx265", plan.CpuFallback.FfmpegName);
        Assert.False(plan.CpuFallback.IsHardware);
    }

    [Fact]
    public void PlanEncode_NvidiaAv1_UsesAv1Nvenc()
    {
        var plan = VideoCompressor.PlanEncode(Hw(Nvidia), VideoCodec.Av1, VideoQuality.Balanced, macOs: false);
        Assert.Equal("av1_nvenc", plan.Primary.FfmpegName);
        Assert.Equal("libsvtav1", plan.CpuFallback.FfmpegName);
    }

    [Fact]
    public void PlanEncode_ForceCpu_UsesLibx265()
    {
        var plan = VideoCompressor.PlanEncode(Hw(Nvidia), VideoCodec.Hevc, VideoQuality.VisuallyLossless, forceCpu: true);
        Assert.Equal("libx265", plan.Primary.FfmpegName);
    }

    [Fact]
    public void PlanEncode_NoGpu_UsesCpu()
    {
        var plan = VideoCompressor.PlanEncode(Hw(), VideoCodec.Auto, VideoQuality.Balanced, macOs: false);
        Assert.False(plan.Primary.IsHardware);
        Assert.Equal("libx265", plan.Primary.FfmpegName);
    }

    [Fact]
    public void PlanEncode_MacOs_UsesVideoToolbox_EvenWithoutDetectedGpu()
    {
        // macOS reports no GPU through the Windows/Linux paths, but every Mac has VideoToolbox.
        var plan = VideoCompressor.PlanEncode(Hw(), VideoCodec.Auto, VideoQuality.VisuallyLossless, macOs: true);
        Assert.Equal("hevc_videotoolbox", plan.Primary.FfmpegName);
        Assert.True(plan.Primary.IsHardware);
        Assert.Equal(GpuVendor.Apple, plan.Primary.Vendor);
        Assert.Equal("libx265", plan.CpuFallback.FfmpegName);
    }

    [Fact]
    public void PlanEncode_MacOsH264_UsesVideoToolbox()
    {
        var plan = VideoCompressor.PlanEncode(Hw(), VideoCodec.H264, VideoQuality.Balanced, macOs: true);
        Assert.Equal("h264_videotoolbox", plan.Primary.FfmpegName);
    }

    [Fact]
    public void PlanEncode_MacOsAv1_FallsBackToCpu_BecauseVideoToolboxHasNoAv1Encoder()
    {
        var plan = VideoCompressor.PlanEncode(Hw(), VideoCodec.Av1, VideoQuality.Balanced, macOs: true);
        Assert.Equal("libsvtav1", plan.Primary.FfmpegName);
        Assert.False(plan.Primary.IsHardware);
    }

    [Fact]
    public void PlanEncode_ForceCpu_WinsOverMacHardware()
    {
        var plan = VideoCompressor.PlanEncode(Hw(), VideoCodec.Hevc, VideoQuality.Balanced, forceCpu: true, macOs: true);
        Assert.Equal("libx265", plan.Primary.FfmpegName);
    }

    [Fact]
    public void RateControlArgs_VideoToolbox_UsesInvertedQualityScale()
    {
        var vl = int.Parse(ValueAfter(VideoCompressor.RateControlArgs("hevc_videotoolbox", VideoQuality.VisuallyLossless), "-q:v"));
        var smaller = int.Parse(ValueAfter(VideoCompressor.RateControlArgs("hevc_videotoolbox", VideoQuality.Smaller), "-q:v"));
        // VideoToolbox: higher number means better quality, the opposite of CRF/CQ.
        Assert.True(vl > smaller, $"visually-lossless q {vl} should exceed smaller q {smaller}");
        Assert.Contains("-allow_sw", VideoCompressor.RateControlArgs("hevc_videotoolbox", VideoQuality.Balanced));
    }

    [Fact]
    public void PlanEncode_Amd_UsesAmf()
    {
        var plan = VideoCompressor.PlanEncode(Hw(Amd), VideoCodec.Hevc, VideoQuality.Balanced, macOs: false);
        Assert.Equal("hevc_amf", plan.Primary.FfmpegName);
        Assert.True(plan.Primary.IsHardware);
    }

    [Fact]
    public void PlanEncode_Intel_UsesQuickSync()
    {
        var plan = VideoCompressor.PlanEncode(Hw(Intel), VideoCodec.Hevc, VideoQuality.Balanced, macOs: false);
        Assert.Equal("hevc_qsv", plan.Primary.FfmpegName);
        Assert.True(plan.Primary.IsHardware);
    }

    [Fact]
    public void RateControlArgs_Nvenc_UsesConstantQualityVbr()
    {
        var args = VideoCompressor.RateControlArgs("hevc_nvenc", VideoQuality.VisuallyLossless);
        Assert.Contains("-cq", args);
        Assert.Equal("vbr", ValueAfter(args, "-rc"));
    }

    [Fact]
    public void RateControlArgs_Cpu_UsesCrf()
    {
        var args = VideoCompressor.RateControlArgs("libx265", VideoQuality.VisuallyLossless);
        Assert.Contains("-crf", args);
    }

    [Fact]
    public void RateControlArgs_HigherQualityMeansLowerCqNumber()
    {
        var vl = int.Parse(ValueAfter(VideoCompressor.RateControlArgs("hevc_nvenc", VideoQuality.VisuallyLossless), "-cq"));
        var smaller = int.Parse(ValueAfter(VideoCompressor.RateControlArgs("hevc_nvenc", VideoQuality.Smaller), "-cq"));
        Assert.True(vl < smaller, $"visually-lossless cq {vl} should be below smaller cq {smaller}");
    }

    [Fact]
    public void BuildArguments_Mp4Hevc_TagsHvc1_CopiesAudio_Faststart()
    {
        var encoder = new VideoEncoder("hevc_nvenc", "NVIDIA HEVC (NVENC)", VideoCodec.Hevc, true, GpuVendor.Nvidia);
        var args = VideoCompressor.BuildArguments("in.mkv", "out.mp4", encoder, VideoQuality.VisuallyLossless);

        Assert.Equal("copy", ValueAfter(args, "-c:a"));
        Assert.Equal("hevc_nvenc", ValueAfter(args, "-c:v"));
        Assert.Contains("hvc1", args);
        Assert.Contains("+faststart", args);
        Assert.Equal("out.mp4", args[^1]);
        Assert.Contains("0:v:0", args);
    }

    [Fact]
    public void BuildArguments_Mkv_HasNoFaststart()
    {
        var encoder = new VideoEncoder("hevc_nvenc", "NVIDIA HEVC (NVENC)", VideoCodec.Hevc, true, GpuVendor.Nvidia);
        var args = VideoCompressor.BuildArguments("in.mp4", "out.mkv", encoder, VideoQuality.Balanced);
        Assert.DoesNotContain("+faststart", args);
    }

    [Fact]
    public void DefaultOutputPath_ChoosesMp4_AndAvoidsOverwritingInput()
    {
        var fromMkv = VideoCompressor.DefaultOutputPath("/videos/clip.mkv", null);
        Assert.EndsWith("clip.mp4", fromMkv.Replace('\\', '/'));

        // Input already .mp4 in place → must not collide with the source.
        var fromMp4 = VideoCompressor.DefaultOutputPath("/videos/clip.mp4", null);
        Assert.EndsWith("clip-boltzip.mp4", fromMp4.Replace('\\', '/'));
    }

    [Theory]
    [InlineData("movie.mp4", true)]
    [InlineData("clip.MKV", true)]
    [InlineData("photo.jpg", false)]
    [InlineData("notes.txt", false)]
    public void VideoFormats_IsVideo(string name, bool expected)
    {
        Assert.Equal(expected, VideoFormats.IsVideo(name));
    }

    [Fact]
    public void FfmpegLocator_InstallHint_IsHelpful()
    {
        Assert.False(string.IsNullOrWhiteSpace(FfmpegLocator.InstallHint()));
    }
}
