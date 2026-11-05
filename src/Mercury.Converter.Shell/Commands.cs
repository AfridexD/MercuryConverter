using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Mercury.Converter.Core;

namespace Mercury.Converter.Shell;

/// <summary>Remembers when File Explorer last asked for something, so the helper can quit when nobody is using it.</summary>
internal static class Activity
{
    private static long lastTick = Environment.TickCount64;

    public static void Touch() => Interlocked.Exchange(ref lastTick, Environment.TickCount64);

    public static double IdleSeconds => (Environment.TickCount64 - Interlocked.Read(ref lastTick)) / 1000.0;
}

internal static class Wrapping
{
    public static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Gives out an object as a COM pointer of one interface. The caller owns the reference.</summary>
    public static IntPtr Pointer(object instance, Guid iid)
    {
        var unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
        try
        {
            return Marshal.QueryInterface(unknown, in iid, out var pointer) == Native.Ok ? pointer : IntPtr.Zero;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }
}

/// <summary>Reads the file paths out of what File Explorer says is selected.</summary>
internal static class Selection
{
    public static List<string> Paths(IShellItemArray? items)
    {
        var paths = new List<string>();
        if (items is null || items.GetCount(out var count) != Native.Ok)
        {
            return paths;
        }

        for (uint index = 0; index < count; index++)
        {
            if (items.GetItemAt(index, out var item) != Native.Ok || item is null)
            {
                continue;
            }

            if (item.GetDisplayName(Native.FileSystemPath, out var name) == Native.Ok && name != IntPtr.Zero)
            {
                var path = Marshal.PtrToStringUni(name);
                Marshal.FreeCoTaskMem(name);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }
        }

        return paths;
    }
}

/// <summary>"Mercury Converter", the line that opens the list of formats.</summary>
[GeneratedComClass]
internal sealed partial class RootCommand(MediaKind kind, Guid id) : IExplorerCommand
{
    private const uint HasSubCommands = 0x1;

    public int GetTitle(IShellItemArray? items, out IntPtr name)
    {
        Activity.Touch();
        name = Marshal.StringToCoTaskMemUni("Mercury Converter");
        return Native.Ok;
    }

    public int GetIcon(IShellItemArray? items, out IntPtr icon)
    {
        Activity.Touch();

        // The same owl as the app. Windows reads it from the package folder.
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(path))
        {
            icon = Marshal.StringToCoTaskMemUni(path);
            return Native.Ok;
        }

        icon = IntPtr.Zero;
        return Native.NotImplemented;
    }

    public int GetToolTip(IShellItemArray? items, out IntPtr tip)
    {
        tip = IntPtr.Zero;
        return Native.NotImplemented;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = id;
        return Native.Ok;
    }

    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        Activity.Touch();
        state = 0; // enabled
        return Native.Ok;
    }

    public int Invoke(IShellItemArray? items, IntPtr bindContext) => Native.Ok;

    public int GetFlags(out uint flags)
    {
        flags = HasSubCommands;
        return Native.Ok;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        Activity.Touch();
        commands = new FormatList(ShellMenu.For(kind).Select(entry => (IExplorerCommand)new FormatCommand(entry)).ToList());
        return Native.Ok;
    }
}

/// <summary>One line such as "To Webp". Choosing it starts Mercury, which converts the selected files.</summary>
[GeneratedComClass]
internal sealed partial class FormatCommand(ShellMenuEntry entry) : IExplorerCommand
{
    public int GetTitle(IShellItemArray? items, out IntPtr name)
    {
        Activity.Touch();
        name = Marshal.StringToCoTaskMemUni(entry.Label);
        return Native.Ok;
    }

    public int GetIcon(IShellItemArray? items, out IntPtr icon)
    {
        icon = IntPtr.Zero;
        return Native.NotImplemented;
    }

    public int GetToolTip(IShellItemArray? items, out IntPtr tip)
    {
        tip = IntPtr.Zero;
        return Native.NotImplemented;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Guid.Empty;
        return Native.NotImplemented;
    }

    public int GetState(IShellItemArray? items, bool okToBeSlow, out uint state)
    {
        Activity.Touch();
        state = 0;
        return Native.Ok;
    }

    public int Invoke(IShellItemArray? items, IntPtr bindContext)
    {
        Activity.Touch();
        try
        {
            var files = Selection.Paths(items);
            if (files.Count == 0)
            {
                return Native.Fail;
            }

            var program = Path.Combine(AppContext.BaseDirectory, "MercuryConverter.exe");
            foreach (var batch in ShellLaunch.Batches(entry, files))
            {
                Process.Start(new ProcessStartInfo(program, ShellLaunch.Arguments(entry, batch))
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                })?.Dispose();
            }

            return Native.Ok;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Native.Fail;
        }
    }

    public int GetFlags(out uint flags)
    {
        flags = 0;
        return Native.Ok;
    }

    public int EnumSubCommands(out IEnumExplorerCommand? commands)
    {
        commands = null;
        return Native.NotImplemented;
    }
}

/// <summary>The list of formats under "Mercury Converter".</summary>
[GeneratedComClass]
internal sealed partial class FormatList(List<IExplorerCommand> commands) : IEnumExplorerCommand
{
    private int position;

    public int Next(uint count, IntPtr buffer, out uint fetched)
    {
        Activity.Touch();
        fetched = 0;
        var iid = typeof(IExplorerCommand).GUID;
        while (fetched < count && position < commands.Count)
        {
            var pointer = Wrapping.Pointer(commands[position], iid);
            if (pointer == IntPtr.Zero)
            {
                return Native.Fail;
            }

            Marshal.WriteIntPtr(buffer, (int)fetched * IntPtr.Size, pointer);
            fetched++;
            position++;
        }

        return fetched == count ? Native.Ok : Native.False;
    }

    public int Skip(uint count)
    {
        position = (int)Math.Min(commands.Count, position + count);
        return position < commands.Count ? Native.Ok : Native.False;
    }

    public int Reset()
    {
        position = 0;
        return Native.Ok;
    }

    public int Clone(out IEnumExplorerCommand? clone)
    {
        clone = new FormatList(commands) { position = position };
        return Native.Ok;
    }
}

/// <summary>Makes a menu when Windows asks for one.</summary>
[GeneratedComClass]
internal sealed partial class MenuFactory(MediaKind kind, Guid id) : IClassFactory
{
    public int CreateInstance(IntPtr outer, in Guid iid, out IntPtr result)
    {
        Activity.Touch();
        result = IntPtr.Zero;
        if (outer != IntPtr.Zero)
        {
            return Native.NoAggregation;
        }

        result = Wrapping.Pointer(new RootCommand(kind, id), iid);
        return result == IntPtr.Zero ? Native.Fail : Native.Ok;
    }

    public int LockServer(bool lockIt)
    {
        Activity.Touch();
        return Native.Ok;
    }
}
