using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lateraroot.Core;

namespace Lateraroot;

/// <summary>Finite transform/opacity motion. No rendering loop or animated window dimensions.</summary>
public static class WidgetMotion
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(WidgetMotion), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, PolicyChanged));
    public static readonly DependencyProperty HoverProperty = DependencyProperty.RegisterAttached("Hover", typeof(bool), typeof(WidgetMotion), new PropertyMetadata(false, HoverChanged));
    public static bool GetHover(DependencyObject target) => (bool)target.GetValue(HoverProperty);
    public static void SetHover(DependencyObject target, bool value) => target.SetValue(HoverProperty, value);
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    internal static bool? TestOverride { get; set; }
    public static bool Allowed => !App.Current.Settings.ReduceMotion && (TestOverride ?? (!App.Current.Settings.FollowSystemAnimations || SystemParameters.ClientAreaAnimation));
    private static void HoverChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Control control) return;
        if ((bool)e.NewValue) { control.MouseEnter += HoverEnter; control.MouseLeave += HoverLeave; control.Loaded += HoverLoaded; }
        else { control.MouseEnter -= HoverEnter; control.MouseLeave -= HoverLeave; control.Loaded -= HoverLoaded; }
    }
    private static void HoverEnter(object sender, MouseEventArgs e) => HoverState((Control)sender, true, true);
    private static void HoverLeave(object sender, MouseEventArgs e) => HoverState((Control)sender, false, true);
    private static void HoverLoaded(object sender, RoutedEventArgs e) => HoverState((Control)sender, ((Control)sender).IsMouseOver, false);
    private static void PolicyChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    { if (target is Control control && GetHover(control)) HoverState(control, control.IsMouseOver, false); }
    private static void HoverState(Control control, bool over, bool animate)
    {
        foreach (var (name, value) in new[] { ("HoverInk", over ? .08 : 0), ("HoverLine", over ? .38 : 0) })
        {
            if (control.Template?.FindName(name, control) is not Border layer) continue;
            double from = layer.Opacity; layer.BeginAnimation(UIElement.OpacityProperty, null); layer.Opacity = value;
            if (animate && Allowed && GetEnabled(control)) Animate(layer, UIElement.OpacityProperty, from, value, over ? 180 : 120, 0);
        }
    }
    public static void Cancel(UIElement target)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null); target.Opacity = 1;
        if (target.RenderTransform is TranslateTransform t) { t.BeginAnimation(TranslateTransform.XProperty, null); t.BeginAnimation(TranslateTransform.YProperty, null); t.X = t.Y = 0; }
    }
    public static void Reveal(UIElement target, DockEdge edge, int rank = 0, bool resume = false)
    {
        double opacity = resume ? target.Opacity : 0;
        bool verticalEdge = DockLayout.IsVertical(edge);
        double currentOffset = resume && target.RenderTransform is TranslateTransform current ? verticalEdge ? current.X : current.Y : 0;
        Cancel(target);
        if (!Allowed) return;
        int delay = Math.Clamp(rank, 0, 7) * 24;
        var translate = target.RenderTransform as TranslateTransform ?? new TranslateTransform(); target.RenderTransform = translate;
        double offset = resume ? currentOffset : edge is DockEdge.Right or DockEdge.Bottom ? 12 : -12;
        Animate(target, UIElement.OpacityProperty, opacity, 1, 260, delay);
        Animate(translate, verticalEdge ? TranslateTransform.XProperty : TranslateTransform.YProperty, offset, 0, 320, delay);
    }
    public static void Exit(UIElement target, DockEdge edge, Action completed)
    {
        if (!Allowed) { Cancel(target); completed(); return; }
        double current = target.Opacity;
        double offset = target.RenderTransform is TranslateTransform t ? DockLayout.IsVertical(edge) ? t.X : t.Y : 0;
        Cancel(target);
        var translate = target.RenderTransform as TranslateTransform ?? new TranslateTransform(); target.RenderTransform = translate;
        var fade = new DoubleAnimation(current, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }, FillBehavior = FillBehavior.HoldEnd };
        fade.Completed += (_, _) => completed();
        var clock = (AnimationClock)fade.CreateClock(true); target.ApplyAnimationClock(UIElement.OpacityProperty, clock, HandoffBehavior.SnapshotAndReplace);
        clock.Controller!.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
        Animate(translate, DockLayout.IsVertical(edge) ? TranslateTransform.XProperty : TranslateTransform.YProperty, offset, edge is DockEdge.Right or DockEdge.Bottom ? 8 : -8, 140, 0);
    }
    private static void Animate(Animatable target, DependencyProperty property, double from, double to, int ms, int delay)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > 0) animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms + delay)), new KeySpline(.215, .61, .355, 1)));
        var clock = (AnimationClock)animation.CreateClock(true); target.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        clock.Controller!.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
    }
    private static void Animate(UIElement target, DependencyProperty property, double from, double to, int ms, int delay)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delay > 0) animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delay))));
        animation.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms + delay)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        var clock = (AnimationClock)animation.CreateClock(true); target.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        clock.Controller!.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
    }
}
