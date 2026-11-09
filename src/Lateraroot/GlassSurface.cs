using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Lateraroot;

/// <summary>Refractive background material with a vector fallback. Foreground content remains separate.</summary>
public sealed class GlassSurface : Border
{
    public static readonly DependencyProperty ReducedProperty = DependencyProperty.RegisterAttached("Reduced", typeof(bool), typeof(GlassSurface),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public static void SetReduced(DependencyObject element, bool value) => element.SetValue(ReducedProperty, value);
    public static bool GetReduced(DependencyObject element) => (bool)element.GetValue(ReducedProperty);
    public double Inset { get; set; } = 2;
    private readonly DrawingVisual liquid = new();
    private LiquidEffect? effect;
    private BitmapSource? cachedFrame;
    private CroppedBitmap? cachedCrop;
    private Int32Rect cachedRegion;
    internal double Refraction = 9, Blur = 2.7;
    internal GlassBackdrop.Host? BackdropHost { get; set; }
    private sealed record Material(Brush Fill, Brush Reflection, Pen Rim, Pen Inner);
    private static readonly Dictionary<(Color, bool), Material> cache = [];
    private static readonly Brush shadow = Frozen(new SolidColorBrush(Color.FromArgb(26, 0, 0, 0)));
    public GlassSurface()
    {
        CornerRadius = new(16); SnapsToDevicePixels = true; AddVisualChild(liquid);
        Loaded += (_, _) => GlassBackdrop.Register(this);
        Unloaded += (_, _) => { GlassBackdrop.Unregister(this); ClearLiquid(); };
    }
    protected override int VisualChildrenCount => base.VisualChildrenCount + 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? liquid : base.GetVisualChild(index - 1);
    private void ClearLiquid() { using var dc = liquid.RenderOpen(); liquid.Effect = null; cachedFrame = cachedCrop = null; }
    internal void ReleaseBackdrop() { ClearLiquid(); InvalidateVisual(); }

    private bool DrawLiquid(Rect rect, SolidColorBrush source)
    {
        var host = BackdropHost;
        if (host?.Frame is not { } frame || GetReduced(this) || SystemParameters.HighContrast) return false;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(this); Point origin = PointToScreen(rect.TopLeft);
            double px = origin.X - frame.Area.X, py = origin.Y - frame.Area.Y;
            double width = rect.Width * dpi.DpiScaleX, height = rect.Height * dpi.DpiScaleY;
            int padding = (int)Math.Ceiling(18 * dpi.DpiScaleX);
            int x = Math.Max(0, (int)Math.Floor(px) - padding), y = Math.Max(0, (int)Math.Floor(py) - padding);
            int right = Math.Min(frame.Bitmap.PixelWidth, (int)Math.Ceiling(px + width) + padding);
            int bottom = Math.Min(frame.Bitmap.PixelHeight, (int)Math.Ceiling(py + height) + padding);
            if (px < 0 || py < 0 || px + width > frame.Bitmap.PixelWidth + 1 || py + height > frame.Bitmap.PixelHeight + 1 || right <= x || bottom <= y) return false;
            var region = new Int32Rect(x, y, right - x, bottom - y);
            if (!ReferenceEquals(cachedFrame, frame.Bitmap) || cachedRegion != region)
            {
                cachedFrame = frame.Bitmap; cachedRegion = region;
                cachedCrop = new CroppedBitmap(frame.Bitmap, region); cachedCrop.Freeze();
            }
            var crop = cachedCrop!;
            effect ??= new LiquidEffect();
            effect.Mapping = new Point4D((px - x) / crop.PixelWidth, (py - y) / crop.PixelHeight, width / crop.PixelWidth, height / crop.PixelHeight);
            effect.Shape = new Point4D(rect.Width, rect.Height, CornerRadius.TopLeft, Refraction);
            var color = source.Color;
            effect.Tint = new Point4D(color.R / 255d, color.G / 255d, color.B / 255d, WidgetVisuals.Ink(source) == Brushes.Black ? 1 : 0);
            double lx = double.IsNaN(host.Pointer.X) ? .12 : Math.Clamp((host.Pointer.X - origin.X) / Math.Max(1, width), -1, 2);
            double ly = double.IsNaN(host.Pointer.Y) ? .08 : Math.Clamp((host.Pointer.Y - origin.Y) / Math.Max(1, height), -1, 2);
            effect.Light = new Point4D(lx, ly, Blur, dpi.DpiScaleX);
            liquid.Offset = new Vector(rect.X, rect.Y); liquid.Effect = effect;
            using var drawing = liquid.RenderOpen(); drawing.DrawImage(crop, new Rect(0, 0, rect.Width, rect.Height));
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Background is not SolidColorBrush source || ActualWidth <= Inset * 2 + 4 || ActualHeight <= Inset * 2 + 4) { ClearLiquid(); return; }
        if (source.Color.A == 0)
        {
            ClearLiquid();
            if (TemplatedParent is Control { IsKeyboardFocused: true } control)
                dc.DrawRoundedRectangle(null, new Pen(control.Foreground, 2), new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 10, 10);
            return;
        }
        var glassRect = new Rect(Inset + .5, Inset + .5, ActualWidth - Inset * 2 - 1, ActualHeight - Inset * 2 - 1);
        if (DrawLiquid(glassRect, source)) return;
        ClearLiquid();
        bool reduced = GetReduced(this) || SystemParameters.HighContrast;
        var color = source.Color;
        var material = GetMaterial(color, reduced);
        var rect = new Rect(Inset + .5, Inset + .5, ActualWidth - Inset * 2 - 1, ActualHeight - Inset * 2 - 1);
        double radius = Math.Min(CornerRadius.TopLeft, Math.Min(rect.Width, rect.Height) / 2);
        if (!reduced) dc.DrawRoundedRectangle(shadow, null, new Rect(rect.X, rect.Y + 1, rect.Width, rect.Height), radius, radius);
        dc.DrawRoundedRectangle(material.Fill, material.Rim, rect, radius, radius);
        if (!reduced)
        {
            dc.DrawRoundedRectangle(material.Reflection, null, rect, radius, radius);
            rect.Inflate(-1.5, -1.5);
            dc.DrawRoundedRectangle(null, material.Inner, rect, Math.Max(0, radius - 1.5), Math.Max(0, radius - 1.5));
        }
        if (BorderBrush is not null && BorderThickness.Left > 0)
            dc.DrawRoundedRectangle(null, new Pen(BorderBrush, BorderThickness.Left), new Rect(Inset + .5, Inset + .5, ActualWidth - Inset * 2 - 1, ActualHeight - Inset * 2 - 1), radius, radius);
    }

    private static Material GetMaterial(Color color, bool reduced)
    {
        if (cache.TryGetValue((color, reduced), out var material)) return material;
        // Keep text legible over both white and black backdrops, including custom mid-tone colors.
        var tint = color;
        bool light = WidgetVisuals.Ink(new SolidColorBrush(color)) == Brushes.Black;
        const byte alpha = 208;
        for (int i = 0; i < 24 && !reduced; i++)
        {
            byte Composite(byte c) => (byte)(light ? c * alpha / 255 : c * alpha / 255 + 255 - alpha);
            double luminance = WidgetVisuals.Luminance(Color.FromRgb(Composite(tint.R), Composite(tint.G), Composite(tint.B)));
            if (light ? luminance >= .23 : luminance <= .12) break;
            byte Shift(byte c) => (byte)Math.Clamp(light ? c + Math.Max(1, (255 - c) / 12) : c - Math.Max(1, c / 12), 0, 255);
            tint = Color.FromRgb(Shift(tint.R), Shift(tint.G), Shift(tint.B));
        }
        Brush fill = reduced ? new SolidColorBrush(color) : new LinearGradientBrush(
            new GradientStopCollection { new(Color.FromArgb(220, tint.R, tint.G, tint.B), 0), new(Color.FromArgb(alpha, tint.R, tint.G, tint.B), .48), new(Color.FromArgb(216, tint.R, tint.G, tint.B), 1) }, new Point(0, 0), new Point(.75, 1));
        var reflection = new LinearGradientBrush(new GradientStopCollection
        { new(Color.FromArgb(light ? (byte)80 : (byte)24, 255, 255, 255), 0), new(Colors.Transparent, .48), new(Color.FromArgb(12, 255, 255, 255), 1) }, new Point(.15, 0), new Point(.85, 1));
        var rim = new LinearGradientBrush(new GradientStopCollection
        { new(Color.FromArgb(235, 255, 255, 255), 0), new(Color.FromArgb(45, 255, 255, 255), .45), new(Color.FromArgb(60, 0, 0, 0), 1) }, new Point(0, 0), new Point(.65, 1));
        byte edge = light ? (byte)0 : (byte)255;
        var inner = new Pen(new SolidColorBrush(Color.FromArgb(light ? (byte)20 : (byte)30, edge, edge, edge)), .6);
        Brush outer = reduced ? new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)) : rim;
        material = new(Frozen(fill), Frozen(reflection), Frozen(new Pen(outer, 1)), Frozen(inner));
        if (cache.Count >= 64) cache.Clear();
        cache[(color, reduced)] = material; return material;
    }
    private static T Frozen<T>(T resource) where T : Freezable { resource.Freeze(); return resource; }
}
