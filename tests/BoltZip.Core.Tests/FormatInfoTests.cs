using BoltZip.Core.Compression;
using Xunit;

namespace BoltZip.Core.Tests;

public class FormatInfoTests
{
    [Theory]
    [InlineData("archive.bz", ArchiveFormat.Bz, false)]
    [InlineData("data.zip", ArchiveFormat.Zip, false)]
    [InlineData("data.7z", ArchiveFormat.SevenZip, false)]
    [InlineData("data.tar", ArchiveFormat.Tar, false)]
    [InlineData("data.zst", ArchiveFormat.Zstd, false)]
    [InlineData("data.tar.zst", ArchiveFormat.Zstd, true)]
    [InlineData("data.tzst", ArchiveFormat.Zstd, true)]
    [InlineData("data.tar.gz", ArchiveFormat.Gzip, true)]
    [InlineData("data.tgz", ArchiveFormat.Gzip, true)]
    [InlineData("data.tar.bz2", ArchiveFormat.Bzip2, true)]
    [InlineData("data.br", ArchiveFormat.Brotli, false)]
    [InlineData("data.rar", ArchiveFormat.Rar, false)]
    [InlineData("mystery.unknown", ArchiveFormat.Unknown, false)]
    public void DetectFromPath_MapsExtensions(string name, ArchiveFormat expected, bool tar)
    {
        var descriptor = FormatInfo.DetectFromPath(name);
        Assert.Equal(expected, descriptor.Format);
        Assert.Equal(tar, descriptor.TarWrapped);
    }

    [Fact]
    public void SniffFromMagic_DetectsZipGzipZstdAndBz()
    {
        Assert.Equal(ArchiveFormat.Zip, FormatInfo.SniffFromMagic("PK\x03\x04"u8));
        Assert.Equal(ArchiveFormat.Gzip, FormatInfo.SniffFromMagic(new byte[] { 0x1F, 0x8B, 0x08 }));
        Assert.Equal(ArchiveFormat.Zstd, FormatInfo.SniffFromMagic(new byte[] { 0x28, 0xB5, 0x2F, 0xFD }));
        Assert.Equal(ArchiveFormat.Bz, FormatInfo.SniffFromMagic("BZ1"u8));
    }

    [Fact]
    public void Capabilities_NativeAndStandardCanCreate()
    {
        Assert.True(FormatInfo.CanCreate(ArchiveFormat.Bz));
        Assert.True(FormatInfo.CanCreate(ArchiveFormat.Zip));
        Assert.True(FormatInfo.CanCreate(ArchiveFormat.Zstd));
        Assert.False(FormatInfo.CanCreate(ArchiveFormat.Rar));
        Assert.True(FormatInfo.CanExtract(ArchiveFormat.Rar));
    }
}
