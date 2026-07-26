using BoltZip.Core.Bz;
using BoltZip.Core.Hardware;

namespace BoltZip.Core.Compression;

/// <summary>
/// High-level entry point used by the CLI and GUI. Detects formats, auto-plans compression
/// from the current hardware, and routes to the native <see cref="BzArchive"/> or the
/// <see cref="StandardArchive"/> engines.
/// </summary>
public sealed class ArchiveService
{
    /// <summary>Detect a format from a path's extension, falling back to magic-byte sniffing.</summary>
    public FormatDescriptor DetectFormat(string path)
    {
        var descriptor = FormatInfo.DetectFromPath(path);
        if (descriptor.Format != ArchiveFormat.Unknown)
        {
            return descriptor;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[512];
            var read = stream.Read(head);
            var sniffed = FormatInfo.SniffFromMagic(head[..read]);
            return new FormatDescriptor(sniffed, TarWrapped: false);
        }
        catch
        {
            return descriptor;
        }
    }

    /// <summary>Compute (but do not run) the hardware-tuned plan for a create request.</summary>
    public async Task<CompressionPlan> PlanAsync(CreateRequest request, CancellationToken cancellationToken = default)
    {
        var descriptor = ResolveCreateFormat(request);
        if (request.PlanOverride is not null)
        {
            return request.PlanOverride;
        }

        var hardware = request.Hardware ?? await HardwareProbe.DetectAsync(cancellationToken);
        var targetStorage = await StorageProbe.GetKindAsync(TargetDirectory(request.OutputPath), cancellationToken);
        var content = ContentAnalysis.Analyze(request.Inputs);
        return OptimizationPlanner.Plan(
            hardware, request.Goal, descriptor.Format, content.TotalBytes, targetStorage, content.IncompressibleRatio);
    }

    /// <summary>Create an archive; returns the resolved output path and the plan that was applied.</summary>
    public async Task<CreateResult> CreateAsync(
        CreateRequest request,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = ResolveCreateFormat(request);

        if (!FormatInfo.CanCreate(descriptor.Format))
        {
            throw new NotSupportedException($"Creating {descriptor.Format} archives is not supported.");
        }

        if (!string.IsNullOrEmpty(request.Password) && descriptor.Format != ArchiveFormat.Bz)
        {
            throw new NotSupportedException(
                "Password encryption is available for the .bz format. Use a .bz output for password protection.");
        }

        var hardware = request.Hardware ?? await HardwareProbe.DetectAsync(cancellationToken);
        var targetStorage = await StorageProbe.GetKindAsync(TargetDirectory(request.OutputPath), cancellationToken);
        var content = ContentAnalysis.Analyze(request.Inputs);
        var plan = request.PlanOverride
            ?? OptimizationPlanner.Plan(
                hardware, request.Goal, descriptor.Format, content.TotalBytes, targetStorage, content.IncompressibleRatio);

        var outputPath = ResolveOutputPath(request.OutputPath, descriptor.Format, descriptor.TarWrapped, request.Inputs);

        if (descriptor.Format == ArchiveFormat.Bz)
        {
            await BzArchive.CreateAsync(outputPath, request.Inputs, request.Password, plan, progress, cancellationToken);
        }
        else
        {
            await Task.Run(
                () => StandardArchive.Create(descriptor.Format, descriptor.TarWrapped, request.Inputs, outputPath, plan, progress, cancellationToken),
                cancellationToken);
        }

        return new CreateResult(outputPath, plan);
    }

    public async Task ExtractAsync(
        ExtractRequest request,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = DetectFormat(request.ArchivePath);
        if (descriptor.Format == ArchiveFormat.Unknown)
        {
            throw new NotSupportedException($"Unrecognized archive format: {request.ArchivePath}");
        }

        if (descriptor.Format == ArchiveFormat.Bz)
        {
            await BzArchive.ExtractAsync(
                request.ArchivePath, request.OutputDirectory, request.Password, request.Overwrite, progress, cancellationToken);
        }
        else
        {
            await Task.Run(
                () => StandardArchive.Extract(
                    descriptor.Format, descriptor.TarWrapped, request.ArchivePath, request.OutputDirectory,
                    request.Password, request.Overwrite, progress, cancellationToken),
                cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ArchiveEntryInfo>> ListAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        var descriptor = DetectFormat(archivePath);
        if (descriptor.Format == ArchiveFormat.Bz)
        {
            return await Task.Run(() => BzArchive.List(archivePath, password), cancellationToken);
        }

        return await Task.Run(() => StandardArchive.List(archivePath, password), cancellationToken);
    }

    private static FormatDescriptor ResolveCreateFormat(CreateRequest request)
    {
        if (request.Format is { } explicitFormat)
        {
            var detectedForTar = FormatInfo.DetectFromPath(request.OutputPath);
            var tar = detectedForTar.Format == explicitFormat && detectedForTar.TarWrapped;
            return new FormatDescriptor(explicitFormat, tar);
        }

        var detected = FormatInfo.DetectFromPath(request.OutputPath);
        if (detected.Format == ArchiveFormat.Unknown)
        {
            throw new NotSupportedException(
                $"Cannot infer a format from '{request.OutputPath}'. Use a known extension (e.g. .bz, .zip, .zst).");
        }

        return detected;
    }

    private static string ResolveOutputPath(string outputPath, ArchiveFormat format, bool tarWrapped, IReadOnlyList<string> inputs)
    {
        if (!FormatInfo.IsSingleStreamCodec(format) || tarWrapped)
        {
            return outputPath;
        }

        var multipleFiles = inputs.Count > 1 ||
            (inputs.Count == 1 && Directory.Exists(Path.GetFullPath(inputs[0])));
        if (!multipleFiles)
        {
            return outputPath;
        }

        var extension = Path.GetExtension(outputPath);
        if (string.IsNullOrEmpty(extension))
        {
            return outputPath;
        }

        // Ensure the name reflects the internal tar so extraction untars it (e.g. .zst -> .tar.zst).
        return string.Concat(outputPath.AsSpan(0, outputPath.Length - extension.Length), ".tar", extension);
    }

    private static string TargetDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        return string.IsNullOrEmpty(directory) ? Directory.GetCurrentDirectory() : directory;
    }
}
