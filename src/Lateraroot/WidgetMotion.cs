using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lateraroot.Core;

namespace Lateraroot;

/// <summary>Finite transform/opacity motion. No rendering loop or animated window dimensions.</summary>
public static class WidgetMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(WidgetMotion), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    internal static bool? TestOverride { get; set; }
    public static bool Allowed => !App.Current.Settings.ReduceMotion && (TestOverride ?? SystemParameters.ClientAreaAnimation);
    public static void Cancel(UIElement target)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null); target.Opacity = 1;
        if (target.RenderTransform is TranslateTransform t) { t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null); t.X = t.Y = 0; }
    }
    public static void Reveal(UIElement target, DockEdge edge, int rank = 0, bool resume = false)
    {
        double opacity = resume ? target.Opacity : 0;
        Cancel(target);
        if (!Allowed) return;
        int delay = Math.Clamp(rank, 0, 7) * 24;
        var translate = target.RenderTransform as TranslateTransform ?? new TranslateTransform(); target.RenderTransform = translate;
        bool verticalEdge = DockLayout.IsVertical(edge);
        double offset = edge is DockEdge.Right or DockEdge.Bottom ? 12 : -12;
        Animate(target, UIElement.OpacityProperty, opacity, 1, 260, delay);
        Animate(translate, verticalEdge ? TranslateTransform.XProperty : TranslateTransform.YProperty, offset, 0, 320, delay);
    }
    public static void Exit(UIElement target, DockEdge edge, Action completed)
    {
        if (!Allowed) { Cancel(target); completed(); return; }
        double current = target.Opacity; Cancel(target);
        var translate = target.RenderTransform as TranslateTransform ?? new TranslateTransform(); target.RenderTransform = translate;
        var fade = new DoubleAnimation(current, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }, FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => completed();
        target.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        Animate(translate, DockLayout.IsVertical(edge) ? TranslateTransform.XProperty : TranslateTransform.YProperty, 0, edge is DockEdge.Right or DockEdge.Bottom ? 8 : -8, 140, 0);
    }
    private static void Animate(Animatable target, DependencyProperty property, double from, double to, int ms, int delay)
    {
        var animation = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delay), FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new KeySpline(.215, .61, .355, 1)));
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
    private static void Animate(UIElement target, DependencyProperty property, double from, double to, int ms, int delay)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
