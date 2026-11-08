using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Lateraroot;

/// <summary>A small compiled Flaticon vector, inheriting its control's ink; never a network/font dependency at runtime.</summary>
public sealed class UiIcon : Control
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(UiIcon),
        new FrameworkPropertyMetadata("SettingsIcon", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public static readonly DependencyProperty OpticalWeightProperty = DependencyProperty.Register(nameof(OpticalWeight), typeof(double), typeof(UiIcon),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double OpticalWeight { get => (double)GetValue(OpticalWeightProperty); set => SetValue(OpticalWeightProperty, value); }
    public UiIcon() { Width = Height = 18; Focusable = false; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext dc)
    {
        if (TryFindResource(Kind) is not Geometry geometry) return;
        var bounds = geometry.Bounds;
        if (bounds.IsEmpty || bounds.Width == 0 || bounds.Height == 0) return;
        double scale = System.Math.Min(ActualWidth / bounds.Width, ActualHeight / bounds.Height);
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, (ActualWidth - bounds.Width * scale) / 2 - bounds.X * scale, (ActualHeight - bounds.Height * scale) / 2 - bounds.Y * scale));
        // Keep extremely thin horizontal glyphs visible at small control sizes.
        // The correction is in DIPs, independent of the font outline's units.
        var outline = OpticalWeight > 0 ? new Pen(Foreground, OpticalWeight / scale) { LineJoin = PenLineJoin.Round } : null;
        dc.DrawGeometry(Foreground, outline, geometry); dc.Pop();
    }
}
