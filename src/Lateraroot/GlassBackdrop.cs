using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Lateraroot;

/// <summary>One bounded DXGI reader for both widget windows. Frames live only in memory.</summary>
internal static class GlassBackdrop
{
    internal sealed record Frame(BitmapSource Bitmap, DesktopCapture.Area Area);
    internal sealed class Host(Window window, int id)
    {
        internal readonly Window Window = window;
        internal readonly int Id = id;
        internal readonly HashSet<GlassSurface> Surfaces = [];
        internal Frame? Frame;
        internal IntPtr Handle;
        internal bool Excluded;
        internal long Revision, LastLight;
        internal Point Pointer = new(double.NaN, double.NaN);
    }
    private sealed record Request(int Id, IntPtr Monitor, DesktopCapture.Area Rect, long Revision);
    private static readonly Dictionary<Window, Host> hosts = [];
    private static readonly AutoResetEvent changed = new(false);
    private static readonly CancellationTokenSource stop = new();
    private static Request[] requests = [];
    private static Task? worker;
    private static int nextId;
    internal static long PublishedFrames;
    private static bool popupHooks;
    internal static string Status { get; private set; } = "ガラス描画はオフです";
    internal static bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && RenderCapability.IsPixelShaderVersionSupported(3, 0);
    internal static void Attach(Window window)
    {
        if (hosts.ContainsKey(window) || App.Current.IsVerifying) return;
        if (!popupHooks)
        {
            popupHooks = true;
            void Popup(object sender, RoutedEventArgs args)
            {
                if (sender is Visual visual && PresentationSource.FromVisual(visual) is HwndSource source)
                    SetWindowDisplayAffinity(source.Handle, App.Current.Settings.LiquidGlass && !App.Current.Settings.ReduceTransparency && !SystemParameters.HighContrast ? 0x11u : 0u);
            }
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent, new RoutedEventHandler(Popup));
            EventManager.RegisterClassHandler(typeof(ToolTip), ToolTip.OpenedEvent, new RoutedEventHandler(Popup));
        }
        var host = new Host(window, ++nextId); hosts[window] = host;
        window.SourceInitialized += (_, _) => { host.Handle = new WindowInteropHelper(window).Handle; Refresh(); };
        window.IsVisibleChanged += (_, _) => Refresh();
        window.LocationChanged += (_, _) => Refresh();
        window.SizeChanged += (_, _) => Refresh();
        window.Closed += (_, _) =>
        {
            if (host.Handle != IntPtr.Zero) SetWindowDisplayAffinity(host.Handle, 0);
            hosts.Remove(window); Refresh();
        };
        window.MouseMove += (_, e) =>
        {
            if (host.Frame is null || App.Current.Settings.ReduceMotion || Environment.TickCount64 - host.LastLight < 30) return;
            Point pointer = window.PointToScreen(e.GetPosition(window));
            if (host.Pointer == pointer) return; // WPF can synthesize MouseMove after a redraw.
            host.LastLight = Environment.TickCount64; host.Pointer = pointer;
            foreach (var surface in host.Surfaces) surface.InvalidateVisual();
        };
    }
    internal static void Register(GlassSurface surface)
    {
        var window = Window.GetWindow(surface);
        if (window is not null && hosts.TryGetValue(window, out var host)) { host.Surfaces.Add(surface); surface.BackdropHost = host; surface.InvalidateVisual(); }
    }
    internal static void Unregister(GlassSurface surface)
    { surface.BackdropHost?.Surfaces.Remove(surface); surface.BackdropHost = null; }

    internal static void Refresh()
    {
        bool enabled = App.Current.Settings.LiquidGlass && !App.Current.Settings.ReduceTransparency && !SystemParameters.HighContrast;
        bool capable = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && RenderCapability.IsPixelShaderVersionSupported(3, 0);
        var next = new List<Request>();
        foreach (var host in hosts.Values)
        {
            host.Revision++;
            bool active = enabled && capable && host.Window.IsVisible && host.Handle != IntPtr.Zero;
            if (active && !host.Excluded) host.Excluded = SetWindowDisplayAffinity(host.Handle, 0x11);
            if (active && host.Excluded && GetWindowRect(host.Handle, out var rect))
            {
                int padding = (int)Math.Ceiling(32 * VisualTreeHelper.GetDpi(host.Window).DpiScaleX);
                var area = new DesktopCapture.Area(rect.Left - padding, rect.Top - padding, rect.Right - rect.Left + padding * 2, rect.Bottom - rect.Top + padding * 2);
                if (area.Valid) next.Add(new(host.Id, MonitorFromWindow(host.Handle, 2), area, host.Revision));
                else { host.Frame = null; foreach (var surface in host.Surfaces) surface.ReleaseBackdrop(); }
            }
            else
            {
                host.Frame = null;
                foreach (var surface in host.Surfaces) surface.ReleaseBackdrop();
                if (host.Excluded && SetWindowDisplayAffinity(host.Handle, 0)) host.Excluded = false;
            }
        }
        Volatile.Write(ref requests, next.ToArray()); changed.Set();
        if (enabled && capable && next.Count > 0)
        {
            worker ??= Task.Run(Run);
            if (!Status.StartsWith("稼働", StringComparison.Ordinal)) Status = "背景を取得しています…";
        }
        else Status = enabled && !capable ? "この環境では通常の半透明表示を使用します" : "ガラス描画はオフです";
    }
    private static void Run()
    {
        var readers = new Dictionary<IntPtr, DesktopCapture>();
        var delivered = new Dictionary<int, long>();
        try
        {
            while (!stop.IsCancellationRequested)
            {
                long started = Environment.TickCount64;
                var current = Volatile.Read(ref requests);
                foreach (int id in delivered.Keys.Where(id => current.All(r => r.Id != id)).ToArray()) delivered.Remove(id);
                foreach (var monitor in readers.Keys.Where(m => current.All(r => r.Monitor != m)).ToArray()) { readers[monitor].Dispose(); readers.Remove(monitor); }
                if (current.Length == 0) { changed.WaitOne(); continue; }
                try
                {
                    foreach (var group in current.GroupBy(r => r.Monitor))
                    {
                        if (!readers.TryGetValue(group.Key, out var reader)) readers[group.Key] = reader = new(group.Key);
                        var refresh = group.Where(r => !delivered.TryGetValue(r.Id, out long revision) || revision != r.Revision).Select(r => r.Id).ToHashSet();
                        foreach (var (id, pixels) in reader.Read(group.Select(r => (r.Id, r.Rect)).ToArray(), refresh))
                        {
                            var request = group.First(r => r.Id == id);
                            var area = pixels.Area;
                            // The desktop is opaque; DXGI's unused alpha must not erase the background.
                            var image = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, pixels.Bytes, area.Width * 4);
                            image.Freeze(); var frame = new Frame(image, area);
                            // Keep at most one dispatcher operation outstanding; never build a frame queue.
                            bool applied = App.Current.Dispatcher.Invoke(() =>
                            {
                                var host = hosts.Values.FirstOrDefault(h => h.Id == id);
                                if (host is null || host.Revision != request.Revision || !host.Window.IsVisible) return false;
                                host.Frame = frame; Status = "稼働中 — 背景ぼかし・屈折（最大30fps）";
                                PublishedFrames++;
                                foreach (var surface in host.Surfaces) surface.InvalidateVisual();
                                return true;
                            }, DispatcherPriority.Background);
                            if (applied) delivered[id] = request.Revision;
                        }
                    }
                }
                catch (Exception ex) when (ex is COMException or NotSupportedException or InvalidOperationException or ArgumentException)
                {
                    foreach (var reader in readers.Values) reader.Dispose(); readers.Clear(); delivered.Clear();
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        Status = "背景取得が使えないため通常表示へ切替: " + ex.GetType().Name;
                        foreach (var host in hosts.Values) { host.Frame = null; foreach (var surface in host.Surfaces) surface.ReleaseBackdrop(); }
                    });
                    changed.WaitOne(1500);
                }
                int delay = (int)Math.Max(0, 33 - (Environment.TickCount64 - started));
                if (delay > 0) changed.WaitOne(delay);
            }
        }
        finally { foreach (var reader in readers.Values) reader.Dispose(); }
    }
    internal static void Shutdown()
    {
        stop.Cancel(); changed.Set();
        foreach (var host in hosts.Values) if (host.Handle != IntPtr.Zero) SetWindowDisplayAffinity(host.Handle, 0);
        // The worker may be waiting for the UI dispatcher; do not block it here.
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
}
