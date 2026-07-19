using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BoltZip.Core.Infrastructure;

/// <summary>
/// Registers/unregisters a cascading "BoltZip" submenu in the Windows right-click menu
/// (per-user, <c>HKCU\Software\Classes</c>, no admin required). Used by the CLI
/// <c>bz install-context</c>. The MSI installer registers the equivalent machine-wide menu.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShellIntegration
{
    private const string ClassesRoot = @"Software\Classes";
    private const string FileMenuKey = "BoltZip.FileMenu";
    private const string DirMenuKey = "BoltZip.DirMenu";

    public static void Install(string appExePath)
    {
        var icon = $"\"{appExePath}\",0";
        var compress = $"\"{appExePath}\" --compress \"%1\"";
        var extract = $"\"{appExePath}\" --extract \"%1\"";

        // Files: BoltZip -> (Add to archive / Extract)
        WriteCascadeRoot(@"*\shell\BoltZip", FileMenuKey, icon);
        WriteSubCommand($@"{FileMenuKey}\shell\01_Compress", "Add to BoltZip archive", compress, icon);
        WriteSubCommand($@"{FileMenuKey}\shell\02_Extract", "Extract with BoltZip", extract, icon);

        // Folders: BoltZip -> (Add to archive)
        WriteCascadeRoot(@"Directory\shell\BoltZip", DirMenuKey, icon);
        WriteSubCommand($@"{DirMenuKey}\shell\01_Compress", "Add to BoltZip archive", compress, icon);
    }

    public static void Uninstall()
    {
        DeleteKey(@"*\shell\BoltZip");
        DeleteKey(@"Directory\shell\BoltZip");
        DeleteKey(FileMenuKey);
        DeleteKey(DirMenuKey);
    }

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\*\shell\BoltZip");
        return key is not null;
    }

    private static void WriteCascadeRoot(string classesRelativePath, string subCommandsKey, string icon)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{classesRelativePath}");
        key.SetValue("MUIVerb", "BoltZip");
        key.SetValue("Icon", icon);
        key.SetValue("ExtendedSubCommandsKey", subCommandsKey);
    }

    private static void WriteSubCommand(string classesRelativePath, string label, string command, string icon)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{classesRelativePath}");
        key.SetValue("MUIVerb", label);
        key.SetValue("Icon", icon);
        using var commandKey = key.CreateSubKey("command");
        commandKey.SetValue(null, command);
    }

    private static void DeleteKey(string classesRelativePath)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{classesRelativePath}", throwOnMissingSubKey: false);
        }
        catch
        {
            // best-effort removal
        }
    }
}
