using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using ZstdSharp;
using IoCompressionMode = System.IO.Compression.CompressionMode;
using ScBZip2Stream = SharpCompress.Compressors.BZip2.BZip2Stream;
using ScCompressionMode = SharpCompress.Compressors.CompressionMode;

namespace BoltZip.Core.Compression;

/// <summary>
/// Create/extract/list for all non-native formats. Uses SharpCompress for container
/// formats (zip, tar, and legacy extract of rar/7z/xz/lzip/arj) and dedicated codec
/// streams for the single-stream families (gzip, bzip2, zstd, brotli), tarring when needed.
/// </summary>
public static class StandardArchive
{
    public static void Create(
        ArchiveFormat format,
        bool tarWrapped,
        IReadOnlyList<string> inputs,
        string outputPath,
        CompressionPlan plan,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        switch (format)
        {
            case ArchiveFormat.Zip:
                CreateZip(inputs, outputPath, plan, progress, cancellationToken);
                break;
            case ArchiveFormat.Tar:
                CreateContainer(ArchiveType.Tar, new WriterOptions(CompressionType.None), inputs, outputPath, progress, cancellationToken);
                break;
            case ArchiveFormat.Gzip:
            case ArchiveFormat.Bzip2:
            case ArchiveFormat.Zstd:
            case ArchiveFormat.Brotli:
                CreateSingleStream(format, tarWrapped, inputs, outputPath, plan, progress, cancellationToken);
                break;
            default:
                throw new NotSupportedException($"Creating {format} archives is not supported.");
        }
    }

    public static void Extract(
        ArchiveFormat format,
        bool tarWrapped,
        string archivePath,
        string outputDirectory,
        string? password,
        bool overwrite,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputRoot = Path.GetFullPath(outputDirectory);

        switch (format)
        {
            case ArchiveFormat.Zstd:
            case ArchiveFormat.Brotli:
            case ArchiveFormat.Gzip:
            case ArchiveFormat.Bzip2:
                ExtractSingleStream(format, tarWrapped, archivePath, outputRoot, overwrite, progress, cancellationToken);
                break;
            case ArchiveFormat.SevenZip:
                ExtractRandomAccess(archivePath, outputRoot, password, overwrite, progress, cancellationToken);
                break;
            default:
                ExtractStreaming(archivePath, outputRoot, password, overwrite, progress, cancellationToken);
                break;
        }
    }

    public static IReadOnlyList<ArchiveEntryInfo> List(string archivePath, string? password)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions { Password = password });
            return archive.Entries.Select(ToEntryInfo).ToList();
        }
        catch
        {
            using var stream = File.OpenRead(archivePath);
            using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions { Password = password });
            var entries = new List<ArchiveEntryInfo>();
            while (reader.MoveToNextEntry())
            {
                entries.Add(ToEntryInfo(reader.Entry));
            }

            return entries;
        }
    }

    private static void CreateZip(
        IReadOnlyList<string> inputs,
        string outputPath,
        CompressionPlan plan,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = InputScanner.EnumerateFiles(inputs);
        var total = files.Count;
        var level = ToCompressionLevel(plan.Level, 9);
        var buffer = new byte[Math.Max(64 * 1024, plan.BufferBytes)];

        using var outStream = File.Create(outputPath);
        using var archive = new ZipArchive(outStream, ZipArchiveMode.Create);

        var done = 0;
        foreach (var (key, path) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(key.Replace('\\', '/'), level);
            try
            {
                entry.LastWriteTime = File.GetLastWriteTime(path);
            }
            catch
            {
                // timestamps outside the zip epoch are ignored
            }

            using (var entryStream = entry.Open())
            using (var source = File.OpenRead(path))
            {
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    entryStream.Write(buffer, 0, read);
                }
            }

            done++;
            progress?.Report(new ArchiveProgress(ArchivePhase.Compressing, key, done, total, done, total));
        }

        progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, total, total, total, total));
    }

    private static void CreateContainer(
        ArchiveType type,
        WriterOptions options,
        IReadOnlyList<string> inputs,
        string outputPath,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = InputScanner.EnumerateFiles(inputs);
        var total = files.Count;

        using var outStream = File.Create(outputPath);
        using var writer = WriterFactory.OpenWriter(outStream, type, options);

        var done = 0;
        foreach (var (key, path) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteFileToWriter(writer, key, path);
            done++;
            progress?.Report(new ArchiveProgress(ArchivePhase.Compressing, key, done, total, done, total));
        }

        progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, total, total, total, total));
    }

    private static void CreateSingleStream(
        ArchiveFormat format,
        bool tarWrapped,
        IReadOnlyList<string> inputs,
        string outputPath,
        CompressionPlan plan,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = InputScanner.EnumerateFiles(inputs);
        var multi = tarWrapped || files.Count > 1;
        var total = files.Count;

        using var outStream = File.Create(outputPath);
        using var codec = OpenCompressor(format, outStream, plan);

        if (multi)
        {
            using var writer = WriterFactory.OpenWriter(codec, ArchiveType.Tar, new WriterOptions(CompressionType.None) { LeaveStreamOpen = true });
            var done = 0;
            foreach (var (key, path) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteFileToWriter(writer, key, path);
                done++;
                progress?.Report(new ArchiveProgress(ArchivePhase.Compressing, key, done, total, done, total));
            }
        }
        else
        {
            using var source = File.OpenRead(files[0].FullPath);
            CopyWithProgress(source, codec, plan.BufferBytes, files[0].Key, progress, cancellationToken);
        }

        progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, total, total, total, total));
    }

    private static void ExtractStreaming(
        string archivePath,
        string outputRoot,
        string? password,
        bool overwrite,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions { Password = password });

        var done = 0;
        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            using (var entryStream = reader.OpenEntryStream())
            {
                WriteEntry(entryStream, outputRoot, reader.Entry.Key, overwrite);
            }

            done++;
            progress?.Report(new ArchiveProgress(ArchivePhase.Extracting, reader.Entry.Key, done, 0, done, 0));
        }

        progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, 0, 0, done, done));
    }

    private static void ExtractRandomAccess(
        string archivePath,
        string outputRoot,
        string? password,
        bool overwrite,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions { Password = password });

        var total = archive.Entries.Count(e => !e.IsDirectory);
        var done = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                continue;
            }

            using (var entryStream = entry.OpenEntryStream())
            {
                WriteEntry(entryStream, outputRoot, entry.Key, overwrite);
            }

            done++;
            progress?.Report(new ArchiveProgress(ArchivePhase.Extracting, entry.Key, done, total, done, total));
        }

        progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, total, total, total, total));
    }

    private static void ExtractSingleStream(
        ArchiveFormat format,
        bool tarWrapped,
        string archivePath,
        string outputRoot,
        bool overwrite,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var fileStream = File.OpenRead(archivePath);
        using var codec = OpenDecompressor(format, fileStream);

        if (tarWrapped)
        {
            using var reader = ReaderFactory.OpenReader(codec, new ReaderOptions { LeaveStreamOpen = true });
            var done = 0;
            while (reader.MoveToNextEntry())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Entry.IsDirectory)
                {
                    continue;
                }

                using (var entryStream = reader.OpenEntryStream())
                {
                    WriteEntry(entryStream, outputRoot, reader.Entry.Key, overwrite);
                }

                done++;
                progress?.Report(new ArchiveProgress(ArchivePhase.Extracting, reader.Entry.Key, done, 0, done, 0));
            }

            progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, 0, 0, done, done));
        }
        else
        {
            var safeOutput = ResolveSafePath(outputRoot, StripCodecExtension(Path.GetFileName(archivePath)));
            if (!overwrite && File.Exists(safeOutput))
            {
                throw new IOException($"File already exists: {safeOutput}. Enable overwrite to replace it.");
            }

            using var outFile = File.Create(safeOutput);
            CopyWithProgress(codec, outFile, 1 << 20, Path.GetFileName(safeOutput), progress, cancellationToken);
            progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, 0, 0, 1, 1));
        }
    }

    private static void WriteFileToWriter(IWriter writer, string key, string path)
    {
        using var source = File.OpenRead(path);
        writer.Write(key, source, File.GetLastWriteTime(path));
    }

    private static void WriteEntry(Stream entryStream, string outputRoot, string? key, bool overwrite)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        var destination = ResolveSafePath(outputRoot, key);
        var parent = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(parent))
        {
            Directory.CreateDirectory(parent);
        }

        if (!overwrite && File.Exists(destination))
        {
            throw new IOException($"File already exists: {destination}. Enable overwrite to replace it.");
        }

        using var outFile = File.Create(destination);
        entryStream.CopyTo(outFile);
    }

    private static Stream OpenCompressor(ArchiveFormat format, Stream output, CompressionPlan plan) => format switch
    {
        ArchiveFormat.Gzip => new GZipStream(output, ToCompressionLevel(plan.Level, 9), leaveOpen: true),
        ArchiveFormat.Brotli => new BrotliStream(output, ToCompressionLevel(plan.Level, 11), leaveOpen: true),
        ArchiveFormat.Bzip2 => ScBZip2Stream.Create(output, ScCompressionMode.Compress, false, leaveOpen: true),
        ArchiveFormat.Zstd => OpenZstdCompressor(output, plan),
        _ => throw new NotSupportedException($"Creating {format} archives is not supported."),
    };

    private static Stream OpenZstdCompressor(Stream output, CompressionPlan plan)
    {
        var compressor = new CompressionStream(output, plan.Level, bufferSize: plan.BufferBytes, leaveOpen: true);
        ZstdTuning.ApplyCompression(compressor, plan);
        return compressor;
    }

    private static Stream OpenDecompressor(ArchiveFormat format, Stream input) => format switch
    {
        ArchiveFormat.Gzip => new GZipStream(input, IoCompressionMode.Decompress, leaveOpen: true),
        ArchiveFormat.Bzip2 => ScBZip2Stream.Create(input, ScCompressionMode.Decompress, true, leaveOpen: true),
        ArchiveFormat.Brotli => new BrotliStream(input, IoCompressionMode.Decompress, leaveOpen: true),
        ArchiveFormat.Zstd => OpenZstdDecompressor(input),
        _ => throw new NotSupportedException($"Extracting {format} archives is not supported here."),
    };

    private static Stream OpenZstdDecompressor(Stream input)
    {
        var decompressor = new DecompressionStream(input);
        ZstdTuning.AllowLargeWindow(decompressor);
        return decompressor;
    }

    private static CompressionLevel ToCompressionLevel(int level, int max)
    {
        if (level <= Math.Max(1, max / 6))
        {
            return CompressionLevel.Fastest;
        }

        return level >= max ? CompressionLevel.SmallestSize : CompressionLevel.Optimal;
    }

    private static string StripCodecExtension(string fileName)
    {
        foreach (var extension in new[] { ".zst", ".br", ".gz", ".bz2", ".xz", ".lz" })
        {
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return fileName[..^extension.Length];
            }
        }

        return fileName + ".out";
    }

    private static string ResolveSafePath(string outputRoot, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(outputRoot, normalized));

        var rootWithSeparator = outputRoot.EndsWith(Path.DirectorySeparatorChar)
            ? outputRoot
            : outputRoot + Path.DirectorySeparatorChar;

        if (!string.Equals(combined, outputRoot, StringComparison.OrdinalIgnoreCase) &&
            !combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Entry path escapes the output directory: {relativePath}");
        }

        return combined;
    }

    private static void CopyWithProgress(
        Stream source,
        Stream destination,
        int bufferSize,
        string? entryName,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[Math.Max(64 * 1024, bufferSize)];
        long processed = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Write(buffer, 0, read);
            processed += read;
            progress?.Report(new ArchiveProgress(ArchivePhase.Compressing, entryName, processed, 0, 0, 1));
        }
    }

    private static ArchiveEntryInfo ToEntryInfo(IEntry entry) => new(
        entry.Key ?? string.Empty,
        entry.Size,
        entry.CompressedSize,
        entry.IsDirectory,
        entry.IsEncrypted,
        entry.LastModifiedTime);
}
