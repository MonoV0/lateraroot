using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lateraroot;

/// <summary>A shared, vector-only glass material. Never captures the desktop or blurs file content.</summary>
public sealed class GlassSurface : Border
{
    public static readonly DependencyProperty ReducedProperty = DependencyProperty.RegisterAttached("Reduced", typeof(bool), typeof(GlassSurface),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
    public static void SetReduced(DependencyObject element, bool value) => element.SetValue(ReducedProperty, value);
    public static bool GetReduced(DependencyObject element) => (bool)element.GetValue(ReducedProperty);
    public double Inset { get; set; } = 2;
    private sealed record Material(Brush Fill, Brush Reflection, Pen Rim, Pen Inner);
    private static readonly Dictionary<(Color, bool), Material> cache = [];
    private static readonly Brush shadow = Frozen(new SolidColorBrush(Color.FromArgb(26, 0, 0, 0)));
    public GlassSurface() { CornerRadius = new(16); SnapsToDevicePixels = true; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Background is not SolidColorBrush source || ActualWidth <= Inset * 2 + 4 || ActualHeight <= Inset * 2 + 4) return;
        if (source.Color.A == 0)
        {
            if (TemplatedParent is Control { IsKeyboardFocused: true } control)
                dc.DrawRoundedRectangle(null, new Pen(control.Foreground, 2), new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 10, 10);
            return;
        }
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
            if (light ? luminance >= .19 : luminance <= .12) break;
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
