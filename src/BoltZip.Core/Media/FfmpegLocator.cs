namespace BoltZip.Core.Media;

/// <summary>
/// Finds the FFmpeg (and FFprobe) executables. BoltZip does not bundle FFmpeg; it uses the copy
/// already on the system. Search order: the <c>BOLTZIP_FFMPEG</c> override, then <c>PATH</c>, then
/// a few common install locations (winget, Chocolatey, Scoop, Program Files).
/// </summary>
public static class FfmpegLocator
{
    /// <summary>Environment variable pointing at an ffmpeg executable or the folder that contains it.</summary>
    public const string OverrideVariable = "BOLTZIP_FFMPEG";

    /// <summary>Returns the ffmpeg executable path, or null when FFmpeg is not installed.</summary>
    public static string? FindFfmpeg() => Find("ffmpeg");

    /// <summary>Returns the ffprobe executable path, or null when it cannot be found.</summary>
    public static string? FindFfprobe() => Find("ffprobe");

    /// <summary>True when FFmpeg is available on this machine.</summary>
    public static bool IsAvailable() => FindFfmpeg() is not null;

    /// <summary>A short, platform-appropriate hint for installing FFmpeg.</summary>
    public static string InstallHint()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Install FFmpeg with:  winget install Gyan.FFmpeg   (or download from https://ffmpeg.org/download.html), " +
                   "then reopen your terminal. You can also set BOLTZIP_FFMPEG to the ffmpeg.exe path.";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "Install FFmpeg with:  brew install ffmpeg";
        }

        return "Install FFmpeg with your package manager, e.g.  sudo apt install ffmpeg   or   sudo dnf install ffmpeg";
    }

    private static string? Find(string tool)
    {
        var exe = OperatingSystem.IsWindows() ? tool + ".exe" : tool;

        // 1) Explicit override (file path or containing directory).
        var overridePath = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            if (File.Exists(overridePath))
            {
                // If the override points at ffmpeg, resolve a sibling ffprobe when asked.
                var dir = Path.GetDirectoryName(overridePath);
                var sibling = dir is null ? null : Path.Combine(dir, exe);
                if (string.Equals(Path.GetFileNameWithoutExtension(overridePath), tool, StringComparison.OrdinalIgnoreCase))
                {
                    return overridePath;
                }

                if (sibling is not null && File.Exists(sibling))
                {
                    return sibling;
                }
            }

            var asDir = Path.Combine(overridePath, exe);
            if (File.Exists(asDir))
            {
                return asDir;
            }
        }

        // 2) PATH.
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // Malformed PATH entry; skip.
            }
        }

        // 3) Common install locations.
        foreach (var candidate in CommonLocations(exe))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> CommonLocations(string exe)
    {
        if (OperatingSystem.IsWindows())
        {
            var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            // winget shim layout (…\Links) and package install dirs vary; check the common ones.
            yield return Path.Combine(localApp, "Microsoft", "WinGet", "Links", exe);
            yield return Path.Combine(programFiles, "ffmpeg", "bin", exe);
            yield return Path.Combine(user, "scoop", "shims", exe);
            yield return Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "ffmpeg", "bin", exe);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin", exe);
        }
        else
        {
            yield return Path.Combine("/usr/bin", exe);
            yield return Path.Combine("/usr/local/bin", exe);
            yield return Path.Combine("/opt/homebrew/bin", exe);
            yield return Path.Combine("/snap/bin", exe);
        }
    }
}
