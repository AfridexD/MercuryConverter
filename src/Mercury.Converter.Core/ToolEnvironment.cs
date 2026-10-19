using System.Diagnostics;

namespace Mercury.Converter.Core;

/// <summary>Settings every converter program is started with.</summary>
public static class ToolEnvironment
{
    /// <summary>
    /// ImageMagick is told to read its security policy from its own folder, where Mercury's policy is. Without this it
    /// would use whatever policy a separate ImageMagick install on the computer has, or none at all.
    /// </summary>
    public static void Apply(ProcessStartInfo startInfo, Tool tool, string toolPath)
    {
        if (tool == Tool.ImageMagick && Path.GetDirectoryName(toolPath) is { Length: > 0 } folder)
        {
            startInfo.Environment["MAGICK_CONFIGURE_PATH"] = folder;
        }
    }
}
