using BoltZip.Core.Hardware;

namespace BoltZip.Core.Compression;

/// <summary>Describes a request to create an archive.</summary>
public sealed record CreateRequest
{
    /// <summary>Destination archive file path. Its extension infers the format when <see cref="Format"/> is null.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Files and/or directories to include.</summary>
    public required IReadOnlyList<string> Inputs { get; init; }

    /// <summary>Explicit output format; when null it is inferred from <see cref="OutputPath"/>.</summary>
    public ArchiveFormat? Format { get; init; }

    /// <summary>Optional password. Only honored by formats that support encryption (.bz, zip).</summary>
    public string? Password { get; init; }

    /// <summary>The optimization intent used to auto-tune codec settings.</summary>
    public OptimizationGoal Goal { get; init; } = OptimizationGoal.Balanced;

    /// <summary>Pre-detected hardware profile. When null the engine probes the machine.</summary>
    public HardwareProfile? Hardware { get; init; }

    /// <summary>When set, these exact settings are used and auto-planning is skipped.</summary>
    public CompressionPlan? PlanOverride { get; init; }
}

/// <summary>Describes a request to extract an archive.</summary>
public sealed record ExtractRequest
{
    public required string ArchivePath { get; init; }

    public required string OutputDirectory { get; init; }

    public string? Password { get; init; }

    /// <summary>Overwrite existing files on disk when true.</summary>
    public bool Overwrite { get; init; }
}

/// <summary>The outcome of a create operation.</summary>
public sealed record CreateResult(string OutputPath, CompressionPlan Plan);
