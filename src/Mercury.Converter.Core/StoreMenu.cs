using System.Globalization;
using System.Security;
using System.Text;

namespace Mercury.Converter.Core;

/// <summary>
/// The right-click menu of the Microsoft Store version. A Store app cannot write to the registry for Explorer, so the
/// menu is declared in the package itself: for each kind of file, a few "To Format" lines. Windows shows them in the
/// classic right-click menu ("Show more options" on Windows 11). The installed version offers the full list.
/// </summary>
public static class StoreMenu
{
    /// <summary>The lines kept in the Store version. A short list stays tidy, because package verbs cannot be put in a submenu.</summary>
    private static readonly (MediaKind Kind, string Group, TargetFormat[] Targets)[] Plan =
    [
        (MediaKind.Image, "mercuryphoto", [TargetFormat.WebP, TargetFormat.Jpeg, TargetFormat.Png]),
        (MediaKind.Animation, "mercuryanimation", [TargetFormat.Mp4, TargetFormat.WebM]),
        (MediaKind.Video, "mercuryvideo", [TargetFormat.Mp4, TargetFormat.Mp3, TargetFormat.Gif]),
        (MediaKind.Audio, "mercuryaudio", [TargetFormat.Mp3, TargetFormat.Aac, TargetFormat.Flac]),
    ];

    /// <summary>
    /// The <c>uap3:Extension</c> elements for the package manifest, one per kind of file. The manifest must declare the
    /// <c>uap</c>, <c>uap2</c> and <c>uap3</c> prefixes.
    /// </summary>
    public static string BuildExtensions()
    {
        var xml = new StringBuilder();
        foreach (var (kind, group, targets) in Plan)
        {
            var extensions = InputFormats.All.Where(pair => pair.Kind == kind).Select(pair => pair.Extension).Distinct().OrderBy(e => e, StringComparer.Ordinal).ToList();
            if (extensions.Count == 0)
            {
                continue;
            }

            xml.AppendLine("        <uap3:Extension Category=\"windows.fileTypeAssociation\">");
            xml.AppendLine(CultureInfo.InvariantCulture, $"          <uap3:FileTypeAssociation Name=\"{group}\">");
            xml.AppendLine("            <uap:SupportedFileTypes>");
            foreach (var extension in extensions)
            {
                xml.AppendLine(CultureInfo.InvariantCulture, $"              <uap:FileType>.{extension}</uap:FileType>");
            }

            xml.AppendLine("            </uap:SupportedFileTypes>");
            xml.AppendLine("            <uap2:SupportedVerbs>");
            foreach (var target in targets)
            {
                var entry = ShellMenu.For(kind).First(e => e.Target == target && e.Quality is null);
                var extension = Targets.Get(target).Extension;
                var parameters = SecurityElement.Escape($"--convert {extension} \"%1\"");
                xml.AppendLine(CultureInfo.InvariantCulture, $"              <uap3:Verb Id=\"to{extension}\" Parameters=\"{parameters}\" MultiSelectModel=\"Player\">{SecurityElement.Escape(entry.Label)}</uap3:Verb>");
            }

            xml.AppendLine("            </uap2:SupportedVerbs>");
            xml.AppendLine("          </uap3:FileTypeAssociation>");
            xml.AppendLine("        </uap3:Extension>");
        }

        return xml.ToString();
    }
}
