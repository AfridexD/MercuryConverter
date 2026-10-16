using Microsoft.Win32;
using Mercury.Converter.Core;

namespace Mercury.Converter.App.Services;

/// <summary>
/// Adds a "Mercury Converter" submenu to File Explorer's right-click menu, with one "To Format" line per format.
/// It writes to HKEY_CURRENT_USER only, so no administrator rights are needed.
/// On Windows 11 the submenu is in the classic menu: "Show more options", or Shift + right-click.
/// </summary>
public static class ShellIntegration
{
    private const string MenuName = "MercuryConverter";
    private const string MenuLabel = "Mercury Converter";
    private const string ClassesRoot = @"Software\Classes\SystemFileAssociations";

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.mp4\shell\{MenuName}");
        return key is not null;
    }

    /// <summary>True when the menu is on but its entries are out of date: written by an older build, or pointing at another folder.</summary>
    public static bool NeedsRepair(string exePath)
    {
        if (!IsInstalled())
        {
            return false;
        }

        var entry = ShellMenu.For(MediaKind.Video).FirstOrDefault();
        if (entry is null)
        {
            return false;
        }

        using var command = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\.mp4\shell\{MenuName}\shell\{entry.VerbKey}\command");
        return command?.GetValue(string.Empty) as string != entry.CommandLine(exePath);
    }

    /// <summary>Writes the menu entries. Safe to run again: existing entries are replaced.</summary>
    public static void Install(string exePath)
    {
        Remove();

        // Each file extension gets its own menu, so it works even for types Windows does not label as video or image.
        foreach (var (extension, kind) in InputFormats.All)
        {
            var entries = ShellMenu.For(kind);
            if (entries.Count == 0)
            {
                continue;
            }

            using var parent = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\.{extension}\shell\{MenuName}");
            parent.SetValue("MUIVerb", MenuLabel);
            parent.SetValue("SubCommands", string.Empty);
            parent.SetValue("Icon", $"\"{exePath}\",0");
            parent.SetValue("MultiSelectModel", "Player");

            foreach (var entry in entries)
            {
                using var item = parent.CreateSubKey($@"shell\{entry.VerbKey}");
                item.SetValue("MUIVerb", entry.Label);
                item.SetValue("MultiSelectModel", "Player");
                using var command = item.CreateSubKey("command");
                command.SetValue(string.Empty, entry.CommandLine(exePath));
            }
        }
    }

    /// <summary>Removes the menu entries, including the ones older versions wrote.</summary>
    public static void Remove()
    {
        foreach (var (extension, _) in InputFormats.All)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\.{extension}\shell\{MenuName}", throwOnMissingSubKey: false);
        }

        // Version 1.0 registered by file type instead of by extension.
        foreach (var type in new[] { "image", "video", "audio" })
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{type}\shell\{MenuName}", throwOnMissingSubKey: false);
        }
    }
}
