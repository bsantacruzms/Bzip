using BoltZip.Core.Infrastructure;

namespace BoltZip.Core.Hardware;

/// <summary>
/// Best-effort detection of what kind of storage backs a given path. Falls back to
/// <see cref="StorageKind.Ssd"/> for fixed drives that cannot be classified, since
/// solid-state is the sensible modern default.
/// </summary>
public static class StorageProbe
{
    public static async Task<StorageKind> GetKindAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var full = Path.GetFullPath(path);

            // UNC paths (\\server\share) are network storage.
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return StorageKind.Network;
            }

            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
            {
                return StorageKind.Unknown;
            }

            DriveInfo drive;
            try
            {
                drive = new DriveInfo(root);
            }
            catch
            {
                return StorageKind.Unknown;
            }

            switch (drive.DriveType)
            {
                case DriveType.Network:
                    return StorageKind.Network;
                case DriveType.Removable:
                    return StorageKind.Removable;
                case DriveType.CDRom:
                    return StorageKind.Optical;
                case DriveType.Ram:
                    return StorageKind.Ram;
                case DriveType.Fixed:
                    break;
                default:
                    return StorageKind.Unknown;
            }

            if (!OperatingSystem.IsWindows())
            {
                // On non-Windows we cannot cheaply classify; assume SSD.
                return StorageKind.Ssd;
            }

            var letter = char.ToUpperInvariant(root[0]);
            if (!char.IsLetter(letter))
            {
                return StorageKind.Ssd;
            }

            var script =
                "$ErrorActionPreference='Stop'; try { " +
                $"$n=(Get-Partition -DriveLetter '{letter}').DiskNumber; " +
                "$pd=Get-PhysicalDisk | Where-Object { $_.DeviceId -eq [string]$n }; " +
                "if($pd.BusType -eq 'NVMe'){'Nvme'} " +
                "elseif($pd.MediaType -eq 'SSD' -or $pd.MediaType -eq 4){'Ssd'} " +
                "elseif($pd.MediaType -eq 'HDD' -or $pd.MediaType -eq 3){'Hdd'} " +
                "else {'Ssd'} } catch { 'Unknown' }";

            var output = await ProcessRunner.RunAsync(
                "powershell.exe",
                $"-NoProfile -NonInteractive -Command \"{script}\"",
                cancellationToken,
                timeoutMs: 6000).ConfigureAwait(false);

            return output.Trim() switch
            {
                "Nvme" => StorageKind.Nvme,
                "Ssd" => StorageKind.Ssd,
                "Hdd" => StorageKind.Hdd,
                _ => StorageKind.Ssd,
            };
        }
        catch
        {
            return StorageKind.Unknown;
        }
    }
}
