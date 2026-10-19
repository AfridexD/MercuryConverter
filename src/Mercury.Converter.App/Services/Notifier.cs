using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Mercury.Converter.App.Services;

/// <summary>
/// Tells the user a long run has finished while they are in another app: a Windows notification, and the taskbar
/// button flashes. The notification is skipped if Windows will not show it. The flash always works.
/// </summary>
public static class Notifier
{
    private const string AppId = "MercuryConverter.App";

    private static bool ready;

    /// <summary>Call once at start. Never throws: if Windows refuses, only the taskbar flash is used.</summary>
    public static void Register()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);

            // Windows only shows notifications from an app it knows by name and icon. This tells it, for this user only.
            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
            key.SetValue("DisplayName", "Mercury Converter");
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon64.png");
            if (File.Exists(icon))
            {
                key.SetValue("IconUri", icon);
            }

            ready = true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or COMException or System.Security.SecurityException)
        {
            ready = false;
        }
    }

    public static void Show(string title, string text, IntPtr windowHandle)
    {
        if (windowHandle != IntPtr.Zero)
        {
            Flash(windowHandle);
        }

        if (!ready)
        {
            return;
        }

        try
        {
            var xml = new XmlDocument();
            xml.LoadXml(
                "<toast><visual><binding template=\"ToastGeneric\">"
                + $"<text>{System.Security.SecurityElement.Escape(title)}</text>"
                + $"<text>{System.Security.SecurityElement.Escape(text)}</text>"
                + "</binding></visual></toast>");
            ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(xml));
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ArgumentException)
        {
            // A missed notification is not worth interrupting the user for.
        }
    }

    private static void Flash(IntPtr windowHandle)
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Window = windowHandle,
            Flags = 0x0000000C, // FLASHW_TIMERNOFG: flash until the window comes to the front
            Count = uint.MaxValue,
            Timeout = 0,
        };
        FlashWindowEx(ref info);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
