namespace BoltZip.Core.Compression;

/// <summary>Expands input paths (files and directories) into archive entry keys.</summary>
internal static class InputScanner
{
    public static List<(string Key, string FullPath)> EnumerateFiles(IReadOnlyList<string> inputs)
    {
        var results = new List<(string, string)>();

        foreach (var input in inputs)
        {
            var full = Path.GetFullPath(input);

            if (Directory.Exists(full))
            {
                var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var baseDir = Path.GetDirectoryName(trimmed) ?? trimmed;

                foreach (var file in Directory.EnumerateFiles(trimmed, "*", SearchOption.AllDirectories))
                {
                    results.Add((Path.GetRelativePath(baseDir, file).Replace('\\', '/'), file));
                }
            }
            else if (File.Exists(full))
            {
                results.Add((Path.GetFileName(full), full));
            }
            else
            {
                throw new FileNotFoundException($"Input not found: {input}");
            }
        }

        return results;
    }
}
