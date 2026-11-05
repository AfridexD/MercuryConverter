using System.Runtime.InteropServices;
using System.Text;

namespace Mercury.Converter.App.Services;

/// <summary>
/// Tells whether Mercury is running from the Microsoft Store (an MSIX package) or as an ordinary installed program.
/// The Store version differs in three small ways: Windows adds the right-click menu from the package, Windows updates
/// the app itself, and notifications use the package's own identity.
/// </summary>
public static class PackageInfo
{
    /// <summary>The error Windows gives when the program has no package.</summary>
    private const int NoPackage = 15700;

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            uint length = 0;
            return GetCurrentPackageFullName(ref length, null) != NoPackage;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder? name);
}
