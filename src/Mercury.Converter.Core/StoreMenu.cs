using System.Globalization;
using System.Text;

namespace Mercury.Converter.Core;

/// <summary>
/// The right-click menu of the Microsoft Store version. A Store app cannot write the menu into the registry the way the
/// installed program does, so the package declares it: for each kind of file a small helper program
/// (<c>MercuryShell.exe</c>) that File Explorer asks for the "Mercury Converter" submenu. This builds the part of the
/// package manifest that says so. The manifest must declare the <c>desktop4</c>, <c>desktop5</c> and <c>com</c> prefixes.
/// </summary>
public static class StoreMenu
{
    /// <summary>The helper program inside the package.</summary>
    public const string HelperExecutable = "MercuryShell.exe";

    /// <summary>The argument that makes Windows start the helper as a COM server.</summary>
    public const string ServerArgument = "-Embedding";

    /// <summary>One menu per kind of file. The ids are fixed: Windows remembers them.</summary>
    public static IReadOnlyList<(MediaKind Kind, Guid Clsid, string Verb, string Name)> Menus { get; } =
    [
        (MediaKind.Image, new Guid("7C1B2E10-6A4F-4D0B-9C57-2E3F1A8D5B01"), "MercuryPhoto", "Mercury Converter photo menu"),
        (MediaKind.Animation, new Guid("7C1B2E10-6A4F-4D0B-9C57-2E3F1A8D5B02"), "MercuryAnimation", "Mercury Converter GIF menu"),
        (MediaKind.Video, new Guid("7C1B2E10-6A4F-4D0B-9C57-2E3F1A8D5B03"), "MercuryVideo", "Mercury Converter video menu"),
        (MediaKind.Audio, new Guid("7C1B2E10-6A4F-4D0B-9C57-2E3F1A8D5B04"), "MercuryAudio", "Mercury Converter audio menu"),
    ];

    /// <summary>The menu for a kind of file.</summary>
    public static (MediaKind Kind, Guid Clsid, string Verb, string Name) For(MediaKind kind) => Menus.First(menu => menu.Kind == kind);

    /// <summary>The manifest elements (two <c>Extension</c> elements) that go inside the application's <c>Extensions</c>.</summary>
    public static string BuildExtensions()
    {
        var xml = new StringBuilder();

        // Which file types get which menu.
        xml.AppendLine("        <desktop4:Extension Category=\"windows.fileExplorerContextMenus\">");
        xml.AppendLine("          <desktop4:FileExplorerContextMenus>");
        foreach (var (kind, clsid, verb, _) in Menus)
        {
            foreach (var extension in Extensions(kind))
            {
                xml.AppendLine(CultureInfo.InvariantCulture, $"            <desktop5:ItemType Type=\".{extension}\">");
                xml.AppendLine(CultureInfo.InvariantCulture, $"              <desktop5:Verb Id=\"{verb}\" Clsid=\"{clsid.ToString("D").ToUpperInvariant()}\" />");
                xml.AppendLine("            </desktop5:ItemType>");
            }
        }

        xml.AppendLine("          </desktop4:FileExplorerContextMenus>");
        xml.AppendLine("        </desktop4:Extension>");

        // The helper program that answers File Explorer.
        xml.AppendLine("        <com:Extension Category=\"windows.comServer\">");
        xml.AppendLine("          <com:ComServer>");
        xml.AppendLine(CultureInfo.InvariantCulture, $"            <com:ExeServer Executable=\"{HelperExecutable}\" Arguments=\"{ServerArgument}\" DisplayName=\"Mercury Converter menu\">");
        foreach (var (_, clsid, _, name) in Menus)
        {
            xml.AppendLine(CultureInfo.InvariantCulture, $"              <com:Class Id=\"{clsid.ToString("D").ToUpperInvariant()}\" DisplayName=\"{name}\" />");
        }

        xml.AppendLine("            </com:ExeServer>");
        xml.AppendLine("          </com:ComServer>");
        xml.AppendLine("        </com:Extension>");
        return xml.ToString();
    }

    /// <summary>The file types of a kind, in a fixed order.</summary>
    public static IReadOnlyList<string> Extensions(MediaKind kind) =>
        InputFormats.All.Where(pair => pair.Kind == kind).Select(pair => pair.Extension).Distinct().OrderBy(e => e, StringComparer.Ordinal).ToList();
}
