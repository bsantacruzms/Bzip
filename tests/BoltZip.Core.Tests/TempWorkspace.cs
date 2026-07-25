namespace BoltZip.Core.Tests;

/// <summary>A disposable temporary directory for round-trip tests.</summary>
public sealed class TempWorkspace : IDisposable
{
    public string Root { get; }

    public TempWorkspace()
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "boltzip-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string CreateDir(string name)
    {
        var path = System.IO.Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public string WriteFile(string relativePath, string content)
    {
        var path = System.IO.Path.Combine(Root, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string WriteFile(string relativePath, byte[] content)
    {
        var path = System.IO.Path.Combine(Root, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public string Path(string relative) => System.IO.Path.Combine(Root, relative);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
