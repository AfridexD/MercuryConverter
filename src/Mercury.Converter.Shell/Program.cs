using System.Runtime.InteropServices;
using Mercury.Converter.Core;
using Mercury.Converter.Shell;

// Windows starts this program with -Embedding when File Explorer needs the "Mercury Converter" right-click menu.
// It answers while it is being used, then quits on its own.
// "--write-manifest <file>" is used when the Store package is built: it writes the part of the package manifest that
// declares this menu, so the manifest and the code always agree.
var manifestAt = Array.FindIndex(args, argument => string.Equals(argument, "--write-manifest", StringComparison.OrdinalIgnoreCase));
if (manifestAt >= 0 && manifestAt + 1 < args.Length)
{
    File.WriteAllText(args[manifestAt + 1], StoreMenu.BuildExtensions());
    return 0;
}

if (!args.Any(argument => string.Equals(argument, StoreMenu.ServerArgument, StringComparison.OrdinalIgnoreCase)))
{
    return 0;
}

const uint MultiThreaded = 0;
_ = Native.CoInitializeEx(IntPtr.Zero, MultiThreaded);

var cookies = new List<uint>();
foreach (var (kind, clsid, _, _) in StoreMenu.Menus)
{
    var unknown = Wrapping.Wrappers.GetOrCreateComInterfaceForObject(new MenuFactory(kind, clsid), CreateComInterfaceFlags.None);
    var result = Native.CoRegisterClassObject(in clsid, unknown, Native.LocalServer, Native.MultipleUse, out var cookie);
    Marshal.Release(unknown);
    if (result == Native.Ok)
    {
        cookies.Add(cookie);
    }
}

Activity.Touch();

// Wait for File Explorer. Stay a little while after the last request, because it asks several times for one menu.
while (Activity.IdleSeconds < 60)
{
    Thread.Sleep(500);
}

foreach (var cookie in cookies)
{
    _ = Native.CoRevokeClassObject(cookie);
}

Native.CoUninitialize();
return 0;
