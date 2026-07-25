using System.Text;
using BoltZip.Core.Compression;
using BoltZip.Core.Hardware;
using BoltZip.Core.Infrastructure;

namespace BoltZip.Cli;

internal static class Program
{
    private static readonly ArchiveService Service = new();

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var (positionals, options) = ParseOptions(args.Skip(1));

        try
        {
            return command switch
            {
                "help" or "-h" or "--help" => PrintHelp(),
                "version" or "--version" => PrintVersion(),
                "hw" or "hardware" => await ShowHardwareAsync(),
                "detect" => Detect(positionals),
                "list" or "l" => await ListAsync(positionals, options),
                "create" or "c" or "add" or "a" => await CreateAsync(positionals, options),
                "extract" or "x" or "e" => await ExtractAsync(positionals, options),
                "install-context" => InstallContext(options),
                "uninstall-context" => UninstallContext(),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CreateAsync(List<string> positionals, Dictionary<string, string?> options)
    {
        if (positionals.Count < 2)
        {
            Console.Error.WriteLine("Usage: bz create <output-archive> <input...> [--goal fast|balanced|max] [-p [password]]");
            return 2;
        }

        var output = positionals[0];
        var inputs = positionals.Skip(1).ToList();
        var goal = ParseGoal(options);
        var password = ResolvePassword(options, confirm: true);
        var quiet = options.ContainsKey("quiet");

        var request = new CreateRequest
        {
            OutputPath = output,
            Inputs = inputs,
            Password = password,
            Goal = goal,
        };

        if (!quiet)
        {
            Console.WriteLine("Analyzing hardware and planning\u2026");
            var plan = await Service.PlanAsync(request);
            PrintPlan(plan);
            Console.WriteLine();
        }

        var result = await Service.CreateAsync(request, MakeProgress(quiet));
        FinishProgressLine(quiet);

        var size = new FileInfo(result.OutputPath).Length;
        Console.WriteLine($"Created {result.OutputPath} ({FormatBytes(size)})");
        return 0;
    }

    private static async Task<int> ExtractAsync(List<string> positionals, Dictionary<string, string?> options)
    {
        if (positionals.Count < 1)
        {
            Console.Error.WriteLine("Usage: bz extract <archive> [--out <dir>] [-y] [-p [password]]");
            return 2;
        }

        var archive = positionals[0];
        var outputDir = options.TryGetValue("out", out var o) && o is not null ? o : Directory.GetCurrentDirectory();
        var overwrite = options.ContainsKey("overwrite");
        var quiet = options.ContainsKey("quiet");
        var explicitPassword = options.ContainsKey("password");
        var password = explicitPassword ? ResolvePassword(options, confirm: false) : null;

        var request = new ExtractRequest
        {
            ArchivePath = archive,
            OutputDirectory = outputDir,
            Password = password,
            Overwrite = overwrite,
        };

        try
        {
            await Service.ExtractAsync(request, MakeProgress(quiet));
        }
        catch (InvalidOperationException) when (!explicitPassword)
        {
            // Encrypted archive without a supplied password: prompt and retry once.
            var pw = PromptPassword("Password: ");
            request = request with { Password = pw };
            await Service.ExtractAsync(request, MakeProgress(quiet));
        }

        FinishProgressLine(quiet);
        Console.WriteLine($"Extracted to {Path.GetFullPath(outputDir)}");
        return 0;
    }

    private static async Task<int> ListAsync(List<string> positionals, Dictionary<string, string?> options)
    {
        if (positionals.Count < 1)
        {
            Console.Error.WriteLine("Usage: bz list <archive> [-p [password]]");
            return 2;
        }

        var archive = positionals[0];
        var explicitPassword = options.ContainsKey("password");
        var password = explicitPassword ? ResolvePassword(options, confirm: false) : null;

        IReadOnlyList<ArchiveEntryInfo> entries;
        try
        {
            entries = await Service.ListAsync(archive, password);
        }
        catch (InvalidOperationException) when (!explicitPassword)
        {
            var pw = PromptPassword("Password: ");
            entries = await Service.ListAsync(archive, pw);
        }

        Console.WriteLine($"{"Size",12}  {"Ratio",6}  Name");
        foreach (var entry in entries.Where(e => !e.IsDirectory))
        {
            var ratio = entry.Size > 0 && entry.CompressedSize > 0 ? $"{entry.Ratio * 100:0}%" : "-";
            Console.WriteLine($"{FormatBytes(entry.Size),12}  {ratio,6}  {entry.Path}");
        }

        var fileCount = entries.Count(e => !e.IsDirectory);
        var totalBytes = entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
        Console.WriteLine($"{fileCount} file(s), {FormatBytes(totalBytes)} uncompressed");
        return 0;
    }

    private static int Detect(List<string> positionals)
    {
        if (positionals.Count < 1)
        {
            Console.Error.WriteLine("Usage: bz detect <file>");
            return 2;
        }

        var descriptor = FormatInfo.DetectFromPath(positionals[0]);
        Console.WriteLine($"Format:      {descriptor.Format}");
        Console.WriteLine($"Tar-wrapped: {descriptor.TarWrapped}");
        Console.WriteLine($"Can create:  {FormatInfo.CanCreate(descriptor.Format)}");
        Console.WriteLine($"Can extract: {FormatInfo.CanExtract(descriptor.Format)}");
        return 0;
    }

    private static async Task<int> ShowHardwareAsync()
    {
        var profile = await HardwareProbe.DetectAsync();
        Console.WriteLine("Hardware profile");
        Console.WriteLine($"  {profile.Summary()}");
        Console.WriteLine($"  Architecture:  {profile.Architecture}");
        Console.WriteLine($"  Logical cores: {profile.LogicalCores}");
        Console.WriteLine($"  Memory:        {profile.TotalMemoryGiB:0.#} GiB total, {profile.AvailableMemoryGiB:0.#} GiB available");
        Console.WriteLine($"  System drive:  {profile.SystemStorage}");
        Console.WriteLine($"  Hardware AES:  {profile.SupportsHardwareAes}");
        Console.WriteLine($"  SIMD:          {(profile.SupportsAvx512 ? "AVX-512" : profile.SupportsAvx2 ? "AVX2" : "baseline")}");
        if (profile.Gpus.Count > 0)
        {
            Console.WriteLine("  GPUs:");
            foreach (var gpu in profile.Gpus)
            {
                Console.WriteLine($"    - {gpu.Name} ({gpu.Vendor}){(gpu.IsDiscrete ? " [discrete]" : string.Empty)}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Recommended for .bz (Balanced):");
        var plan = OptimizationPlanner.Plan(profile, OptimizationGoal.Balanced, ArchiveFormat.Bz, 1L << 30);
        PrintPlan(plan);
        return 0;
    }

    private static int InstallContext(Dictionary<string, string?> options)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Shell integration is only available on Windows.");
            return 1;
        }

        var appPath = options.TryGetValue("app", out var a) && a is not null ? a : FindGuiExecutable();
        if (appPath is null || !File.Exists(appPath))
        {
            Console.Error.WriteLine("Could not locate the BoltZip GUI (BoltZipTool.exe). Pass --app <path>.");
            return 1;
        }

        ShellIntegration.Install(appPath);
        Console.WriteLine($"Added BoltZip to the right-click menu (points to {appPath}).");
        Console.WriteLine("On Windows 11 look under \"Show more options\".");
        return 0;
    }

    private static int UninstallContext()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Shell integration is only available on Windows.");
            return 1;
        }

        ShellIntegration.Uninstall();
        Console.WriteLine("Removed BoltZip from the right-click menu.");
        return 0;
    }

    private static string? FindGuiExecutable()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "BoltZipTool.exe");
        return File.Exists(candidate) ? candidate : null;
    }

    private static void PrintPlan(CompressionPlan plan)
    {
        Console.WriteLine($"  Goal: {plan.Goal} | level {plan.Level} | {plan.WorkerThreads} thread(s) | " +
                          $"window {plan.WindowMiB:0.#} MiB | buffer {plan.BufferBytes / 1024} KiB | " +
                          $"LDM {(plan.LongDistanceMatching ? "on" : "off")}");
        foreach (var reason in plan.Rationale)
        {
            Console.WriteLine($"    - {reason}");
        }
    }

    private static OptimizationGoal ParseGoal(Dictionary<string, string?> options)
    {
        if (!options.TryGetValue("goal", out var value) || value is null)
        {
            return OptimizationGoal.Balanced;
        }

        return value.ToLowerInvariant() switch
        {
            "fast" or "speed" or "fastest" => OptimizationGoal.MaxSpeed,
            "max" or "maximum" or "ratio" or "small" or "smallest" => OptimizationGoal.MaxRatio,
            _ => OptimizationGoal.Balanced,
        };
    }

    private static string? ResolvePassword(Dictionary<string, string?> options, bool confirm)
    {
        if (!options.TryGetValue("password", out var value))
        {
            return null;
        }

        if (value is not null)
        {
            return value;
        }

        var password = PromptPassword("Password: ");
        if (confirm)
        {
            var again = PromptPassword("Confirm password: ");
            if (!string.Equals(password, again, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Passwords did not match.");
            }
        }

        return password;
    }

    private static string PromptPassword(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        var builder = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0)
                {
                    builder.Length--;
                    Console.Write("\b \b");
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
                Console.Write('*');
            }
        }

        return builder.ToString();
    }

    private static IProgress<ArchiveProgress> MakeProgress(bool quiet)
    {
        if (quiet)
        {
            return new Progress<ArchiveProgress>(_ => { });
        }

        var lastPercent = -1;
        return new Progress<ArchiveProgress>(p =>
        {
            var percent = (int)p.Percent;
            if (percent == lastPercent)
            {
                return;
            }

            lastPercent = percent;
            Console.Write($"\r{p.Phase,-12} {percent,3}%   ");
        });
    }

    private static void FinishProgressLine(bool quiet)
    {
        if (!quiet)
        {
            Console.WriteLine();
        }
    }

    private static (List<string> Positionals, Dictionary<string, string?> Options) ParseOptions(IEnumerable<string> tokens)
    {
        var positionals = new List<string>();
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var list = tokens.ToList();

        for (var i = 0; i < list.Count; i++)
        {
            var token = list[i];
            if (!token.StartsWith('-'))
            {
                positionals.Add(token);
                continue;
            }

            var name = NormalizeOption(token);
            switch (name)
            {
                case "password":
                    if (i + 1 < list.Count && !list[i + 1].StartsWith('-'))
                    {
                        options[name] = list[++i];
                    }
                    else
                    {
                        options[name] = null;
                    }

                    break;
                case "goal":
                case "out":
                case "format":
                case "app":
                    if (i + 1 < list.Count)
                    {
                        options[name] = list[++i];
                    }

                    break;
                default:
                    options[name] = "true";
                    break;
            }
        }

        return (positionals, options);
    }

    private static string NormalizeOption(string token)
    {
        var name = token.TrimStart('-').ToLowerInvariant();
        return name switch
        {
            "p" => "password",
            "g" => "goal",
            "o" => "out",
            "f" => "format",
            "y" or "yes" => "overwrite",
            "q" => "quiet",
            _ => name,
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KiB", "MiB", "GiB", "TiB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp();
        return 2;
    }

    private static int PrintVersion()
    {
        Console.WriteLine("BoltZip (bz) 1.0.0");
        return 0;
    }

    private static int PrintHelp()
    {
        Console.WriteLine(
            """
            BoltZip (bz) - a modern, hardware-optimized archiver

            Usage:
              bz create <output> <input...> [--goal fast|balanced|max] [-p [password]] [-q]
              bz extract <archive> [--out <dir>] [-y] [-p [password]] [-q]
              bz list <archive> [-p [password]]
              bz detect <file>
              bz hw
              bz install-context [--app <BoltZipTool.exe>]
              bz uninstall-context

            Formats:
              Create : .bz (native, encrypted)  .zip  .tar  .gz  .bz2  .zst  .br
              Extract: all of the above plus .7z .rar .xz .lz .arj

            Options:
              -p, --password [pw]  Encrypt (.bz only) or decrypt. Omit value to be prompted.
              -g, --goal <g>       fast | balanced (default) | max
              -o, --out <dir>      Extraction output directory
              -y, --yes            Overwrite existing files
              -q, --quiet          Suppress progress and planning output

            Encryption (.bz): XChaCha20-Poly1305 with Argon2id key derivation.
            Compression is auto-tuned to your CPU, RAM and storage. Run 'bz hw' to preview.

            BoltZip, free and open source: https://github.com/bsantacruzms/Bzip
            """);
        return 0;
    }
}
