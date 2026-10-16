using System.Runtime.InteropServices;

namespace Mercury.Converter.App.Services;

/// <summary>
/// Shows conversion progress on the window's taskbar button, using the Windows ITaskbarList3 interface.
/// Every call is best-effort: a failure here must never stop a conversion.
/// </summary>
public sealed class TaskbarProgress(IntPtr windowHandle) : IDisposable
{
    private static readonly Guid TaskbarListClsid = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    private ITaskbarList3? taskbar;
    private bool unavailable;

    /// <summary>Sets the progress bar to a fraction from 0 to 1.</summary>
    public void SetFraction(double fraction)
    {
        if (!TryGet(out var list))
        {
            return;
        }

        var percent = (ulong)Math.Clamp(Math.Round(fraction * 1000), 0, 1000);
        Try(() => list.SetProgressState(windowHandle, TbpfNormal));
        Try(() => list.SetProgressValue(windowHandle, percent, 1000));
    }

    /// <summary>Shows an error state (red bar) when a conversion failed.</summary>
    public void ShowError()
    {
        if (TryGet(out var list))
        {
            Try(() => list.SetProgressState(windowHandle, TbpfError));
        }
    }

    /// <summary>Removes the progress bar.</summary>
    public void Clear()
    {
        if (TryGet(out var list))
        {
            Try(() => list.SetProgressState(windowHandle, TbpfNoProgress));
        }
    }

    private bool TryGet(out ITaskbarList3 list)
    {
        if (unavailable)
        {
            list = null!;
            return false;
        }

        if (taskbar is null)
        {
            try
            {
                var type = Type.GetTypeFromCLSID(TaskbarListClsid);
                taskbar = (ITaskbarList3?)Activator.CreateInstance(type!);
                taskbar?.HrInit();
            }
            catch (COMException)
            {
                unavailable = true;
            }
            catch (InvalidCastException)
            {
                unavailable = true;
            }
        }

        list = taskbar!;
        return taskbar is not null;
    }

    private static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (COMException)
        {
            // The taskbar can be busy or restarting. Missing progress is harmless.
        }
    }

    public void Dispose()
    {
        if (taskbar is not null)
        {
            Marshal.FinalReleaseComObject(taskbar);
            taskbar = null;
        }
    }

    private const int TbpfNoProgress = 0;
    private const int TbpfNormal = 2;
    private const int TbpfError = 4;

    /// <summary>The parts of ITaskbarList3 that we use, in the order Windows defines them.</summary>
    // Windows 10 and 11 answer the ITaskbarList4 interface ID. ITaskbarList3 is refused, even though
    // ITaskbarList4 extends it and keeps the same method order for the methods used here.
    [ComImport]
    [Guid("c43dc798-95d1-4bea-9030-bb99e2983a1a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, int state);
    }
}
