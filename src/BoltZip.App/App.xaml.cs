using System.IO;
using System.Linq;
using System.Windows;

namespace BoltZip.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startup = StartupAction.Parse(e.Args);
        var window = new MainWindow(startup);
        window.Show();
    }
}

/// <summary>An action requested via the command line (e.g. from the shell context menu).</summary>
public sealed record StartupAction(StartupMode Mode, string? Path)
{
    public static StartupAction Parse(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--compress" or "-c":
                    return new StartupAction(StartupMode.Compress, i + 1 < args.Length ? args[i + 1] : null);
                case "--extract" or "-x":
                    return new StartupAction(StartupMode.Extract, i + 1 < args.Length ? args[i + 1] : null);
            }
        }

        // Bare path argument (e.g. a file dropped on the exe): infer intent from its extension.
        var pathArg = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (pathArg is not null)
        {
            var mode = File.Exists(pathArg) &&
                       BoltZip.Core.Compression.FormatInfo.DetectFromPath(pathArg).Format
                           is not BoltZip.Core.Compression.ArchiveFormat.Unknown
                ? StartupMode.Extract
                : StartupMode.Compress;
            return new StartupAction(mode, pathArg);
        }

        return new StartupAction(StartupMode.Compress, null);
    }
}

public enum StartupMode
{
    Compress,
    Extract,
}

