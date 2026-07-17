using System.Runtime.Versioning;
using Microsoft.Win32;

namespace BoltZip.Core.Infrastructure;

/// <summary>
/// Registers/unregisters BoltZip verbs in the Windows right-click menu. Writes only to
/// <c>HKCU\Software\Classes</c> so no administrator rights are required. On Windows 11 the
/// entries appear under "Show more options" (the classic menu).
/// </summary>
[SupportedOSPlatform("windows")]
public static class ShellIntegration
{
    private const string CompressVerb = "BoltZip.Compress";
    private const string ExtractVerb = "BoltZip.Extract";
    private const string ClassesRoot = @"Software\Classes";

    public static void Install(string appExePath)
    {
        var compress = $"\"{appExePath}\" --compress \"%1\"";
        var extract = $"\"{appExePath}\" --extract \"%1\"";

        RegisterVerb($@"*\shell\{CompressVerb}", "Add to BoltZip archive\u2026", compress, appExePath);
        RegisterVerb($@"*\shell\{ExtractVerb}", "Extract with BoltZip", extract, appExePath);
        RegisterVerb($@"Directory\shell\{CompressVerb}", "Add to BoltZip archive\u2026", compress, appExePath);
    }

    public static void Uninstall()
    {
        DeleteVerb($@"*\shell\{CompressVerb}");
        DeleteVerb($@"*\shell\{ExtractVerb}");
        DeleteVerb($@"Directory\shell\{CompressVerb}");
    }

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\*\shell\{CompressVerb}");
        return key is not null;
    }

    private static void RegisterVerb(string classesRelativePath, string label, string command, string iconPath)
    {
        using var shellKey = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{classesRelativePath}");
        shellKey.SetValue("MUIVerb", label);
        shellKey.SetValue("Icon", $"\"{iconPath}\",0");

        using var commandKey = shellKey.CreateSubKey("command");
        commandKey.SetValue(null, command);
    }

    private static void DeleteVerb(string classesRelativePath)
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
