using System.Numerics;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace BoltZip.Core.Compression;

/// <summary>Applies a <see cref="CompressionPlan"/> to ZstdSharp streams.</summary>
internal static class ZstdTuning
{
    public static void ApplyCompression(CompressionStream compressor, CompressionPlan plan)
    {
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_compressionLevel, plan.Level);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_windowLog, BitOperations.Log2((ulong)plan.WindowBytes));
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_enableLongDistanceMatching, plan.LongDistanceMatching ? 1 : 0);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_nbWorkers, plan.WorkerThreads);
        TrySet(compressor, ZSTD_cParameter.ZSTD_c_checksumFlag, 1);
    }

    public static void AllowLargeWindow(DecompressionStream decompressor)
    {
        try
        {
            decompressor.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 31);
        }
        catch
        {
            // ignore when unsupported
        }
    }

    private static void TrySet(CompressionStream compressor, ZSTD_cParameter parameter, int value)
    {
        try
        {
            compressor.SetParameter(parameter, value);
        }
        catch
        {
            // Parameter unsupported by this build (e.g. no multithreading); ignore.
        }
    }
}
