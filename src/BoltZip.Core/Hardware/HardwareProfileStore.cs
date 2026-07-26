using System.Text.Json;

namespace BoltZip.Core.Hardware;

/// <summary>
/// Persists a detected <see cref="HardwareProfile"/> so an installed BoltZip only has to
/// profile the machine once. Portable builds pass <c>useCache: false</c> to always re-probe,
/// since the same executable may be carried between different PCs.
/// </summary>
public static class HardwareProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Per-user cache location (e.g. %LOCALAPPDATA%\BoltZip on Windows).</summary>
    public static string DefaultCachePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BoltZip",
            "hardware-profile.json");

    /// <summary>
    /// Returns a hardware profile. When <paramref name="useCache"/> is true and a cached
    /// profile exists it is loaded from disk (no re-probe); otherwise the machine is probed
    /// with <see cref="HardwareProbe.DetectAsync"/> and, if caching, the result is saved.
    /// </summary>
    public static async Task<HardwareProfile> GetProfileAsync(
        bool useCache, CancellationToken cancellationToken = default)
    {
        if (useCache && TryLoad(DefaultCachePath) is { } cached)
        {
            return cached;
        }

        var profile = await HardwareProbe.DetectAsync(cancellationToken);

        if (useCache)
        {
            TrySave(DefaultCachePath, profile);
        }

        return profile;
    }

    /// <summary>Loads a cached profile, or null when absent or unreadable.</summary>
    public static HardwareProfile? TryLoad(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<HardwareProfile>(File.ReadAllText(path), JsonOptions)
                : null;
        }
        catch
        {
            return null; // corrupt or incompatible cache -> re-probe
        }
    }

    /// <summary>Writes the profile to disk (best-effort; caching is an optimization).</summary>
    public static void TrySave(string path, HardwareProfile profile)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
        }
        catch
        {
            // Ignore: a failed cache write simply means we re-probe next time.
        }
    }
}
