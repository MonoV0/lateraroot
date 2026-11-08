using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Lateraroot;

/// <summary>Foreground notifications only. No timer, window enumeration, or process-history storage.</summary>
internal sealed class DesktopContextMonitor : IDisposable
{
    private readonly WinEvent callback;
    private readonly Action<bool> changed;
    private readonly IntPtr hook;
    public bool IsDesktop { get; private set; }
    public IntPtr DesktopHandle { get; private set; }

    public DesktopContextMonitor(Action<bool> changed)
    {
        this.changed = changed;
        callback = (_, _, hwnd, _, _, _, _) => Inspect(hwnd);
        hook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2); // EVENT_SYSTEM_FOREGROUND, SKIPOWNPROCESS
        Inspect(GetForegroundWindow());
    }

    private void Inspect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out uint process);
        if (process == Environment.ProcessId) return; // Keep the context while interacting with our own controls.
        var name = new StringBuilder(256); GetClassName(hwnd, name, name.Capacity);
        var value = name.ToString();
        // Taskbar and shell menus do not change the underlying desktop/application context.
        if (value is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "#32768") return;
        bool desktop = hwnd == GetShellWindow() || IsDesktopClass(value);
        bool hostChanged = desktop && DesktopHandle != hwnd;
        if (desktop) DesktopHandle = hwnd;
        if (desktop == IsDesktop && !hostChanged) return;
        IsDesktop = desktop; changed(desktop);
    }
    internal static bool IsDesktopClass(string value) => value is "Progman" or "WorkerW" or "SHELLDLL_DefView";
    public void Dispose() { if (hook != IntPtr.Zero) UnhookWinEvent(hook); GC.KeepAlive(callback); }
    private delegate void WinEvent(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
}
