using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Mercury.Converter.Shell;

// The few Windows interfaces File Explorer uses for a right-click menu. The order of the methods is the order Windows
// expects, so none may be moved or left out.

[GeneratedComInterface]
[Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
internal partial interface IShellItem
{
    [PreserveSig]
    int BindToHandler(IntPtr bindContext, in Guid handler, in Guid iid, out IntPtr result);

    [PreserveSig]
    int GetParent(out IntPtr parent);

    [PreserveSig]
    int GetDisplayName(uint kind, out IntPtr name);

    [PreserveSig]
    int GetAttributes(uint mask, out uint attributes);

    [PreserveSig]
    int Compare(IntPtr other, uint hint, out int order);
}

[GeneratedComInterface]
[Guid("B63EA76D-1F85-456F-A19C-48159EFA858B")]
internal partial interface IShellItemArray
{
    [PreserveSig]
    int BindToHandler(IntPtr bindContext, in Guid handler, in Guid iid, out IntPtr result);

    [PreserveSig]
    int GetPropertyStore(int flags, in Guid iid, out IntPtr result);

    [PreserveSig]
    int GetPropertyDescriptionList(IntPtr keyType, in Guid iid, out IntPtr result);

    [PreserveSig]
    int GetAttributes(int flags, uint mask, out uint attributes);

    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetItemAt(uint index, out IShellItem? item);

    [PreserveSig]
    int EnumItems(out IntPtr result);
}

[GeneratedComInterface]
[Guid("A08CE4D0-FA25-44AB-B57C-C7B1C323E0B9")]
internal partial interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(IShellItemArray? items, out IntPtr name);

    [PreserveSig]
    int GetIcon(IShellItemArray? items, out IntPtr icon);

    [PreserveSig]
    int GetToolTip(IShellItemArray? items, out IntPtr tip);

    [PreserveSig]
    int GetCanonicalName(out Guid name);

    [PreserveSig]
    int GetState(IShellItemArray? items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);

    [PreserveSig]
    int Invoke(IShellItemArray? items, IntPtr bindContext);

    [PreserveSig]
    int GetFlags(out uint flags);

    [PreserveSig]
    int EnumSubCommands(out IEnumExplorerCommand? commands);
}

[GeneratedComInterface]
[Guid("A88826F8-186F-4987-AADE-EA0CEF8FBFE8")]
internal partial interface IEnumExplorerCommand
{
    /// <param name="commands">A buffer for <paramref name="count"/> IExplorerCommand pointers.</param>
    [PreserveSig]
    int Next(uint count, IntPtr commands, out uint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumExplorerCommand? clone);
}

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr outer, in Guid iid, out IntPtr result);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool lockIt);
}

internal static partial class Native
{
    public const int Ok = 0;
    public const int False = 1;
    public const int NotImplemented = unchecked((int)0x80004001);
    public const int NoAggregation = unchecked((int)0x80040110);
    public const int Fail = unchecked((int)0x80004005);

    /// <summary>Asks for the real file path of an item.</summary>
    public const uint FileSystemPath = 0x80058000;

    public const uint LocalServer = 4;
    public const uint MultipleUse = 1;

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int CoInitializeEx(IntPtr reserved, uint options);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int CoRegisterClassObject(in Guid clsid, IntPtr unknown, uint context, uint flags, out uint cookie);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int CoRevokeClassObject(uint cookie);
}
