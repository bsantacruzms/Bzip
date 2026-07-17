using BoltZip.Core.Bz;
using BoltZip.Core.Compression;
using Xunit;

namespace BoltZip.Core.Tests;

public class BzArchiveTests
{
    private const string Password = "correct horse battery staple";

    [Fact]
    public async Task RoundTrip_NoPassword_RestoresFilesAndDirectories()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("src/a.txt", "hello world");
        ws.WriteFile("src/sub/b.txt", new string('x', 5000));
        var srcDir = ws.Path("src");

        var archive = ws.Path("out.bz");
        await BzArchive.CreateAsync(archive, new[] { srcDir }, password: null, TestPlans.For(ArchiveFormat.Bz));

        var outDir = ws.CreateDir("out");
        await BzArchive.ExtractAsync(archive, outDir, password: null, overwrite: true);

        Assert.Equal("hello world", File.ReadAllText(System.IO.Path.Combine(outDir, "src", "a.txt")));
        Assert.Equal(5000, new FileInfo(System.IO.Path.Combine(outDir, "src", "sub", "b.txt")).Length);
    }

    [Fact]
    public async Task RoundTrip_WithPassword_RestoresContent()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("data/secret.txt", "top secret payload");
        var srcDir = ws.Path("data");

        var archive = ws.Path("secure.bz");
        await BzArchive.CreateAsync(archive, new[] { srcDir }, Password, TestPlans.For(ArchiveFormat.Bz));

        var outDir = ws.CreateDir("out");
        await BzArchive.ExtractAsync(archive, outDir, Password, overwrite: true);

        Assert.Equal("top secret payload", File.ReadAllText(System.IO.Path.Combine(outDir, "data", "secret.txt")));
    }

    [Fact]
    public async Task WrongPassword_Throws()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("data/secret.txt", "top secret payload");
        var archive = ws.Path("secure.bz");
        await BzArchive.CreateAsync(archive, new[] { ws.Path("data") }, Password, TestPlans.For(ArchiveFormat.Bz));

        var outDir = ws.CreateDir("out");
        await Assert.ThrowsAnyAsync<Exception>(
            () => BzArchive.ExtractAsync(archive, outDir, "wrong password", overwrite: true));
    }

    [Fact]
    public async Task EncryptedList_WithoutPassword_Throws()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("data/secret.txt", "x");
        var archive = ws.Path("secure.bz");
        await BzArchive.CreateAsync(archive, new[] { ws.Path("data") }, Password, TestPlans.For(ArchiveFormat.Bz));

        Assert.ThrowsAny<Exception>(() => BzArchive.List(archive, password: null));
    }

    [Fact]
    public async Task List_ReturnsEntries()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("data/a.txt", "aaa");
        ws.WriteFile("data/b.txt", "bbb");
        var archive = ws.Path("plain.bz");
        await BzArchive.CreateAsync(archive, new[] { ws.Path("data") }, password: null, TestPlans.For(ArchiveFormat.Bz));

        var entries = BzArchive.List(archive);

        Assert.Contains(entries, e => e.Path == "data/a.txt");
        Assert.Contains(entries, e => e.Path == "data/b.txt");
    }

    [Fact]
    public async Task Tampering_WithEncryptedContent_IsDetected()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("data/secret.txt", new string('z', 2000));
        var archive = ws.Path("secure.bz");
        await BzArchive.CreateAsync(archive, new[] { ws.Path("data") }, Password, TestPlans.For(ArchiveFormat.Bz));

        var bytes = await File.ReadAllBytesAsync(archive);
        bytes[bytes.Length / 2] ^= 0xFF; // flip a byte inside the encrypted content
        await File.WriteAllBytesAsync(archive, bytes);

        var outDir = ws.CreateDir("out");
        await Assert.ThrowsAnyAsync<Exception>(
            () => BzArchive.ExtractAsync(archive, outDir, Password, overwrite: true));
    }

    [Fact]
    public async Task SingleFile_RoundTrips()
    {
        using var ws = new TempWorkspace();
        var file = ws.WriteFile("note.txt", "just one file");
        var archive = ws.Path("single.bz");
        await BzArchive.CreateAsync(archive, new[] { file }, password: null, TestPlans.For(ArchiveFormat.Bz));

        var outDir = ws.CreateDir("out");
        await BzArchive.ExtractAsync(archive, outDir, overwrite: true);

        Assert.Equal("just one file", File.ReadAllText(System.IO.Path.Combine(outDir, "note.txt")));
    }
}
