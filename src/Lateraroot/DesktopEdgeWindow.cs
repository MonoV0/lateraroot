using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Lateraroot.Core;

namespace Lateraroot;

/// <summary>A thin edge-only window: the center of the desktop has no overlay or input surface.</summary>
internal sealed class DesktopEdgeWindow : Window
{
    private readonly DockPanel root = new();
    private readonly UniformGrid zones = new();
    private readonly StackPanel controls = new() { Orientation = Orientation.Horizontal };
    private readonly Action<string> select;

    public DesktopEdgeWindow(Action<string> select, Action settings, Action minimize, Action hide, Action enter, Action leave)
    {
        this.select = select;
        Title = "Lateraroot — デスクトップ";
        Style = (Style)App.Current.FindResource(typeof(Window));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Content = root;
        foreach (var (text, tip, action) in new[] { ("⚙", "設定", settings), ("−", "最小化", minimize), ("×", "非表示", hide) })
        {
            var button = new Button { Content = text, ToolTip = tip, Padding = new Thickness(0), BorderThickness = new Thickness(0), Width = 24, Height = 48 };
            System.Windows.Automation.AutomationProperties.SetName(button, tip);
            button.Click += (_, _) => action(); controls.Children.Add(button);
        }
        root.Children.Add(controls); root.Children.Add(zones);
        MouseEnter += (_, _) => enter(); MouseLeave += (_, _) => leave();
    }

    public void Update(IReadOnlyList<StackRule> rules, IReadOnlyDictionary<string, int> counts)
    {
        bool vertical = DockLayout.IsVertical(App.Current.Settings.Edge);
        DockPanel.SetDock(controls, vertical ? Dock.Bottom : Dock.Right);
        controls.Orientation = vertical ? Orientation.Horizontal : Orientation.Vertical;
        foreach (Button button in controls.Children) { button.Width = vertical ? 24 : 48; button.Height = vertical ? 48 : 24; }
        zones.Columns = vertical ? 1 : rules.Count; zones.Rows = vertical ? rules.Count : 1;
        zones.Children.Clear();
        var screen = WindowPlacement.Screen;
        var scale = WindowPlacement.Scale(this, screen);
        double slot = ((vertical ? screen.WorkingArea.Height : screen.WorkingArea.Width) / scale - 48) / rules.Count;
        foreach (var rule in rules)
        {
            var label = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = rule.Symbol, FontSize = vertical ? 23 : 18, HorizontalAlignment = HorizontalAlignment.Center, Visibility = slot >= 68 ? Visibility.Visible : Visibility.Collapsed });
            label.Children.Add(new TextBlock { Text = rule.Name, FontSize = 10, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = vertical ? 68 : Math.Max(20, slot - 6), Margin = new Thickness(3, vertical && slot >= 68 ? 8 : 0, 3, 2) });
            label.Children.Add(new TextBlock { Text = counts.GetValueOrDefault(rule.Id).ToString(), FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Visibility = slot >= 52 ? Visibility.Visible : Visibility.Collapsed });
            var button = new Button { Content = label, Tag = rule.Id, Background = ColorBrush(rule.Color), BorderBrush = Brushes.White, BorderThickness = new Thickness(0, 0, 0, 0.5), Padding = new Thickness(0), ToolTip = rule.Name };
            System.Windows.Automation.AutomationProperties.SetName(button, rule.Name + " ジャンル");
            button.MouseEnter += (_, _) => select(rule.Id); button.Click += (_, _) => select(rule.Id);
            zones.Children.Add(button);
        }
        Place();
    }

    public void Place()
    {
        var screen = WindowPlacement.Screen; var scale = WindowPlacement.Scale(this, screen);
        WindowPlacement.Place(this, DockLayout.DesktopRail(WindowPlacement.Area(screen), App.Current.Settings.Edge, scale), scale);
    }
    public static Brush ColorBrush(string color, double white = 0)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        var brush = new SolidColorBrush(Color.FromRgb((byte)(c.R + (255 - c.R) * white), (byte)(c.G + (255 - c.G) * white), (byte)(c.B + (255 - c.B) * white)));
        brush.Freeze(); return brush;
    }
}
