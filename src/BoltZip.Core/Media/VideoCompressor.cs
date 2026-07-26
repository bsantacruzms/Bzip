using System.Diagnostics;
using System.Globalization;
using System.Text;
using BoltZip.Core.Hardware;

namespace BoltZip.Core.Media;

/// <summary>
/// Shrinks videos by re-encoding them with a modern codec, using the machine's GPU video encoder
/// when one is available (NVIDIA NVENC, AMD AMF, Intel Quick Sync) and falling back to a software
/// encoder otherwise. Re-encoding is how a video is made smaller: at the visually-lossless setting
/// the result is indistinguishable from the source to the eye, just not byte-for-byte identical.
/// FFmpeg does the actual encoding; BoltZip picks the best settings for your hardware.
/// </summary>
public sealed class VideoCompressor
{
    private readonly string _ffmpegPath;
    private readonly string? _ffprobePath;

    public VideoCompressor(string ffmpegPath, string? ffprobePath = null)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath ?? FfmpegLocator.FindFfprobe();
    }

    /// <summary>Chooses the encoder for the given hardware, requested codec and quality.</summary>
    public static VideoEncodePlan PlanEncode(
        HardwareProfile hardware, VideoCodec codec, VideoQuality quality, bool forceCpu = false)
    {
        var target = codec == VideoCodec.Auto ? VideoCodec.Hevc : codec;
        var rationale = new List<string>();

        var gpu = hardware.Gpus.FirstOrDefault(g => g.IsDiscrete)
                  ?? hardware.Gpus.FirstOrDefault(g => g.Vendor is GpuVendor.Intel);

        VideoEncoder cpuFallback = CpuEncoder(target);

        if (forceCpu || gpu is null)
        {
            rationale.Add(forceCpu
                ? "Software encoding requested → using a CPU encoder."
                : "No GPU video encoder detected → using a CPU encoder.");
            rationale.Add($"Encoder: {cpuFallback.DisplayName}, quality '{quality}'.");
            return new VideoEncodePlan(cpuFallback, cpuFallback, quality, rationale);
        }

        var primary = GpuEncoder(gpu.Vendor, target);
        if (primary is null)
        {
            rationale.Add($"{gpu.Vendor} GPU has no {target} hardware encoder → using a CPU encoder.");
            rationale.Add($"Encoder: {cpuFallback.DisplayName}, quality '{quality}'.");
            return new VideoEncodePlan(cpuFallback, cpuFallback, quality, rationale);
        }

        rationale.Add($"{gpu.Name} → hardware {target} encoding ({primary.FfmpegName}).");
        rationale.Add($"Quality '{quality}'. Falls back to {cpuFallback.DisplayName} if the GPU encoder is unavailable.");
        return new VideoEncodePlan(primary, cpuFallback, quality, rationale);
    }

    private static VideoEncoder? GpuEncoder(GpuVendor vendor, VideoCodec codec) => vendor switch
    {
        GpuVendor.Nvidia => codec switch
        {
            VideoCodec.Hevc => new("hevc_nvenc", "NVIDIA HEVC (NVENC)", VideoCodec.Hevc, true, vendor),
            VideoCodec.Av1 => new("av1_nvenc", "NVIDIA AV1 (NVENC)", VideoCodec.Av1, true, vendor),
            VideoCodec.H264 => new("h264_nvenc", "NVIDIA H.264 (NVENC)", VideoCodec.H264, true, vendor),
            _ => null,
        },
        GpuVendor.Amd => codec switch
        {
            VideoCodec.Hevc => new("hevc_amf", "AMD HEVC (AMF)", VideoCodec.Hevc, true, vendor),
            VideoCodec.Av1 => new("av1_amf", "AMD AV1 (AMF)", VideoCodec.Av1, true, vendor),
            VideoCodec.H264 => new("h264_amf", "AMD H.264 (AMF)", VideoCodec.H264, true, vendor),
            _ => null,
        },
        GpuVendor.Intel => codec switch
        {
            VideoCodec.Hevc => new("hevc_qsv", "Intel HEVC (Quick Sync)", VideoCodec.Hevc, true, vendor),
            VideoCodec.Av1 => new("av1_qsv", "Intel AV1 (Quick Sync)", VideoCodec.Av1, true, vendor),
            VideoCodec.H264 => new("h264_qsv", "Intel H.264 (Quick Sync)", VideoCodec.H264, true, vendor),
            _ => null,
        },
        _ => null,
    };

    private static VideoEncoder CpuEncoder(VideoCodec codec) => codec switch
    {
        VideoCodec.Av1 => new("libsvtav1", "AV1 (SVT-AV1, CPU)", VideoCodec.Av1, false, GpuVendor.Unknown),
        VideoCodec.H264 => new("libx264", "H.264 (x264, CPU)", VideoCodec.H264, false, GpuVendor.Unknown),
        _ => new("libx265", "HEVC (x265, CPU)", VideoCodec.Hevc, false, GpuVendor.Unknown),
    };

    /// <summary>Builds the FFmpeg argument list for a single encode (without runtime/progress flags).</summary>
    public static IReadOnlyList<string> BuildArguments(
        string input, string output, VideoEncoder encoder, VideoQuality quality)
    {
        var args = new List<string>
        {
            "-y", "-hide_banner",
            "-i", input,
            "-map", "0:v:0", "-map", "0:a?",
            "-c:v", encoder.FfmpegName,
        };
        args.AddRange(RateControlArgs(encoder.FfmpegName, quality));
        args.Add("-c:a");
        args.Add("copy");

        var ext = Path.GetExtension(output).ToLowerInvariant();
        if (ext is ".mp4" or ".m4v" or ".mov")
        {
            if (encoder.Codec == VideoCodec.Hevc)
            {
                args.Add("-tag:v");
                args.Add("hvc1");
            }

            args.Add("-movflags");
            args.Add("+faststart");
        }

        args.Add(output);
        return args;
    }

    /// <summary>The rate-control flags (quality target) for a given FFmpeg encoder.</summary>
    public static IReadOnlyList<string> RateControlArgs(string encoder, VideoQuality quality)
    {
        var q = QualityNumber(encoder, quality).ToString(CultureInfo.InvariantCulture);

        if (encoder.Contains("nvenc", StringComparison.Ordinal))
        {
            return new[] { "-preset", "p5", "-rc", "vbr", "-cq", q, "-b:v", "0" };
        }

        if (encoder.Contains("qsv", StringComparison.Ordinal))
        {
            return new[] { "-global_quality", q };
        }

        if (encoder.Contains("amf", StringComparison.Ordinal))
        {
            return new[] { "-rc", "cqp", "-qp_i", q, "-qp_p", q, "-qp_b", q };
        }

        if (encoder.Contains("svtav1", StringComparison.Ordinal))
        {
            return new[] { "-preset", "6", "-crf", q };
        }

        // libx265 / libx264
        return new[] { "-preset", "medium", "-crf", q };
    }

    private static int QualityNumber(string encoder, VideoQuality quality)
    {
        var av1 = encoder.Contains("av1", StringComparison.Ordinal) || encoder.Contains("svtav1", StringComparison.Ordinal);

        (int vl, int bal, int sm) = encoder switch
        {
            _ when encoder.Contains("nvenc", StringComparison.Ordinal) => av1 ? (28, 34, 40) : (19, 24, 28),
            _ when encoder.Contains("qsv", StringComparison.Ordinal) => av1 ? (28, 34, 40) : (22, 26, 30),
            _ when encoder.Contains("amf", StringComparison.Ordinal) => av1 ? (28, 34, 40) : (20, 24, 28),
            _ when encoder.Contains("svtav1", StringComparison.Ordinal) => (26, 32, 40),
            _ => (20, 24, 28), // libx265 / libx264
        };

        return quality switch
        {
            VideoQuality.VisuallyLossless => vl,
            VideoQuality.Balanced => bal,
            _ => sm,
        };
    }

    /// <summary>The default output file for an input: an <c>.mp4</c> alongside it (or in <paramref name="outputDir"/>).</summary>
    public static string DefaultOutputPath(string input, string? outputDir)
    {
        var fullInput = Path.GetFullPath(input);
        var dir = outputDir is not null ? Path.GetFullPath(outputDir) : Path.GetDirectoryName(fullInput)!;
        var name = Path.GetFileNameWithoutExtension(input);
        var output = Path.Combine(dir, name + ".mp4");
        if (string.Equals(Path.GetFullPath(output), fullInput, StringComparison.OrdinalIgnoreCase))
        {
            output = Path.Combine(dir, name + "-boltzip.mp4");
        }

        return output;
    }

    /// <summary>Expands an input (a video file or a folder) into the list of videos to process.</summary>
    public static IReadOnlyList<string> CollectVideos(string input)
    {
        var full = Path.GetFullPath(input);
        if (Directory.Exists(full))
        {
            return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
                .Where(VideoFormats.IsVideo)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (File.Exists(full))
        {
            return VideoFormats.IsVideo(full) ? new[] { full } : Array.Empty<string>();
        }

        throw new FileNotFoundException($"Input not found: {input}");
    }

    /// <summary>Re-encodes one video, returning the sizes achieved. Retries on the CPU if the GPU encoder fails.</summary>
    public async Task<VideoEncodeResult> CompressAsync(
        string input,
        string output,
        VideoEncodePlan plan,
        IProgress<VideoProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var inputBytes = new FileInfo(input).Length;
        var durationSec = await ProbeDurationSecondsAsync(input, cancellationToken);
        var outDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        var sw = Stopwatch.StartNew();
        var usedFallback = false;
        var encoder = plan.Primary;

        var (exitCode, stderr) = await RunEncodeAsync(input, output, encoder, plan.Quality, durationSec, progress, cancellationToken);
        if (exitCode != 0 && encoder.IsHardware && plan.CpuFallback.FfmpegName != encoder.FfmpegName)
        {
            // The GPU encoder failed to initialize (driver/session limits, unsupported input) — fall back.
            usedFallback = true;
            encoder = plan.CpuFallback;
            (exitCode, stderr) = await RunEncodeAsync(input, output, encoder, plan.Quality, durationSec, progress, cancellationToken);
        }

        sw.Stop();
        if (exitCode != 0)
        {
            TryDelete(output);
            var detail = string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" FFmpeg said: {Tail(stderr, 400)}";
            throw new InvalidOperationException($"Encoding failed for '{Path.GetFileName(input)}' (exit {exitCode}).{detail}");
        }

        var outputBytes = new FileInfo(output).Length;
        return new VideoEncodeResult(input, output, inputBytes, outputBytes, sw.Elapsed, encoder, usedFallback);
    }

    private async Task<(int ExitCode, string Stderr)> RunEncodeAsync(
        string input, string output, VideoEncoder encoder, VideoQuality quality,
        double? durationSec, IProgress<VideoProgress>? progress, CancellationToken cancellationToken)
    {
        var runArgs = new List<string>(BuildArguments(input, output, encoder, quality));
        var insertAt = runArgs.IndexOf("-i");
        runArgs.InsertRange(insertAt, new[] { "-nostdin", "-loglevel", "error", "-progress", "pipe:1", "-nostats" });

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in runArgs)
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stderrTask = proc.StandardError.ReadToEndAsync(cancellationToken);
        var name = Path.GetFileName(input);
        var speed = 0d;

        try
        {
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync(cancellationToken)) is not null)
            {
                if (line.StartsWith("speed=", StringComparison.Ordinal))
                {
                    var s = line.Substring(6).TrimEnd('x', ' ');
                    if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sp))
                    {
                        speed = sp;
                    }
                }
                else if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && durationSec is > 0)
                {
                    if (long.TryParse(line.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
                    {
                        var pct = Math.Clamp(us / 1_000_000d / durationSec.Value * 100d, 0, 100);
                        progress?.Report(new VideoProgress(name, pct, speed));
                    }
                }
                else if (line.StartsWith("progress=end", StringComparison.Ordinal))
                {
                    progress?.Report(new VideoProgress(name, 100, speed));
                }
            }

            await proc.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(proc);
            throw;
        }

        var stderr = await stderrTask;
        return (proc.ExitCode, stderr);
    }

    private async Task<double?> ProbeDurationSecondsAsync(string input, CancellationToken cancellationToken)
    {
        if (_ffprobePath is null)
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffprobePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", input })
            {
                psi.ArgumentList.Add(a);
            }

            using var proc = new Process { StartInfo = psi };
            proc.Start();
            var outText = await proc.StandardOutput.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);
            if (double.TryParse(outText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                return seconds;
            }
        }
        catch
        {
            // Duration probe is best-effort; without it progress is simply indeterminate.
        }

        return null;
    }

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string Tail(string text, int chars) =>
        text.Length <= chars ? text.Trim() : text[^chars..].Trim();
}
