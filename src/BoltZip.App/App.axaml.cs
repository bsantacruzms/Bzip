using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BoltZip.Core.Compression;

namespace BoltZip.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow(StartupAction.Parse(desktop.Args ?? Array.Empty<string>()));
        }

        base.OnFrameworkInitializationCompleted();
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

        var pathArg = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (pathArg is not null)
        {
            var mode = File.Exists(pathArg) &&
                       FormatInfo.DetectFromPath(pathArg).Format is not ArchiveFormat.Unknown
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
