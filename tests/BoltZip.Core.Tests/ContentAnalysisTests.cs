using BoltZip.Core.Compression;
using Xunit;

namespace BoltZip.Core.Tests;

public class ContentAnalysisTests
{
    [Theory]
    [InlineData("clip.mp4", true)]
    [InlineData("MOVIE.MKV", true)]
    [InlineData("photo.JPG", true)]
    [InlineData("song.mp3", true)]
    [InlineData("bundle.zip", true)]
    [InlineData("archive.bz", true)]
    [InlineData("notes.txt", false)]
    [InlineData("data.csv", false)]
    [InlineData("audio.wav", false)]   // uncompressed PCM genuinely compresses
    [InlineData("image.bmp", false)]   // uncompressed bitmap genuinely compresses
    public void IsAlreadyCompressed_ClassifiesByExtension(string name, bool expected)
    {
        Assert.Equal(expected, ContentAnalysis.IsAlreadyCompressed(name));
    }

    [Fact]
    public void Analyze_MediaDominated_ReportsHighRatio()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("movie.mp4", new byte[1_000_000]);
        ws.WriteFile("readme.txt", new byte[10_000]);

        var content = ContentAnalysis.Analyze(new[] { ws.Root });

        Assert.Equal(1_010_000, content.TotalBytes);
        Assert.Equal(1_000_000, content.IncompressibleBytes);
        Assert.True(content.IncompressibleRatio > 0.98);
        Assert.True(content.IsIncompressibleDominated);
    }

    [Fact]
    public void Analyze_TextDominated_IsNotIncompressibleDominated()
    {
        using var ws = new TempWorkspace();
        ws.WriteFile("logo.png", new byte[50_000]);
        ws.WriteFile("app.log", new byte[1_000_000]);

        var content = ContentAnalysis.Analyze(new[] { ws.Root });

        Assert.False(content.IsIncompressibleDominated);
        Assert.True(content.IncompressibleRatio < 0.1);
    }

    [Fact]
    public void Analyze_EmptyInput_HasZeroRatio()
    {
        using var ws = new TempWorkspace();
        var content = ContentAnalysis.Analyze(new[] { ws.Root });

        Assert.Equal(0, content.TotalBytes);
        Assert.Equal(0d, content.IncompressibleRatio);
        Assert.False(content.IsIncompressibleDominated);
    }
}
