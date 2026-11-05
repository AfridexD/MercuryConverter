using System.Globalization;
using System.Text;

namespace Mercury.Converter.Core;

/// <summary>
/// How the Microsoft Store version appears in File Explorer. A Store app cannot add its own submenu to the right-click menu
/// the way the installed program does, so the package lists the file types Mercury can read. Windows then offers Mercury
/// under "Open with", and choosing it adds the file to Mercury's list. It never becomes the default program for a file type.
/// </summary>
public static class StoreMenu
{
    /// <summary>One group per kind of file. The names are fixed by the package format: lowercase letters only.</summary>
    private static readonly (MediaKind Kind, string Group)[] Plan =
    [
        (MediaKind.Image, "mercuryphoto"),
        (MediaKind.Animation, "mercuryanimation"),
        (MediaKind.Video, "mercuryvideo"),
        (MediaKind.Audio, "mercuryaudio"),
    ];

    /// <summary>
    /// The <c>uap3:Extension</c> elements for the package manifest, one per kind of file. The manifest must declare the
    /// <c>uap</c> and <c>uap3</c> prefixes.
    /// </summary>
    public static string BuildExtensions()
    {
        var xml = new StringBuilder();
        foreach (var (kind, group) in Plan)
        {
            var extensions = InputFormats.All.Where(pair => pair.Kind == kind).Select(pair => pair.Extension).Distinct().OrderBy(e => e, StringComparer.Ordinal).ToList();
            if (extensions.Count == 0)
            {
                continue;
            }

            xml.AppendLine("        <uap3:Extension Category=\"windows.fileTypeAssociation\">");
            xml.AppendLine(CultureInfo.InvariantCulture, $"          <uap3:FileTypeAssociation Name=\"{group}\" Parameters=\"&quot;%1&quot;\" MultiSelectModel=\"Player\">");
            xml.AppendLine("            <uap:SupportedFileTypes>");
            foreach (var extension in extensions)
            {
                xml.AppendLine(CultureInfo.InvariantCulture, $"              <uap:FileType>.{extension}</uap:FileType>");
            }

            xml.AppendLine("            </uap:SupportedFileTypes>");
            xml.AppendLine("          </uap3:FileTypeAssociation>");
            xml.AppendLine("        </uap3:Extension>");
        }

        return xml.ToString();
    }
}
