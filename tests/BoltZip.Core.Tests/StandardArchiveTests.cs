using BoltZip.Core.Compression;
using Xunit;

namespace BoltZip.Core.Tests;

public class StandardArchiveTests
{
    [Fact]
    public void Zip_RoundTrip()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("src/a.txt", "alpha");
        ws.WriteFile("src/nested/b.txt", "beta");

        var archive = ws.Path("out.zip");
        StandardArchive.Create(ArchiveFormat.Zip, false, new[] { ws.Path("src") }, archive, TestPlans.For(ArchiveFormat.Zip), null, default);

        var outDir = ws.CreateDir("out");
        StandardArchive.Extract(ArchiveFormat.Zip, false, archive, outDir, null, true, null, default);

        Assert.Equal("alpha", File.ReadAllText(System.IO.Path.Combine(outDir, "src", "a.txt")));
        Assert.Equal("beta", File.ReadAllText(System.IO.Path.Combine(outDir, "src", "nested", "b.txt")));
    }

    [Fact]
    public void Zstd_SingleFile_RoundTrip()
    {
        using var ws = new TempWorkspace();
        var file = ws.WriteFile("payload.txt", new string('q', 10_000));

        var archive = ws.Path("payload.txt.zst");
        StandardArchive.Create(ArchiveFormat.Zstd, false, new[] { file }, archive, TestPlans.For(ArchiveFormat.Zstd), null, default);

        var outDir = ws.CreateDir("out");
        StandardArchive.Extract(ArchiveFormat.Zstd, false, archive, outDir, null, true, null, default);

        Assert.Equal(10_000, new FileInfo(System.IO.Path.Combine(outDir, "payload.txt")).Length);
    }

    [Fact]
    public void Gzip_SingleFile_RoundTrip()
    {
        using var ws = new TempWorkspace();
        var file = ws.WriteFile("log.txt", "line1\nline2\n");

        var archive = ws.Path("log.txt.gz");
        StandardArchive.Create(ArchiveFormat.Gzip, false, new[] { file }, archive, TestPlans.For(ArchiveFormat.Gzip), null, default);

        var outDir = ws.CreateDir("out");
        StandardArchive.Extract(ArchiveFormat.Gzip, false, archive, outDir, null, true, null, default);

        Assert.Equal("line1\nline2\n", File.ReadAllText(System.IO.Path.Combine(outDir, "log.txt")));
    }

    [Fact]
    public void Tar_RoundTrip()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("src/one.txt", "one");
        ws.WriteFile("src/two.txt", "two");

        var archive = ws.Path("out.tar");
        StandardArchive.Create(ArchiveFormat.Tar, false, new[] { ws.Path("src") }, archive, TestPlans.For(ArchiveFormat.Tar), null, default);

        var outDir = ws.CreateDir("out");
        StandardArchive.Extract(ArchiveFormat.Tar, false, archive, outDir, null, true, null, default);

        Assert.Equal("one", File.ReadAllText(System.IO.Path.Combine(outDir, "src", "one.txt")));
        Assert.Equal("two", File.ReadAllText(System.IO.Path.Combine(outDir, "src", "two.txt")));
    }
}
