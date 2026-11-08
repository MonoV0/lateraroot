using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lateraroot.Core;
using Forms = System.Windows.Forms;

namespace Lateraroot;

internal static class WindowPlacement
{
    public static IntPtr DesktopAnchor { get; set; }
    public static Forms.Screen Screen => Array.Find(Forms.Screen.AllScreens, s => s.DeviceName == App.Current.Settings.Monitor) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    public static DockRect Area(Forms.Screen screen) => new(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
    public static double Scale(Window window, Forms.Screen screen)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        if (Forms.Screen.FromHandle(handle).DeviceName != screen.DeviceName)
            SetWindowPos(handle, IntPtr.Zero, screen.WorkingArea.Left, screen.WorkingArea.Top, 0, 0, 0x0015);
        return Math.Max(96u, GetDpiForWindow(handle)) / 96d;
    }
    public static void Place(Window window, DockRect rect, double scale)
    {
        if (window.WindowState != WindowState.Normal) window.WindowState = WindowState.Normal;
        window.Width = rect.Width / scale; window.Height = rect.Height / scale;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        IntPtr after = window.Topmost ? new IntPtr(-1) : DesktopInsertAfter();
        SetWindowPos(handle, window.Topmost ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x0013);
        if (!SetWindowPos(handle, after, (int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height), 0x0010))
            SetWindowPos(handle, window.Topmost ? new IntPtr(-1) : new IntPtr(-2), (int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height), 0x0010);
    }
    private static IntPtr DesktopInsertAfter()
    {
        var anchor = DesktopAnchor != IntPtr.Zero && IsWindow(DesktopAnchor) ? DesktopAnchor : GetShellWindow();
        var candidate = GetWindow(anchor, 3); // Immediately above the desktop, below ordinary app windows.
        for (int i = 0; i < 32 && candidate != IntPtr.Zero; i++)
        {
            GetWindowThreadProcessId(candidate, out uint pid);
            if (pid != Environment.ProcessId)
                return (GetWindowLongPtr(candidate, -20).ToInt64() & 8) == 0 ? candidate : IntPtr.Zero;
            candidate = GetWindow(candidate, 3);
        }
        return IntPtr.Zero;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
}
