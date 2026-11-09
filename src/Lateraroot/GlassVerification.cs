using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lateraroot.Core;

namespace Lateraroot;

/// <summary>GPU tests use only an opaque, programmatically generated scene owned by this process.</summary>
internal static class GlassVerification
{
    internal static async Task RunAsync()
    {
        var checks = new List<string>(); var app = App.Current;
        string output = Path.Combine(app.DataDirectory, "glass-verification"); Directory.CreateDirectory(output);
        Window? scene = null, live = null; DesktopCapture? capture = null;
        try
        {
            if (!GlassBackdrop.Supported) throw new NotSupportedException("This machine does not expose PS 3.0 hardware rendering.");
            var image = Pattern(640, 480);
            var canvas = new Canvas { Background = new ImageBrush(image) { Stretch = Stretch.Fill } };
            var text = new TextBlock { Text = "Liquid Glass", FontSize = 18, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var surface = new GlassSurface { Width = 220, Height = 220, Background = Brushes.White, Child = text };
            Canvas.SetLeft(surface, 190); Canvas.SetTop(surface, 120); canvas.Children.Add(surface);
            scene = new Window { Title = "Lateraroot GPU verification", Width = 640, Height = 480, Left = 100, Top = 100, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Topmost = true, Content = canvas };
            scene.Show(); scene.UpdateLayout(); await Task.Delay(250);
            Point origin = scene.PointToScreen(new Point()); var dpi = VisualTreeHelper.GetDpi(scene);
            var rect = new DesktopCapture.Area((int)origin.X, (int)origin.Y, (int)(scene.ActualWidth * dpi.DpiScaleX), (int)(scene.ActualHeight * dpi.DpiScaleY));
            // Use matching physical pixels even on scaled displays.
            image = Pattern(rect.Width, rect.Height); canvas.Background = new ImageBrush(image) { Stretch = Stretch.Fill };
            surface.BackdropHost = new GlassBackdrop.Host(scene, -1) { Frame = new(image, rect) };
            capture = new DesktopCapture(MonitorFromWindow(new WindowInteropHelper(scene).Handle, 2));
            surface.Refraction = 0; surface.Blur = 0; surface.InvalidateVisual(); await Task.Delay(250);
            byte[] baseline = await Read(capture, rect);
            surface.Refraction = 9; surface.InvalidateVisual(); await Task.Delay(250);
            byte[] refracted = await Read(capture, rect);
            int changed = Enumerable.Range(0, baseline.Length / 4).Count(i => Math.Abs(baseline[i * 4] - refracted[i * 4]) + Math.Abs(baseline[i * 4 + 1] - refracted[i * 4 + 1]) + Math.Abs(baseline[i * 4 + 2] - refracted[i * 4 + 2]) > 25);
            Check(changed > 300, "The actual GPU-rendered window changes background pixels when refraction is enabled");
            surface.Refraction = 0; surface.Blur = 2.7; surface.InvalidateVisual(); await Task.Delay(250);
            byte[] blurred = await Read(capture, rect);
            Write(baseline, rect.Width, rect.Height, "baseline.png"); Write(blurred, rect.Width, rect.Height, "blurred.png");
            Check(Enumerable.Range(0, baseline.Length / 4).Count(i => Math.Abs(baseline[i * 4] - blurred[i * 4]) > 12) > 300, "Background blur changes real output pixels independently of refraction");
            int x0 = (int)(190 * dpi.DpiScaleX), y0 = (int)(120 * dpi.DpiScaleY);
            int opaqueText = 0, stableText = 0;
            for (int y = y0 + (int)(80 * dpi.DpiScaleY); y < y0 + (int)(145 * dpi.DpiScaleY); y++)
            for (int x = x0 + (int)(20 * dpi.DpiScaleX); x < x0 + (int)(200 * dpi.DpiScaleX); x++)
            {
                int p = (y * rect.Width + x) * 4;
                // Antialiased edge pixels legitimately change with their backdrop.
                // Opaque glyph interiors must remain intact rather than being blurred.
                if (baseline[p] < 5 && baseline[p + 1] < 5 && baseline[p + 2] < 5)
                {
                    opaqueText++;
                    if (blurred[p] < 10 && blurred[p + 1] < 10 && blurred[p + 2] < 10) stableText++;
                }
            }
            Check(opaqueText >= 20 && stableText >= opaqueText * .98, "Opaque foreground glyphs remain sharp when their background is blurred");
            surface.Refraction = 9; surface.Blur = 2.7; surface.InvalidateVisual(); await Task.Delay(250);
            Write(await Read(capture, rect), rect.Width, rect.Height, "gpu-glass.png");
            capture.Dispose(); capture = null;
            // Remove the synthetic-effect surface before testing live capture.
            canvas.Children.Clear(); scene.UpdateLayout(); await Task.Delay(100);
            var liveSurface = new GlassSurface { Background = Brushes.Black, Child = new TextBlock { Text = "LIVE", FontSize = 20, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            live = new Window { Title = "Lateraroot live glass verification", Width = 260, Height = 240, Left = 280, Top = 230, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false, Topmost = true, Content = liveSurface };
            app.Settings.LiquidGlass = true; GlassBackdrop.Attach(live); live.Show();
            var deadline = Stopwatch.StartNew();
            while (liveSurface.BackdropHost?.Frame is null && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(80);
            Check(liveSurface.BackdropHost?.Frame is not null, "DXGI delivers real background frames to the actual widget material");
            var frame = liveSurface.BackdropHost!.Frame!;
            Point sample = live.PointToScreen(new Point(70, 70));
            int fx = (int)sample.X - frame.Area.X, fy = (int)sample.Y - frame.Area.Y;
            var actual = new byte[4]; frame.Bitmap.CopyPixels(new Int32Rect(fx, fy, 1, 1), actual, 4, 0);
            int sx = (int)sample.X - rect.X, sy = (int)sample.Y - rect.Y;
            var expected = new byte[4]; image.CopyPixels(new Int32Rect(sx, sy, 1, 1), expected, 4, 0);
            Check(Enumerable.Range(0, 3).All(i => Math.Abs(actual[i] - expected[i]) < 5), "Capture excludes the widget itself and sees the controlled background beneath it");
            var changedImage = Pattern(rect.Width, rect.Height, 7); canvas.Background = new ImageBrush(changedImage) { Stretch = Stretch.Fill };
            deadline.Restart();
            while (ReferenceEquals(liveSurface.BackdropHost.Frame, frame) && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(60);
            Check(!ReferenceEquals(liveSurface.BackdropHost.Frame, frame), "A changed background updates the live glass through the dirty-region path");
            app.Settings.ReduceTransparency = true; GlassBackdrop.Refresh();
            Check(liveSurface.BackdropHost.Frame is null, "Reduced transparency clears the live background immediately");
            app.Settings.ReduceTransparency = false; GlassBackdrop.Refresh(); deadline.Restart();
            while (liveSurface.BackdropHost.Frame is null && deadline.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(60);
            Check(liveSurface.BackdropHost.Frame is not null, "Re-enabling glass refreshes a stationary background without waiting for content changes");
            await Task.Delay(2000); // Exclude device/shader/window warm-up from the idle sample.
            var process = Process.GetCurrentProcess(); var cpuStart = process.TotalProcessorTime; var idle = Stopwatch.StartNew();
            long publishedBefore = GlassBackdrop.PublishedFrames, copiedBefore = DesktopCapture.RegionCopies;
            await Task.Delay(3000); process.Refresh();
            double cpuCorePercent = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / idle.Elapsed.TotalMilliseconds * 100;
            live.Hide(); await Task.Delay(250);
            Check(liveSurface.BackdropHost!.Frame is null, "Hiding the widget releases its displayed backdrop");
            JsonStore.Write(Path.Combine(output, "result.json"), new { Passed = true, Checks = checks, IdleCpuOneCorePercent = cpuCorePercent, IdleFramesPublished = GlassBackdrop.PublishedFrames - publishedBefore, IdleRegionsCopied = DesktopCapture.RegionCopies - copiedBefore, Tier = RenderCapability.Tier >> 16, Status = GlassBackdrop.Status });
            app.Shutdown(0);
        }
        catch (Exception ex) { JsonStore.Write(Path.Combine(output, "result.json"), new { Passed = false, Checks = checks, Error = ex.ToString(), Status = GlassBackdrop.Status }); app.Shutdown(1); }
        finally { capture?.Dispose(); live?.Close(); scene?.Close(); }

        void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks.Add(name); }
        async Task<byte[]> Read(DesktopCapture reader, DesktopCapture.Area area)
        {
            for (int attempt = 0; attempt < 15; attempt++)
            {
                var frames = await Task.Run(() => reader.Read(new[] { (1, area) }));
                if (frames.Count > 0) return frames[0].Frame.Bytes.ToArray();
                await Task.Delay(50);
            }
            throw new InvalidOperationException("No owned-window GPU frame arrived.");
        }
        void Write(byte[] pixels, int width, int height, string name)
        {
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, name)); png.Save(stream);
        }
    }
    private static BitmapSource Pattern(int width, int height, int phase = 0)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4; bool stripe = (((x + phase) / 7) + (y / 11)) % 2 == 0;
            pixels[p] = stripe ? (byte)225 : (byte)75; pixels[p + 1] = (byte)(80 + y * 150 / height); pixels[p + 2] = stripe ? (byte)65 : (byte)230; pixels[p + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
}
