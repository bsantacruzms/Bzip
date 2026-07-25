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

    [Fact]
    public async Task RoundTrip_LargeInput_ParallelPath_PlainAndEncrypted()
    {
        using var ws = new TempWorkspace();

        // > 4 MiB total so the multi-core block path runs, with mixed content:
        // compressible text, incompressible random, an empty file, and a tiny trailing file.
        var text = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", 200_000));
        ws.WriteFile("big/text.log", text);

        var rand = new byte[4 * 1024 * 1024];
        new Random(12345).NextBytes(rand);
        ws.WriteFile("big/rand.bin", rand);

        ws.WriteFile("big/empty.dat", Array.Empty<byte>());
        ws.WriteFile("big/tiny.txt", "tail");

        var srcDir = ws.Path("big");
        var expected = HashDir(srcDir);

        foreach (var password in new string?[] { null, Password })
        {
            var archive = ws.Path((password is null ? "plain" : "enc") + ".bz");
            await BzArchive.CreateAsync(archive, new[] { srcDir }, password, TestPlans.For(ArchiveFormat.Bz));

            var outDir = ws.CreateDir(password is null ? "outp" : "oute");
            await BzArchive.ExtractAsync(archive, outDir, password, overwrite: true);

            var actual = HashDir(System.IO.Path.Combine(outDir, "big"));
            Assert.Equal(expected, actual);
        }
    }

    private static Dictionary<string, string> HashDir(string dir)
    {
        var map = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = System.IO.Path.GetRelativePath(dir, file).Replace('\\', '/');
            using var fs = File.OpenRead(file);
            map[rel] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs));
        }

        return map;
    }
}
