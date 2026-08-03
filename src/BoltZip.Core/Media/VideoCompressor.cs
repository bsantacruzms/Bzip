using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
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
    private static readonly IReadOnlySet<string> Mp4AudioCodecs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "aac", "ac3", "eac3", "mp3", "alac" };
    private static readonly IReadOnlySet<string> Mp4SubtitleCodecs =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mov_text", "tx3g" };

    private readonly string _ffmpegPath;
    private readonly string? _ffprobePath;
    private Task<IReadOnlySet<string>?>? _availableEncodersTask;

    public VideoCompressor(string ffmpegPath, string? ffprobePath = null)
    {
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath ?? FfmpegLocator.FindFfprobe();
    }

    /// <summary>Chooses the encoder for the given hardware, requested codec and quality.</summary>
    /// <param name="macOs">Overrides macOS detection (for tests). Null uses the current OS.</param>
    public static VideoEncodePlan PlanEncode(
        HardwareProfile hardware, VideoCodec codec, VideoQuality quality, bool forceCpu = false, bool? macOs = null)
    {
        var target = codec == VideoCodec.Auto ? VideoCodec.Hevc : codec;
        var rationale = new List<string>();

        var gpu = hardware.Gpus.FirstOrDefault(g => g.IsDiscrete)
                  ?? hardware.Gpus.FirstOrDefault(g => g.Vendor is GpuVendor.Intel);

        VideoEncoder cpuFallback = CpuEncoder(target);

        if (forceCpu)
        {
            rationale.Add("Software encoding requested → using a CPU encoder.");
            rationale.Add($"Encoder: {cpuFallback.DisplayName}, quality '{quality}'.");
            return new VideoEncodePlan(cpuFallback, cpuFallback, quality, rationale);
        }

        // Every Mac (Intel and Apple silicon) has VideoToolbox hardware encoding, so it does not
        // depend on discovering a discrete GPU the way the Windows/Linux paths do.
        if (macOs ?? OperatingSystem.IsMacOS())
        {
            var appleEncoder = AppleEncoder(target);
            if (appleEncoder is not null)
            {
                rationale.Add($"macOS → hardware {target} encoding with VideoToolbox ({appleEncoder.FfmpegName}).");
                rationale.Add($"Quality '{quality}'. Falls back to {cpuFallback.DisplayName} if VideoToolbox is unavailable.");
                return new VideoEncodePlan(appleEncoder, cpuFallback, quality, rationale);
            }

            rationale.Add($"macOS VideoToolbox has no {target} encoder → using a CPU encoder.");
            rationale.Add($"Encoder: {cpuFallback.DisplayName}, quality '{quality}'.");
            return new VideoEncodePlan(cpuFallback, cpuFallback, quality, rationale);
        }

        if (gpu is null)
        {
            rationale.Add("No GPU video encoder detected → using a CPU encoder.");
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

    /// <summary>Apple VideoToolbox encoders. VideoToolbox has no AV1 encoder, so AV1 stays on the CPU.</summary>
    private static VideoEncoder? AppleEncoder(VideoCodec codec) => codec switch
    {
        VideoCodec.Hevc => new("hevc_videotoolbox", "Apple HEVC (VideoToolbox)", VideoCodec.Hevc, true, GpuVendor.Apple),
        VideoCodec.H264 => new("h264_videotoolbox", "Apple H.264 (VideoToolbox)", VideoCodec.H264, true, GpuVendor.Apple),
        _ => null,
    };


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
        string input, string output, VideoEncoder encoder, VideoQuality quality, VideoSourceInfo? sourceInfo = null)
    {
        var args = new List<string>
        {
            "-y", "-hide_banner",
            "-i", input,
            "-map", sourceInfo?.PrimaryVideoStreamIndex is { } videoIndex ? $"0:{videoIndex}" : "0:v:0",
            "-map", "0:a?", "-map", "0:s?",
            "-map_metadata", "0", "-map_chapters", "0",
            "-c:v", encoder.FfmpegName,
        };
        args.AddRange(RateControlArgs(encoder.FfmpegName, quality));
        args.Add("-c:a");
        args.Add("copy");
        args.Add("-c:s");
        args.Add("copy");

        var ext = Path.GetExtension(output).ToLowerInvariant();
        if (ext == ".mkv")
        {
            args.Add("-map");
            args.Add("0:t?");
            args.Add("-c:t");
            args.Add("copy");

            for (var index = 0; index < (sourceInfo?.SubtitleCodecs.Count ?? 0); index++)
            {
                if (sourceInfo!.SubtitleCodecs[index] is "mov_text" or "tx3g")
                {
                    args.Add($"-c:s:{index}");
                    args.Add("srt");
                }
            }
        }

        AddArgument(args, "-pix_fmt", ResolveOutputPixelFormat(encoder, sourceInfo?.PixelFormat));
        AddArgument(args, "-color_primaries", sourceInfo?.ColorPrimaries);
        AddArgument(args, "-color_trc", sourceInfo?.ColorTransfer);
        AddArgument(args, "-colorspace", sourceInfo?.ColorSpace);
        AddArgument(args, "-bsf:v", ColorMetadataBitstreamFilter(encoder, sourceInfo));

        if (ext is ".mp4" or ".m4v" or ".mov")
        {
            if (encoder.Codec == VideoCodec.Hevc)
            {
                args.Add("-tag:v");
                args.Add("hvc1");
            }

            args.Add("-movflags");
            args.Add("+faststart+write_colr");
        }

        args.Add(output);
        return args;
    }

    private static void AddArgument(List<string> args, string flag, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            args.Add(flag);
            args.Add(value);
        }
    }

    private static string? ResolveOutputPixelFormat(VideoEncoder encoder, string? sourcePixelFormat)
    {
        if (string.IsNullOrWhiteSpace(sourcePixelFormat))
        {
            return null;
        }

        var format = sourcePixelFormat.ToLowerInvariant();
        if (format is "yuv420p" or "nv12")
        {
            return "yuv420p";
        }

        if (format is "yuv420p10le" or "p010le")
        {
            if (encoder.Codec == VideoCodec.H264)
            {
                return null;
            }

            return encoder.IsHardware ? "p010le" : "yuv420p10le";
        }

        return null;
    }

    private static string? ColorMetadataBitstreamFilter(VideoEncoder encoder, VideoSourceInfo? source)
    {
        if (source is null || encoder.Codec is not (VideoCodec.Hevc or VideoCodec.H264 or VideoCodec.Av1))
        {
            return null;
        }

        var primaries = ColorCode(source.ColorPrimaries, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["bt709"] = 1, ["bt470m"] = 4, ["bt470bg"] = 5, ["smpte170m"] = 6,
            ["smpte240m"] = 7, ["film"] = 8, ["bt2020"] = 9, ["smpte428"] = 10,
            ["smpte431"] = 11, ["smpte432"] = 12, ["ebu3213"] = 22,
        });
        var transfer = ColorCode(source.ColorTransfer, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["bt709"] = 1, ["gamma22"] = 4, ["gamma28"] = 5, ["smpte170m"] = 6,
            ["smpte240m"] = 7, ["linear"] = 8, ["log"] = 9, ["log_sqrt"] = 10,
            ["iec61966-2-4"] = 11, ["bt1361e"] = 12, ["iec61966-2-1"] = 13,
            ["bt2020-10"] = 14, ["bt2020-12"] = 15, ["smpte2084"] = 16,
            ["smpte428"] = 17, ["arib-std-b67"] = 18,
        });
        var matrix = ColorCode(source.ColorSpace, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["rgb"] = 0, ["bt709"] = 1, ["fcc"] = 4, ["bt470bg"] = 5,
            ["smpte170m"] = 6, ["smpte240m"] = 7, ["ycgco"] = 8,
            ["bt2020nc"] = 9, ["bt2020c"] = 10,
        });

        var settings = new List<string>();
        if (primaries is not null)
        {
            var primariesOption = encoder.Codec == VideoCodec.Av1 ? "color_primaries" : "colour_primaries";
            settings.Add($"{primariesOption}={primaries}");
        }
        if (transfer is not null) settings.Add($"transfer_characteristics={transfer}");
        if (matrix is not null) settings.Add($"matrix_coefficients={matrix}");
        if (settings.Count == 0)
        {
            return null;
        }

        var filter = encoder.Codec switch
        {
            VideoCodec.Hevc => "hevc_metadata",
            VideoCodec.H264 => "h264_metadata",
            _ => "av1_metadata",
        };
        return filter + "=" + string.Join(':', settings);
    }

    private static int? ColorCode(string? value, IReadOnlyDictionary<string, int> codes) =>
        value is not null && codes.TryGetValue(value, out var code) ? code : null;

    /// <summary>Explains when the selected encoder cannot guarantee preservation of source bit depth or chroma.</summary>
    public static string? PixelFormatWarning(VideoSourceInfo source, VideoEncoder encoder)
    {
        var format = source.PixelFormat?.ToLowerInvariant();
        if (format is null or "yuv420p" or "nv12")
        {
            return null;
        }

        if (format is "yuv420p10le" or "p010le")
        {
            return encoder.Codec == VideoCodec.H264
                ? $"{encoder.DisplayName} cannot reliably preserve 10-bit HDR depth; choose HEVC or AV1 to avoid conversion."
                : null;
        }

        return $"{encoder.DisplayName} may not preserve source pixel format '{source.PixelFormat}'; verify the output before deleting the source.";
    }

    /// <summary>Warnings for source video metadata or streams that re-encoding cannot guarantee.</summary>
    public static IReadOnlyList<string> PreservationWarnings(VideoSourceInfo source)
    {
        var warnings = new List<string>();
        if (source.VideoStreamCount > 1)
        {
            warnings.Add(
                $"The source has {source.VideoStreamCount} video streams. BoltZip encodes the primary stream only; " +
                "alternate angles, enhancement layers, or video cover art will not be retained.");
        }

            if (source.DataStreamCount > 0)
            {
                warnings.Add(
                $"The source has {source.DataStreamCount} auxiliary data stream(s), such as MOV timecode, " +
                "that cannot be reliably retained in the output container.");
            }

        if (source.HasDolbyVision)
        {
            warnings.Add(
                "Standard HDR color tags will be retained where supported, but FFmpeg cannot preserve the Dolby Vision RPU.");
        }

        if (source.HasMasteringDisplayMetadata || source.HasContentLightMetadata)
        {
            warnings.Add(
                "Detected BT.2020/PQ or HLG signaling and supported bit depth will be retained where the selected encoder supports them, but HDR mastering-display " +
                "metadata and MaxCLL/MaxFALL are not guaranteed by the selected encoder.");
        }

        return warnings;
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

        if (encoder.Contains("videotoolbox", StringComparison.Ordinal))
        {
            // VideoToolbox quality runs 0-100 where HIGHER is better, the opposite of CRF/CQ.
            // -allow_sw lets macOS fall back to its software encoder rather than failing outright.
            return new[] { "-q:v", q, "-allow_sw", "1" };
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
            // VideoToolbox is inverted: higher number = higher quality.
            _ when encoder.Contains("videotoolbox", StringComparison.Ordinal) => (65, 50, 38),
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

    /// <summary>Chooses the safest compatible container for the streams that will be preserved.</summary>
    public static VideoContainer ChooseContainer(VideoSourceInfo source, VideoContainer requested = VideoContainer.Auto)
    {
        if (requested != VideoContainer.Auto)
        {
            return requested;
        }

        if (!source.InspectionSucceeded || source.AudioStreams.Count > 1 || source.ChapterCount > 0 || source.AttachmentCount > 0)
        {
            return VideoContainer.Mkv;
        }

        return Mp4CompatibilityProblem(source) is null ? VideoContainer.Mp4 : VideoContainer.Mkv;
    }

    /// <summary>Returns why MP4 cannot safely preserve the inspected streams, or null when compatible.</summary>
    public static string? Mp4CompatibilityProblem(VideoSourceInfo source)
    {
        if (!source.InspectionSucceeded)
        {
            return "the source streams could not be inspected";
        }

        var audio = source.AudioStreams.FirstOrDefault(stream => !Mp4AudioCodecs.Contains(stream.Codec));
        if (audio is not null)
        {
            return $"audio codec '{audio.Codec}' cannot be safely copied to MP4";
        }

        var subtitle = source.SubtitleCodecs.FirstOrDefault(codec => !Mp4SubtitleCodecs.Contains(codec));
        if (subtitle is not null)
        {
            return $"subtitle codec '{subtitle}' cannot be safely copied to MP4";
        }

        return source.AttachmentCount > 0
            ? "MP4 cannot preserve Matroska attachments such as subtitle fonts"
            : null;
    }

    /// <summary>The default output file alongside the input (or in <paramref name="outputDir"/>).</summary>
    public static string DefaultOutputPath(
        string input,
        string? outputDir,
        VideoSourceInfo? sourceInfo = null,
        VideoContainer container = VideoContainer.Auto)
    {
        var fullInput = Path.GetFullPath(input);
        var dir = outputDir is not null ? Path.GetFullPath(outputDir) : Path.GetDirectoryName(fullInput)!;
        var name = Path.GetFileNameWithoutExtension(input);
        var selected = ChooseContainer(sourceInfo ?? VideoSourceInfo.Unknown, container);
        var extension = selected == VideoContainer.Mp4 ? ".mp4" : ".mkv";
        var output = Path.Combine(dir, name + extension);
        if (string.Equals(Path.GetFullPath(output), fullInput, StringComparison.OrdinalIgnoreCase))
        {
            output = Path.Combine(dir, name + "-boltzip" + extension);
        }

        return output;
    }

    /// <summary>Returns a path that does not replace an existing file.</summary>
    public static string NonCollidingOutputPath(string desiredPath)
    {
        if (!File.Exists(desiredPath))
        {
            return desiredPath;
        }

        var directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        var extension = Path.GetExtension(desiredPath);
        var name = Path.GetFileNameWithoutExtension(desiredPath);
        var baseName = name.EndsWith("-boltzip", StringComparison.OrdinalIgnoreCase) ? name : name + "-boltzip";
        var candidate = Path.Combine(directory, baseName + extension);
        for (var suffix = 2; File.Exists(candidate); suffix++)
        {
            candidate = Path.Combine(directory, $"{baseName}-{suffix}{extension}");
        }

        return candidate;
    }

    /// <summary>Inspects source streams and metadata with FFprobe.</summary>
    public async Task<VideoSourceInfo> InspectAsync(string input, CancellationToken cancellationToken = default)
    {
        if (_ffprobePath is null)
        {
            return VideoSourceInfo.Unknown;
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
            foreach (var arg in new[] { "-v", "error", "-show_streams", "-show_format", "-show_chapters", "-of", "json", input })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = psi };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var output = await outputTask;
            await process.WaitForExitAsync(cancellationToken);
            await stderrTask;
            if (process.ExitCode != 0)
            {
                return VideoSourceInfo.Unknown;
            }

            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var audio = new List<VideoAudioStream>();
            var subtitles = new List<string>();
            string? videoCodec = null;
            string? pixelFormat = null;
            string? colorPrimaries = null;
            string? colorTransfer = null;
            string? colorSpace = null;
            var hasDolbyVision = false;
            var hasMasteringDisplayMetadata = false;
            var hasContentLightMetadata = false;
            var attachmentCount = 0;
            var dataStreamCount = 0;
            var videoStreamCount = 0;
            int? primaryVideoStreamIndex = null;
            var primaryVideoPriority = -1;

            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var type = JsonString(stream, "codec_type");
                    var codec = JsonString(stream, "codec_name") ?? "unknown";
                    if (type == "video")
                    {
                        videoStreamCount++;
                        var streamIndex = JsonInt(stream, "index");
                        var isDefault = JsonInt(stream, "default", "disposition") == 1;
                        var isAttachedPicture = JsonInt(stream, "attached_pic", "disposition") == 1;
                        var priority = isAttachedPicture ? 0 : isDefault ? 2 : 1;
                        if (streamIndex is not null && priority > primaryVideoPriority)
                        {
                            primaryVideoPriority = priority;
                            primaryVideoStreamIndex = streamIndex;
                            videoCodec = codec;
                            pixelFormat = JsonString(stream, "pix_fmt");
                            colorPrimaries = JsonString(stream, "color_primaries");
                            colorTransfer = JsonString(stream, "color_transfer");
                            colorSpace = JsonString(stream, "color_space");
                        }
                    }
                    else if (type == "audio")
                    {
                        audio.Add(new VideoAudioStream(codec, JsonString(stream, "profile")));
                    }
                    else if (type == "subtitle")
                    {
                        subtitles.Add(codec);
                    }
                    else if (type == "attachment")
                    {
                        attachmentCount++;
                    }
                    else if (type == "data")
                    {
                        dataStreamCount++;
                    }

                    if (stream.TryGetProperty("side_data_list", out var sideData))
                    {
                        foreach (var item in sideData.EnumerateArray())
                        {
                            var sideDataType = JsonString(item, "side_data_type") ?? string.Empty;
                            hasDolbyVision |= sideDataType.Contains("DOVI", StringComparison.OrdinalIgnoreCase);
                            hasMasteringDisplayMetadata |= sideDataType.Contains("Mastering display", StringComparison.OrdinalIgnoreCase);
                            hasContentLightMetadata |= sideDataType.Contains("Content light", StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }

            double? duration = null;
            if (root.TryGetProperty("format", out var format) &&
                double.TryParse(JsonString(format, "duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
                seconds > 0)
            {
                duration = seconds;
            }

            var chapterCount = root.TryGetProperty("chapters", out var chapters) ? chapters.GetArrayLength() : 0;
            return new VideoSourceInfo(
                true, duration, videoCodec, primaryVideoStreamIndex, videoStreamCount, audio, subtitles,
                dataStreamCount, attachmentCount, chapterCount, pixelFormat,
                colorPrimaries, colorTransfer, colorSpace, hasDolbyVision,
                hasMasteringDisplayMetadata, hasContentLightMetadata);
        }
        catch
        {
            return VideoSourceInfo.Unknown;
        }
    }

    private static string? JsonString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? JsonInt(JsonElement element, string property, string? parent = null)
    {
        var target = element;
        if (parent is not null && (!element.TryGetProperty(parent, out target) || target.ValueKind != JsonValueKind.Object))
        {
            return null;
        }

        return target.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : null;
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
        CancellationToken cancellationToken = default,
        bool overwriteOutput = true)
    {
        var sourceInfo = await InspectAsync(input, cancellationToken);
        return await CompressAsync(input, output, plan, sourceInfo, progress, cancellationToken, overwriteOutput);
    }

    /// <summary>Re-encodes one already-inspected video without probing it a second time.</summary>
    public async Task<VideoEncodeResult> CompressAsync(
        string input,
        string output,
        VideoEncodePlan plan,
        VideoSourceInfo sourceInfo,
        IProgress<VideoProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool overwriteOutput = true)
    {
        var inputBytes = new FileInfo(input).Length;
        var durationSec = sourceInfo.DurationSeconds ?? await ProbeDurationSecondsAsync(input, cancellationToken);
        var outDir = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        var temporaryOutput = TemporaryOutputPath(output);

        var sw = Stopwatch.StartNew();
        var usedFallback = false;
        var encoder = plan.Primary;
        var availableEncoders = await AvailableEncodersAsync();
        if (availableEncoders is not null && !availableEncoders.Contains(encoder.FfmpegName))
        {
            if (!availableEncoders.Contains(plan.CpuFallback.FfmpegName))
            {
                throw new InvalidOperationException(
                    $"This FFmpeg build has neither '{encoder.FfmpegName}' nor fallback " +
                    $"'{plan.CpuFallback.FfmpegName}'. Install a full FFmpeg build with {encoder.Codec} encoding support.");
            }

            usedFallback = true;
            encoder = plan.CpuFallback;
        }

        var (exitCode, stderr) = await RunEncodeAsync(input, temporaryOutput, encoder, plan.Quality, sourceInfo, durationSec, progress, cancellationToken);
        if (exitCode != 0 && encoder.IsHardware && plan.CpuFallback.FfmpegName != encoder.FfmpegName &&
            (availableEncoders is null || availableEncoders.Contains(plan.CpuFallback.FfmpegName)))
        {
            // The GPU encoder failed to initialize (driver/session limits, unsupported input) — fall back.
            usedFallback = true;
            encoder = plan.CpuFallback;
            (exitCode, stderr) = await RunEncodeAsync(input, temporaryOutput, encoder, plan.Quality, sourceInfo, durationSec, progress, cancellationToken);
        }

        sw.Stop();
        if (exitCode != 0)
        {
            TryDelete(temporaryOutput);
            var detail = string.IsNullOrWhiteSpace(stderr) ? string.Empty : $" FFmpeg said: {Tail(stderr, 400)}";
            throw new InvalidOperationException($"Encoding failed for '{Path.GetFileName(input)}' (exit {exitCode}).{detail}");
        }

        var outputBytes = new FileInfo(temporaryOutput).Length;
        try
        {
            File.Move(temporaryOutput, output, overwriteOutput);
        }
        catch
        {
            TryDelete(temporaryOutput);
            throw;
        }

        return new VideoEncodeResult(input, output, inputBytes, outputBytes, sw.Elapsed, encoder, usedFallback);
    }

    private static string TemporaryOutputPath(string output)
    {
        var fullOutput = Path.GetFullPath(output);
        var directory = Path.GetDirectoryName(fullOutput)!;
        var extension = Path.GetExtension(fullOutput);
        var name = Path.GetFileNameWithoutExtension(fullOutput);
        return Path.Combine(directory, $".{name}.boltzip-{Guid.NewGuid():N}.tmp{extension}");
    }

    private Task<IReadOnlySet<string>?> AvailableEncodersAsync() =>
        _availableEncodersTask ??= ProbeAvailableEncodersAsync();

    private async Task<IReadOnlySet<string>?> ProbeAvailableEncodersAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");

            using var process = new Process { StartInfo = psi };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await outputTask;
            await errorTask;
            if (process.ExitCode != 0)
            {
                return null;
            }

            var encoders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var columns = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (columns.Length >= 2 && columns[0].Length == 6 && columns[0][0] == 'V')
                {
                    encoders.Add(columns[1]);
                }
            }

            return encoders.Count > 0 ? encoders : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(int ExitCode, string Stderr)> RunEncodeAsync(
        string input, string output, VideoEncoder encoder, VideoQuality quality,
        VideoSourceInfo sourceInfo, double? durationSec,
        IProgress<VideoProgress>? progress, CancellationToken cancellationToken)
    {
        var runArgs = new List<string>(BuildArguments(input, output, encoder, quality, sourceInfo));
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
            var stderr = await stderrTask;
            return (proc.ExitCode, stderr);
        }
        catch
        {
            TryKill(proc);
            try
            {
                await proc.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
                // Best effort; cleanup below may still succeed if the process already exited.
            }

            TryDelete(output);
            throw;
        }
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
