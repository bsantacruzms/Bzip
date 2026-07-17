using BoltZip.Core.Compression;
using BoltZip.Core.Hardware;
using Xunit;

namespace BoltZip.Core.Tests;

public class ArchiveServiceTests
{
    private static CreateRequest Request(string output, IReadOnlyList<string> inputs, string? password = null, OptimizationGoal goal = OptimizationGoal.Balanced)
        => new()
        {
            OutputPath = output,
            Inputs = inputs,
            Password = password,
            Goal = goal,
            Hardware = HardwareProbe.DetectFast(),
        };

    [Fact]
    public async Task Create_Bz_WithPassword_Then_Extract()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("src/a.txt", "service payload");
        var service = new ArchiveService();

        var result = await service.CreateAsync(Request(ws.Path("out.bz"), new[] { ws.Path("src") }, password: "pw12345"));

        var outDir = ws.CreateDir("out");
        await service.ExtractAsync(new ExtractRequest
        {
            ArchivePath = result.OutputPath,
            OutputDirectory = outDir,
            Password = "pw12345",
            Overwrite = true,
        });

        Assert.Equal("service payload", File.ReadAllText(System.IO.Path.Combine(outDir, "src", "a.txt")));
    }

    [Fact]
    public async Task Create_Zip_List_Extract()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("src/a.txt", "zip me");
        var service = new ArchiveService();

        var result = await service.CreateAsync(Request(ws.Path("out.zip"), new[] { ws.Path("src") }));
        var entries = await service.ListAsync(result.OutputPath);
        Assert.Contains(entries, e => e.Path.EndsWith("a.txt"));

        var outDir = ws.CreateDir("out");
        await service.ExtractAsync(new ExtractRequest { ArchivePath = result.OutputPath, OutputDirectory = outDir, Overwrite = true });
        Assert.Equal("zip me", File.ReadAllText(System.IO.Path.Combine(outDir, "src", "a.txt")));
    }

    [Fact]
    public async Task Create_Zstd_MultipleFiles_AutoTars()
    {
        using var ws = new TempWorkspace();
        var f1 = ws.WriteFile("a.txt", "one");
        var f2 = ws.WriteFile("b.txt", "two");
        var service = new ArchiveService();

        var result = await service.CreateAsync(Request(ws.Path("bundle.zst"), new[] { f1, f2 }));

        // Multiple inputs to a single-stream codec should auto-insert .tar.
        Assert.EndsWith(".tar.zst", result.OutputPath);

        var outDir = ws.CreateDir("out");
        await service.ExtractAsync(new ExtractRequest { ArchivePath = result.OutputPath, OutputDirectory = outDir, Overwrite = true });
        Assert.Equal("one", File.ReadAllText(System.IO.Path.Combine(outDir, "a.txt")));
        Assert.Equal("two", File.ReadAllText(System.IO.Path.Combine(outDir, "b.txt")));
    }

    [Fact]
    public async Task Plan_ReflectsGoal()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("src/a.txt", "content");
        var service = new ArchiveService();

        var fast = await service.PlanAsync(Request(ws.Path("out.bz"), new[] { ws.Path("src") }, goal: OptimizationGoal.MaxSpeed));
        var small = await service.PlanAsync(Request(ws.Path("out.bz"), new[] { ws.Path("src") }, goal: OptimizationGoal.MaxRatio));

        Assert.True(small.Level > fast.Level);
    }

    [Fact]
    public async Task Create_EncryptedZip_IsRejected()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("src/a.txt", "content");
        var service = new ArchiveService();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.CreateAsync(Request(ws.Path("out.zip"), new[] { ws.Path("src") }, password: "nope")));
    }
}
