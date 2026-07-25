using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using BoltZip.Core.Compression;
using BoltZip.Core.Infrastructure;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace BoltZip.Core.Bz;

/// <summary>
/// Reader/writer for BoltZip's native <c>.bz</c> format: a solid Zstandard stream with a
/// separate compressed index, optionally protected end-to-end with XChaCha20-Poly1305
/// (Argon2id-derived keys). See <see cref="BzFormat"/> for the on-disk layout.
/// </summary>
public static class BzArchive
{
    private const int IndexCompressionLevel = 19;

    private sealed record Entry(string Path, bool IsDirectory, long Size, long ModifiedUnix);

    private sealed record ScanItem(Entry Entry, string? FullPath);

    private sealed record HeaderInfo(
        List<Entry> Entries,
        bool Encrypted,
        byte[]? ContentKey,
        byte[]? NoncePrefix,
        int ChunkSize);

    public static async Task CreateAsync(
        string outputPath,
        IReadOnlyList<string> inputs,
        string? password,
        CompressionPlan plan,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new ArchiveProgress(ArchivePhase.Scanning, null, 0, 0, 0, 0));

        var items = Scan(inputs);
        var totalBytes = items.Where(i => !i.Entry.IsDirectory).Sum(i => i.Entry.Size);
        var entriesTotal = items.Count;

        var encrypted = !string.IsNullOrEmpty(password);
        byte[]? salt = null, noncePrefix = null, contentKey = null, indexKey = null;
        var chunkSize = BzFormat.DefaultChunkSize;

        if (encrypted)
        {
            salt = RandomNumberGenerator.GetBytes(BzFormat.SaltBytes);
            noncePrefix = RandomNumberGenerator.GetBytes(BzFormat.NoncePrefixBytes);
            var master = BzCrypto.DeriveMasterKey(password!, salt, BzFormat.ArgonOpsLimit, BzFormat.ArgonMemLimit);
            try
            {
                contentKey = BzCrypto.DeriveSubKey(master, BzFormat.ContentKeyInfo);
                indexKey = BzCrypto.DeriveSubKey(master, BzFormat.IndexKeyInfo);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(master);
            }
        }

        var indexBlob = BuildIndexBlob(items);
        var indexZstd = ZstdCompress(indexBlob, IndexCompressionLevel);
        var indexStored = encrypted
            ? EncryptBytes(indexZstd, indexKey!, noncePrefix!, chunkSize)
            : indexZstd;

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var fs = new FileStream(
            outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            Math.Max(64 * 1024, plan.BufferBytes));

        fs.Write(BzFormat.Magic);
        fs.WriteByte(BzFormat.Version);
        fs.WriteByte((byte)(encrypted ? BzFormat.HeaderFlags.Encrypted : BzFormat.HeaderFlags.None));
        fs.WriteByte(BzFormat.CodecZstd);
        fs.WriteByte(0);
        fs.WriteByte(0);

        if (encrypted)
        {
            fs.Write(salt!);
            fs.Write(noncePrefix!);
            WriteInt64(fs, BzFormat.ArgonOpsLimit);
            WriteInt32(fs, BzFormat.ArgonMemLimit);
            WriteInt32(fs, chunkSize);
        }

        WriteInt32(fs, indexBlob.Length);
        WriteInt32(fs, indexStored.Length);
        fs.Write(indexStored);

        var contentLenPos = fs.Position;
        WriteInt64(fs, 0);
        var contentStart = fs.Position;

        Stream sink = fs;
        ChunkedAeadWriteStream? aead = null;
        if (encrypted)
        {
            aead = new ChunkedAeadWriteStream(fs, contentKey!, noncePrefix!, chunkSize);
            sink = aead;
        }

        progress?.Report(new ArchiveProgress(ArchivePhase.Compressing, null, 0, totalBytes, 0, entriesTotal));

        long processed = 0;

        var fileRefs = items
            .Where(i => !i.Entry.IsDirectory)
            .Select(i => new ParallelBlockCompressor.FileRef(i.FullPath!, i.Entry.Path, i.Entry.Size))
            .ToList();

        var workers = Math.Max(1, plan.WorkerThreads);
        var blockSize = ChooseBlockSize(totalBytes, workers);

        if (workers > 1 && totalBytes > (long)blockSize * 2)
        {
            // Multi-core path: compress independent Zstandard frames across all workers.
            // Concatenated frames decompress as one stream, so the format is unchanged.
            processed = ParallelBlockCompressor.Compress(
                sink, fileRefs, plan, workers, blockSize,
                (done, entry) =>
                {
                    var entriesApprox = totalBytes > 0
                        ? (int)Math.Min(entriesTotal, done * entriesTotal / totalBytes)
                        : entriesTotal;
                    progress?.Report(new ArchiveProgress(
                        ArchivePhase.Compressing, entry, done, totalBytes, entriesApprox, entriesTotal));
                },
                cancellationToken);
        }
        else
        {
            // Single-frame streaming path (small inputs / single worker) keeps the best ratio.
            var buffer = new byte[Math.Max(64 * 1024, plan.BufferBytes)];
            var compressor = new CompressionStream(sink, plan.Level, bufferSize: plan.BufferBytes);
            try
            {
                ApplyCompressionParameters(compressor, plan);

                var entriesDone = 0;
                foreach (var item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (item.Entry.IsDirectory)
                    {
                        entriesDone++;
                        continue;
                    }

                    processed = await PumpFileAsync(
                        item.FullPath!, compressor, buffer, processed, totalBytes,
                        entriesDone, entriesTotal, item.Entry.Path, progress, cancellationToken);
                    entriesDone++;
                }
            }
            finally
            {
                compressor.Dispose();
            }
        }

        aead?.CompleteFinal();

        var contentEnd = fs.Position;
        fs.Position = contentLenPos;
        WriteInt64(fs, contentEnd - contentStart);
        fs.Position = contentEnd;

        progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, totalBytes, totalBytes, entriesTotal, entriesTotal));
    }

    public static IReadOnlyList<ArchiveEntryInfo> List(string archivePath, string? password = null)
    {
        using var fs = File.OpenRead(archivePath);
        var header = ReadHeader(fs, password);
        return header.Entries
            .Select(e => new ArchiveEntryInfo(
                e.Path, e.Size, 0, e.IsDirectory, header.Encrypted,
                DateTimeOffset.FromUnixTimeSeconds(e.ModifiedUnix).UtcDateTime))
            .ToList();
    }

    public static async Task ExtractAsync(
        string archivePath,
        string outputDirectory,
        string? password = null,
        bool overwrite = false,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var outputRoot = Path.GetFullPath(outputDirectory);

        await using var fs = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);

        var header = ReadHeader(fs, password);
        var contentStoredLen = ReadInt64(fs);

        var totalBytes = header.Entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
        var entriesTotal = header.Entries.Count;

        Stream reader = header.Encrypted
            ? new ChunkedAeadReadStream(fs, header.ContentKey!, header.NoncePrefix!, contentStoredLen)
            : new LimitedReadStream(fs, contentStoredLen);

        using var decompressor = new DecompressionStream(reader);
        TrySetDecompressionWindow(decompressor);

        var buffer = new byte[1 << 20];
        long processed = 0;
        var entriesDone = 0;

        foreach (var entry in header.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(ResolveSafePath(outputRoot, entry.Path));
                entriesDone++;
                continue;
            }

            var destination = ResolveSafePath(outputRoot, entry.Path);
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            if (!overwrite && File.Exists(destination))
            {
                throw new IOException($"File already exists: {destination}. Enable overwrite to replace it.");
            }

            await using (var outFile = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length))
            {
                processed = await CopyExactAsync(
                    decompressor, outFile, entry.Size, buffer, processed, totalBytes,
                    entriesDone, entriesTotal, entry.Path, progress, cancellationToken);
            }

            TrySetModified(destination, entry.ModifiedUnix);
            entriesDone++;
        }

        // Drain to force authentication of the trailing chunk on encrypted archives.
        await decompressor.CopyToAsync(Stream.Null, cancellationToken);

        progress?.Report(new ArchiveProgress(ArchivePhase.Done, null, totalBytes, totalBytes, entriesTotal, entriesTotal));
    }

    private static HeaderInfo ReadHeader(Stream fs, string? password)
    {
        Span<byte> magic = stackalloc byte[3];
        ReadExactly(fs, magic);
        if (!magic.SequenceEqual(BzFormat.Magic))
        {
            throw new InvalidDataException("Not a BoltZip (.bz) archive.");
        }

        var version = fs.ReadByte();
        if (version != BzFormat.Version)
        {
            throw new NotSupportedException($"Unsupported .bz version: {version}.");
        }

        var flags = (BzFormat.HeaderFlags)fs.ReadByte();
        _ = fs.ReadByte(); // codec (only zstd today)
        _ = fs.ReadByte(); // reserved
        _ = fs.ReadByte(); // reserved

        var encrypted = flags.HasFlag(BzFormat.HeaderFlags.Encrypted);
        byte[]? noncePrefix = null, contentKey = null, indexKey = null;
        var chunkSize = BzFormat.DefaultChunkSize;

        if (encrypted)
        {
            var salt = ReadBytes(fs, BzFormat.SaltBytes);
            noncePrefix = ReadBytes(fs, BzFormat.NoncePrefixBytes);
            var ops = ReadInt64(fs);
            var mem = ReadInt32(fs);
            chunkSize = ReadInt32(fs);

            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("This archive is encrypted; a password is required.");
            }

            var master = BzCrypto.DeriveMasterKey(password, salt, ops, mem);
            try
            {
                contentKey = BzCrypto.DeriveSubKey(master, BzFormat.ContentKeyInfo);
                indexKey = BzCrypto.DeriveSubKey(master, BzFormat.IndexKeyInfo);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(master);
            }
        }

        var indexPlainLen = ReadInt32(fs);
        var indexStoredLen = ReadInt32(fs);
        var indexStored = ReadBytes(fs, indexStoredLen);

        var indexZstd = encrypted
            ? DecryptBytes(indexStored, indexKey!, noncePrefix!)
            : indexStored;

        var indexBlob = ZstdDecompress(indexZstd, indexPlainLen);
        var entries = ParseIndex(indexBlob);

        return new HeaderInfo(entries, encrypted, contentKey, noncePrefix, chunkSize);
    }

    private static List<ScanItem> Scan(IReadOnlyList<string> inputs)
    {
        var items = new List<ScanItem>();

        foreach (var input in inputs)
        {
            var full = Path.GetFullPath(input);

            if (Directory.Exists(full))
            {
                var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var baseDir = Path.GetDirectoryName(trimmed) ?? trimmed;

                AddDirectory(items, trimmed, baseDir);
                foreach (var dir in Directory.EnumerateDirectories(trimmed, "*", SearchOption.AllDirectories))
                {
                    AddDirectory(items, dir, baseDir);
                }

                foreach (var file in Directory.EnumerateFiles(trimmed, "*", SearchOption.AllDirectories))
                {
                    AddFile(items, file, baseDir);
                }
            }
            else if (File.Exists(full))
            {
                var baseDir = Path.GetDirectoryName(full) ?? Directory.GetCurrentDirectory();
                AddFile(items, full, baseDir);
            }
            else
            {
                throw new FileNotFoundException($"Input not found: {input}");
            }
        }

        return items;
    }

    private static void AddDirectory(List<ScanItem> items, string dir, string baseDir)
    {
        var rel = Path.GetRelativePath(baseDir, dir).Replace('\\', '/');
        items.Add(new ScanItem(new Entry(rel, true, 0, ToUnix(Directory.GetLastWriteTimeUtc(dir))), null));
    }

    private static void AddFile(List<ScanItem> items, string file, string baseDir)
    {
        var info = new FileInfo(file);
        var rel = Path.GetRelativePath(baseDir, file).Replace('\\', '/');
        items.Add(new ScanItem(new Entry(rel, false, info.Length, ToUnix(info.LastWriteTimeUtc)), file));
    }

    private static byte[] BuildIndexBlob(List<ScanItem> items)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(items.Count);
            foreach (var item in items)
            {
                var pathBytes = Encoding.UTF8.GetBytes(item.Entry.Path);
                bw.Write(pathBytes.Length);
                bw.Write(pathBytes);
                bw.Write(item.Entry.IsDirectory);
                bw.Write(item.Entry.Size);
                bw.Write(item.Entry.ModifiedUnix);
            }
        }

        return ms.ToArray();
    }

    private static List<Entry> ParseIndex(byte[] blob)
    {
        using var ms = new MemoryStream(blob);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        var count = br.ReadInt32();
        var entries = new List<Entry>(Math.Max(0, count));
        for (var i = 0; i < count; i++)
        {
            var length = br.ReadInt32();
            var path = Encoding.UTF8.GetString(br.ReadBytes(length));
            var isDir = br.ReadBoolean();
            var size = br.ReadInt64();
            var mtime = br.ReadInt64();
            entries.Add(new Entry(path, isDir, size, mtime));
        }

        return entries;
    }

    private static async Task<long> PumpFileAsync(
        string path, Stream compressor, byte[] buffer, long processed, long total,
        int entriesDone, int entriesTotal, string entryName,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        await using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, useAsync: true);

        int read;
        while ((read = await fs.ReadAsync(buffer, cancellationToken)) > 0)
        {
            compressor.Write(buffer, 0, read);
            processed += read;
            progress?.Report(new ArchiveProgress(
                ArchivePhase.Compressing, entryName, processed, total, entriesDone, entriesTotal));
        }

        return processed;
    }

    private static async Task<long> CopyExactAsync(
        Stream source, Stream destination, long size, byte[] buffer, long processed, long total,
        int entriesDone, int entriesTotal, string entryName,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var remaining = size;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of BoltZip content.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
            processed += read;
            progress?.Report(new ArchiveProgress(
                ArchivePhase.Extracting, entryName, processed, total, entriesDone, entriesTotal));
        }

        return processed;
    }

    private static int ChooseBlockSize(long totalBytes, int workers)
    {
        const long MiB = 1024 * 1024;
        if (totalBytes <= 0)
        {
            return (int)(2 * MiB);
        }

        // Aim for ~2 blocks per worker so cores stay busy, while keeping blocks large
        // enough that per-block compression ratio stays close to a single solid stream.
        var target = totalBytes / (Math.Max(1, workers) * 2L);
        return (int)Math.Clamp(target, 2 * MiB, 8 * MiB);
    }

    private static void ApplyCompressionParameters(CompressionStream compressor, CompressionPlan plan)
    {
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_compressionLevel, plan.Level);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_windowLog, BitOperations.Log2((ulong)plan.WindowBytes));
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching, plan.LongDistanceMatching ? 1 : 0);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_nbWorkers, plan.WorkerThreads);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
    }

    private static void TrySet(CompressionStream compressor, ZSTD_cParameter parameter, int value)
    {
        try
        {
            compressor.SetParameter(parameter, value);
        }
        catch
        {
            // Parameter unsupported by this codec build (e.g. no multithreading); ignore.
        }
    }

    private static void TrySetDecompressionWindow(DecompressionStream decompressor)
    {
        try
        {
            decompressor.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 31);
        }
        catch
        {
            // ignore
        }
    }

    private static byte[] ZstdCompress(byte[] data, int level)
    {
        using var ms = new MemoryStream();
        using (var cs = new CompressionStream(ms, level))
        {
            cs.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    private static byte[] ZstdDecompress(byte[] data, int expectedLength)
    {
        using var input = new MemoryStream(data);
        using var ds = new DecompressionStream(input);
        TrySetDecompressionWindow(ds);
        using var output = new MemoryStream(Math.Max(0, expectedLength));
        ds.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] EncryptBytes(byte[] data, byte[] key, byte[] noncePrefix, int chunkSize)
    {
        using var ms = new MemoryStream();
        using (var writer = new ChunkedAeadWriteStream(ms, key, noncePrefix, chunkSize))
        {
            writer.Write(data, 0, data.Length);
            writer.CompleteFinal();
        }

        return ms.ToArray();
    }

    private static byte[] DecryptBytes(byte[] data, byte[] key, byte[] noncePrefix)
    {
        using var ms = new MemoryStream(data);
        using var reader = new ChunkedAeadReadStream(ms, key, noncePrefix, data.Length);
        using var output = new MemoryStream(data.Length);
        reader.CopyTo(output);
        return output.ToArray();
    }

    private static string ResolveSafePath(string outputRoot, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
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

    private static void TrySetModified(string path, long modifiedUnix)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTimeOffset.FromUnixTimeSeconds(modifiedUnix).UtcDateTime);
        }
        catch
        {
            // best-effort metadata restore
        }
    }

    private static long ToUnix(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static void WriteInt64(Stream stream, long value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    private static int ReadInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        ReadExactly(stream, bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static long ReadInt64(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[8];
        ReadExactly(stream, bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    private static byte[] ReadBytes(Stream stream, int count)
    {
        var buffer = new byte[count];
        ReadExactly(stream, buffer);
        return buffer;
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var n = stream.Read(destination[read..]);
            if (n == 0)
            {
                throw new EndOfStreamException("Unexpected end of BoltZip archive.");
            }

            read += n;
        }
    }
}
