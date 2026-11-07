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
using Forms = System.Windows.Forms;

namespace Lateraroot;

public partial class MainWindow
{
    // Deterministic integration checks of our own WPF visual tree, using only demo data.
    // No OS input injection, real-desktop modification, or other application inspection.
    internal async Task VerifyUiAsync()
    {
        var checks = new List<string>();
        string output = Path.Combine(Host.DataDirectory, "verification");
        Directory.CreateDirectory(output);
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(!expanded && Panel.Visibility == Visibility.Collapsed && Math.Abs(ActualWidth - 68) < 1, "Collapsed rail");
            Capture(this, "collapsed.png");
            var docs = Host.Settings.Stacks.FindIndex(x => x.Id == "documents");
            ((Button)StackRail.Children[docs]).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            await Settled();
            Check(expanded && Panel.IsVisible && ActualWidth > 400 && FileItems.Items.Count == 3, "Hover routed event expands document stack");
            Check(Rail.TransformToAncestor(this).Transform(new Point()).X > 400, "Right rail remains at right edge");
            CheckBounds(DockEdge.Right);
            StatusText.Text = "デモモード · 実際のデスクトップには触れません";
            Capture(this, "expanded.png");
            SearchBox.Text = "アイデア"; Check(FileItems.Items.Count == 1, "Search text-change filters files");
            SearchBox.Text = "not-a-demo-file"; Check(FileItems.Items.Count == 0 && EmptyState.Visibility == Visibility.Visible, "Search empty state");
            SearchBox.Text = "";
            OrderBox.SelectedIndex = (int)FileOrder.Size; Check(Host.Settings.Order == FileOrder.Size, "Sort change persists");
            OrderBox.SelectedIndex = (int)FileOrder.Name;
            TogglePin(this, new RoutedEventArgs()); Check(pinned, "Pin button toggles persistent panel");
            Collapse(); Check(!expanded && !pinned, "Close resets pin and collapses");
            Host.Settings.Edge = DockEdge.Left; PlaceDock(); Expand(); await Settled();
            Check(Rail.TransformToAncestor(this).Transform(new Point()).X < 5, "Left rail remains at left edge");
            CheckBounds(DockEdge.Left);
            Capture(this, "left-edge.png");
            var settings = new SettingsWindow();
            settings.Show(); await Settled();
            Check(settings.IsVisible && settings.ActualHeight > 500, "Settings window renders");
            Capture(settings, "settings.png"); settings.Close();
            Host.Settings.Edge = DockEdge.Right; JsonStore.Write(Host.SettingsPath, Host.Settings);
            var stored = Host.Library.Organize(allItems.Where(x => x.CanOrganize));
            Check(stored.Succeeded == 5 && stored.Errors.Count == 0, "Demo files and directory organized");
            RefreshFiles();
            Check(allItems.Count(x => x.IsStored) == 5 && !OrganizeButton.IsEnabled && RestoreButton.IsEnabled, "UI reflects organized files");
            var restored = Host.Library.Restore(allItems.Where(x => x.IsStored).Select(x => x.RecordId!));
            Check(restored.Succeeded == 5 && restored.Errors.Count == 0, "Demo files restored");
            RefreshFiles(); Check(allItems.All(x => !x.IsStored) && OrganizeButton.IsEnabled, "UI reflects restored files");
            JsonStore.Write(Path.Combine(output, "result.json"), new { Passed = true, Checks = checks });
            Host.Trace("UI verification passed: " + checks.Count);
            Host.Shutdown(0);
        }
        catch (Exception ex)
        {
            JsonStore.Write(Path.Combine(output, "result.json"), new { Passed = false, Checks = checks, Error = ex.ToString() });
            Host.Trace("UI verification failed: " + ex.Message);
            Host.Shutdown(1);
        }

        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        void CheckBounds(DockEdge edge)
        {
            var handle = new WindowInteropHelper(this).Handle;
            GetWindowRect(handle, out var rect);
            var area = Forms.Screen.FromHandle(handle).WorkingArea;
            Check(rect.Left >= area.Left && rect.Right <= area.Right && rect.Top >= area.Top && rect.Bottom <= area.Bottom &&
                (edge == DockEdge.Left ? Math.Abs(rect.Left - area.Left) < 2 : Math.Abs(rect.Right - area.Right) < 2), edge + " dock stays inside work area");
        }
        void Capture(Window window, string name)
        {
            window.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(window);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name)); encoder.Save(stream);
        }
        async Task Settled() { await Task.Delay(220); await Dispatcher.InvokeAsync(UpdateLayout, DispatcherPriority.ApplicationIdle); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
}
