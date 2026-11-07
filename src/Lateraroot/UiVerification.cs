using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lateraroot.Core;

namespace Lateraroot;

public partial class MainWindow
{
    internal async Task VerifyUiAsync()
    {
        var checks = new List<string>();
        string output = Path.Combine(Host.DataDirectory, "verification");
        Directory.CreateDirectory(output);
        try
        {
            await Settled();
            var face = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
            Check(face.TryGetGlyphTypeface(out var glyph) && glyph.FontUri.ToString().Contains("ZenKakuGothicNew-Medium.ttf", StringComparison.OrdinalIgnoreCase), "Bundled Zen Kaku Gothic New Medium actually resolves");
            Check(!expanded && Math.Abs(ActualWidth - 68) < 1, "Compact collapsed rail");
            Capture(this, "collapsed.png");
            int docs = Host.Settings.Stacks.FindIndex(x => x.Id == "documents");
            ((Button)StackRail.Children[docs]).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            TogglePin(this, new RoutedEventArgs());
            await Settled();
            Check(expanded && pinned && FileItems.Items.Count == 3, "Compact hover and pin use shared file view");
            Capture(this, "compact.png");
            SearchBox.Text = "アイデア"; Check(FileItems.Items.Count == 1, "Search text-change filters files");
            SearchBox.Text = "no-such-file"; Check(FileItems.Items.Count == 0 && EmptyState.Visibility == Visibility.Visible, "Search empty state"); SearchBox.Text = "";
            OrderBox.SelectedIndex = (int)FileOrder.Size; Check(Host.Settings.Order == FileOrder.Size, "Sort change persists"); OrderBox.SelectedIndex = 0;
            for (int i = 1; i <= 16; i++) File.WriteAllText(Path.Combine(Host.DesktopDirectory, $"資料 {i:00}.txt"), "Lateraroot UI demo");
            RefreshFiles();
            UpdateDesktopContext(true); await Settled();
            Check(desktopEdge.IsVisible && !IsVisible, "Desktop context shows only full edge when collapsed");
            var screen = WindowPlacement.Screen; var area = WindowPlacement.Area(screen);
            Check(Math.Abs(desktopEdge.ActualHeight * VisualTreeHelper.GetDpi(desktopEdge).DpiScaleY - area.Height) < 2, "Genre blocks cover the complete work-area edge");
            SelectStack("documents"); pinned = true; await Settled();
            Check(desktopMode && expanded && FileItems.Items.Count == DesktopColumns && ExpandAllButton.IsVisible, "Desktop hover shows one row of genre-colored files");
            Check(FileScroll.ExtentHeight <= FileScroll.ViewportHeight + 1 && FileScroll.ViewportWidth >= DesktopColumns * 100, "Desktop preview fits a complete row without scrollbars");
            CaptureDesktop("desktop-preview.png");
            ToggleAllFiles(this, new RoutedEventArgs()); await Settled();
            Check(showAll && FileItems.Items.Count == 19 && SearchTools.IsVisible, "OPEN reveals wrapped rows and common tools");
            CaptureDesktop("desktop-expanded.png");
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                Host.Settings.Edge = edge; BuildRail(); ApplyPresentation(); await Settled();
                CheckBounds(this, edge, false); CheckBounds(desktopEdge, edge, true);
            }
            Host.Settings.Edge = DockEdge.Right; BuildRail(); ApplyPresentation();
            SetDockVisibility(DockVisibility.Minimized); await Settled();
            Check(IsVisible && !desktopEdge.IsVisible && MinimizedTab.IsVisible && ActualWidth == 28, "Minimize replaces desktop edge with a small tab");
            Capture(this, "minimized.png");
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                Host.Settings.Edge = edge; ApplyPresentation(); await Settled();
                Check(MiniRestore.ActualWidth >= 24 && MiniRestore.ActualHeight >= 24, edge + " minimized restore control stays usable");
            }
            Host.Settings.Edge = DockEdge.Right;
            SetDockVisibility(DockVisibility.Hidden); Check(!IsVisible && !desktopEdge.IsVisible, "Hide removes every application surface");
            bool handled = false; WindowMessage(new WindowInteropHelper(this).Handle, 0x0312, new IntPtr(1), IntPtr.Zero, ref handled);
            Check(handled && Host.Settings.Visibility == DockVisibility.Visible && desktopEdge.IsVisible, "Hotkey restores a hidden dock");
            modalDepth++;
            UpdateDesktopContext(false);
            Check(!desktopEdge.IsVisible, "Desktop edge immediately hides when an external app wins focus during a dialog");
            EndModal();
            Check(!desktopMode && IsVisible, "Dialog exit applies the latest deferred foreground context");
            UpdateDesktopContext(false); await Settled();
            Check(IsVisible && !desktopEdge.IsVisible && !expanded, "Application context switches back to compact mode");
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                Host.Settings.Edge = edge;
                foreach (double offset in new[] { 0d, 1d })
                {
                    Host.Settings.DockOffset = offset; ApplyPresentation(); await Settled(); CheckBounds(this, edge, true);
                }
            }
            Check(!DesktopContextMonitor.IsDesktopClass("CabinetWClass") && DesktopContextMonitor.IsDesktopClass("Progman") && DesktopContextMonitor.IsDesktopClass("WorkerW"), "Explorer file windows remain distinct from the desktop");
            Host.Settings.Edge = DockEdge.Right; Host.Settings.DockOffset = 0.5; BuildRail(); ApplyPresentation();
            var settings = new SettingsWindow(); settings.Show(); await Settled();
            Check(settings.IsVisible && settings.FontFamily.Source.Contains("Zen Kaku Gothic New"), "Settings uses the common bundled typography");
            Capture(settings, "settings.png"); settings.Close();
            SelectStack("documents"); pinned = true;
            int total = allItems.Count;
            var stored = Host.Library.Organize(allItems.Where(x => x.CanOrganize));
            Check(stored.Succeeded == total && stored.Errors.Count == 0, "Storage regression: all demo files and directories organized");
            RefreshFiles(); Check(allItems.All(x => x.IsStored) && !OrganizeButton.IsEnabled && RestoreButton.IsEnabled, "Shared view reflects storage state");
            var restored = Host.Library.Restore(allItems.Select(x => x.RecordId!));
            Check(restored.Succeeded == total && restored.Errors.Count == 0, "Storage regression: all items restored");
            RefreshFiles(); Check(allItems.All(x => !x.IsStored), "Restored files reappear without duplicate entries");
            Check(Host.Windows.Count == 2, "Two reusable windows only; no overlay per file or genre");
            SaveSettings();
            JsonStore.Write(Path.Combine(output, "result.json"), new { Passed = true, Checks = checks });
            Host.Trace("UI verification passed: " + checks.Count); Host.Shutdown(0);
        }
        catch (Exception ex)
        {
            JsonStore.Write(Path.Combine(output, "result.json"), new { Passed = false, Checks = checks, Error = ex.ToString() });
            Host.Trace("UI verification failed: " + ex.Message); Host.Shutdown(1);
        }
        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        void CheckBounds(Window window, DockEdge edge, bool onEdge)
        {
            GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
            var area = WindowPlacement.Area(WindowPlacement.Screen);
            bool inside = r.Left >= area.X - 1 && r.Right <= area.Right + 1 && r.Top >= area.Y - 1 && r.Bottom <= area.Bottom + 1;
            bool aligned = edge switch { DockEdge.Right => Math.Abs(r.Right - area.Right) < 2, DockEdge.Left => Math.Abs(r.Left - area.X) < 2, DockEdge.Top => Math.Abs(r.Top - area.Y) < 2, _ => Math.Abs(r.Bottom - area.Bottom) < 2 };
            Check(inside && (!onEdge || aligned), $"{(desktopMode ? "Desktop" : "Compact")} {window.Title} {edge} bounds and alignment");
        }
        RenderTargetBitmap Render(Window window)
        {
            window.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window); return bitmap;
        }
        void Write(BitmapSource bitmap, string name)
        {
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        }
        void Capture(Window window, string name) => Write(Render(window), name);
        void CaptureDesktop(string name)
        {
            var area = WindowPlacement.Area(WindowPlacement.Screen);
            var canvas = new DrawingVisual();
            using (var dc = canvas.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, area.Width, area.Height));
                foreach (var window in new Window[] { desktopEdge, this })
                {
                    GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
                    dc.DrawImage(Render(window), new Rect(r.Left - area.X, r.Top - area.Y, r.Right - r.Left, r.Bottom - r.Top));
                }
            }
            var bitmap = new RenderTargetBitmap((int)area.Width, (int)area.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(canvas); Write(bitmap, name);
        }
        async Task Settled() { closeTimer.Stop(); await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(80); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
}
