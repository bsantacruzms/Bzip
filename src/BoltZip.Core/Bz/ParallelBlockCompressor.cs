using System.Numerics;
using BoltZip.Core.Compression;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace BoltZip.Core.Bz;

/// <summary>
/// Compresses the concatenated bytes of a set of files into a sequence of independent
/// Zstandard frames, compressing blocks in parallel across all CPU cores. Concatenated
/// zstd frames decompress transparently as a single stream, so the reader/format need no
/// changes — this is purely a faster way to produce the same solid content stream.
/// </summary>
internal static class ParallelBlockCompressor
{
    internal readonly record struct FileRef(string Path, string EntryName, long Size);

    /// <summary>
    /// Reads every file in order, splits the byte stream into <paramref name="blockSize"/>
    /// blocks, compresses up to <paramref name="workers"/> blocks concurrently, and writes
    /// the resulting frames to <paramref name="sink"/> in order. Returns bytes read.
    /// </summary>
    public static long Compress(
        Stream sink,
        IReadOnlyList<FileRef> files,
        CompressionPlan plan,
        int workers,
        int blockSize,
        Action<long, string?>? onProgress,
        CancellationToken cancellationToken)
    {
        var windowLog = ClampWindowLog(blockSize, plan.WindowBytes);

        var input = new byte[workers][];
        var frames = new byte[workers][];
        var frameLengths = new int[workers];
        var blockLengths = new int[workers];
        var entryNames = new string?[workers];
        for (var i = 0; i < workers; i++)
        {
            input[i] = new byte[blockSize];
        }

        var reader = new ConcatReader(files);
        long processed = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = 0;
            while (count < workers)
            {
                var read = reader.ReadBlock(input[count], out var entry);
                if (read <= 0)
                {
                    break;
                }

                blockLengths[count] = read;
                entryNames[count] = entry;
                count++;
            }

            if (count == 0)
            {
                break;
            }

            Parallel.For(0, count, new ParallelOptions
            {
                MaxDegreeOfParallelism = workers,
                CancellationToken = cancellationToken,
            }, j =>
            {
                frames[j] = CompressBlock(
                    input[j], blockLengths[j], plan.Level, windowLog,
                    plan.LongDistanceMatching, out frameLengths[j]);
            });

            for (var j = 0; j < count; j++)
            {
                sink.Write(frames[j], 0, frameLengths[j]);
                processed += blockLengths[j];
                onProgress?.Invoke(processed, entryNames[j]);
                frames[j] = null!;
            }
        }

        reader.Dispose();
        return processed;
    }

    private static byte[] CompressBlock(
        byte[] data, int length, int level, int windowLog, bool ldm, out int frameLength)
    {
        using var compressor = new Compressor(level);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_windowLog, windowLog);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching, ldm ? 1 : 0);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_checksumFlag, 1);

        var dest = new byte[Compressor.GetCompressBound(length)];
        frameLength = compressor.Wrap(data.AsSpan(0, length), dest);
        return dest;
    }

    private static int ClampWindowLog(int blockSize, long planWindowBytes)
    {
        var planLog = Math.Max(10, BitOperations.Log2((ulong)Math.Max(1, planWindowBytes)));
        var blockLog = Math.Max(10, CeilLog2(blockSize));
        return Math.Min(planLog, blockLog);
    }

    private static int CeilLog2(long value)
    {
        var log = 0;
        var v = 1L;
        while (v < value)
        {
            v <<= 1;
            log++;
        }

        return log;
    }

    private static void TrySet(Compressor compressor, ZSTD_cParameter parameter, int value)
    {
        try
        {
            compressor.SetParameter(parameter, value);
        }
        catch
        {
            // Parameter unsupported by this build; ignore.
        }
    }

    /// <summary>Reads the concatenated bytes of the given files, in order, block by block.</summary>
    private sealed class ConcatReader : IDisposable
    {
        private readonly IReadOnlyList<FileRef> _files;
        private int _index;
        private FileStream? _current;
        private string? _currentEntry;

        public ConcatReader(IReadOnlyList<FileRef> files) => _files = files;

        /// <summary>
        /// Fills <paramref name="buffer"/> with up to its length in bytes, spanning file
        /// boundaries. Returns the number of bytes read (0 at end of all files).
        /// </summary>
        public int ReadBlock(byte[] buffer, out string? entry)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                if (_current == null && !AdvanceFile())
                {
                    break;
                }

                var read = _current!.Read(buffer, offset, buffer.Length - offset);
                if (read == 0)
                {
                    _current.Dispose();
                    _current = null;
                    continue;
                }

                offset += read;
            }

            entry = _currentEntry;
            return offset;
        }

        private bool AdvanceFile()
        {
            while (_index < _files.Count)
            {
                var file = _files[_index];
                _index++;
                _currentEntry = file.EntryName;

                if (file.Size <= 0)
                {
                    continue; // empty file contributes no content bytes
                }

                _current = new FileStream(
                    file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            _current?.Dispose();
            _current = null;
        }
    }
}
