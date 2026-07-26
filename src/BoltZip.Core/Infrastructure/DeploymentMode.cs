using System.Runtime.InteropServices;

namespace BoltZip.Core.Infrastructure;

/// <summary>
/// Distinguishes an installed BoltZip (placed by an installer into a system location) from a
/// portable copy. Installed builds cache the hardware profile so the machine is profiled once;
/// portable builds re-profile on every run because the executable can move between PCs.
/// </summary>
public static class DeploymentMode
{
    /// <summary>True when this executable runs from a system install location.</summary>
    public static bool IsInstalled()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            var dir = string.IsNullOrEmpty(exePath) ? null : Path.GetDirectoryName(exePath);
            return !string.IsNullOrEmpty(dir) && IsSystemLocation(dir);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSystemLocation(string dir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (var folder in new[]
                     {
                         Environment.SpecialFolder.ProgramFiles,
                         Environment.SpecialFolder.ProgramFilesX86,
                     })
            {
                var root = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(root) &&
                    dir.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return dir.Contains("/Applications/", StringComparison.Ordinal);
        }

        // Linux and similar: system packages (.deb/.rpm) install under /usr; some tools under /opt.
        return dir.StartsWith("/usr/", StringComparison.Ordinal) ||
               dir.StartsWith("/opt/", StringComparison.Ordinal);
    }
}
