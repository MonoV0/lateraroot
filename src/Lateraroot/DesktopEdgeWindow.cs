using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Lateraroot.Core;

namespace Lateraroot;

internal sealed class DesktopEdgeWindow : Window
{
    private readonly Canvas root = new() { Background = null };
    private readonly List<FrameworkElement> cells = [];
    private readonly WidgetActions actions;
    private (string Id, string Name, string Color)[] appearance = [];
    private int labelSize;
    private Thumb? grip;
    internal bool CanDrag => grip?.IsEnabled == true;
    private readonly DragStartedEventHandler dragStart;
    private readonly DragDeltaEventHandler dragDelta;
    private readonly DragCompletedEventHandler dragEnd;
    public double TileSide { get; private set; } = 88;
    public int CellCount => cells.Count;
    public IReadOnlyList<FrameworkElement> SquareCells => cells;

    public DesktopEdgeWindow(WidgetActions actions, Action enter, Action leave, DragStartedEventHandler dragStart, DragDeltaEventHandler dragDelta, DragCompletedEventHandler dragEnd)
    {
        this.actions = actions;
        this.dragStart = dragStart; this.dragDelta = dragDelta; this.dragEnd = dragEnd;
        Title = "Lateraroot — ウィジェット"; Style = (Style)App.Current.FindResource(typeof(Window));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = false; Content = root;
        MouseEnter += (_, _) => enter(); MouseLeave += (_, _) => leave();
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.F5) { actions.Refresh(); e.Handled = true; } };
    }
    public void Update(IReadOnlyList<StackRule> rules, IReadOnlyDictionary<string, int> counts)
    {
        var next = rules.Select(r => (r.Id, r.Name, r.Color)).ToArray();
        if (appearance.SequenceEqual(next) && labelSize == App.Current.Settings.TileSize)
        {
            for (int i = 0; i < rules.Count; i++) UpdateCount((Grid)((Button)cells[i]).Content, counts.GetValueOrDefault(rules[i].Id));
            UpdateDragLock(); return;
        }
        appearance = next; labelSize = App.Current.Settings.TileSize;
        root.Children.Clear(); cells.Clear();
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var label = GenreLabel(rule, i, counts.GetValueOrDefault(rule.Id));
            var button = new Button { Content = label, Tag = rule.Id, Background = ColorBrush(rule.Color), Padding = new Thickness(0), BorderThickness = new Thickness(0), Style = (Style)App.Current.FindResource("SquareButton"), ToolTip = rule.Name + " — クリックまたはホバーで開く" };
            button.Foreground = WidgetVisuals.Ink(button.Background);
            System.Windows.Automation.AutomationProperties.SetName(button, rule.Name + " ウィジェット");
            button.MouseEnter += (_, _) => actions.Select(rule.Id); button.Click += (_, _) => actions.Select(rule.Id);
            button.ContextMenu = actions.CreateMenu(rule.Id);
            root.Children.Add(button); cells.Add(button);
        }
        var utility = new Grid { Background = Brushes.Transparent };
        utility.Children.Add(new GlassSurface { Background = Brushes.White, IsHitTestVisible = false });
        grip = new Thumb { Height = 22, VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.SizeAll };
        grip.Template = (ControlTemplate)App.Current.FindResource("CompactGripTemplate"); WidgetMotion.SetHover(grip, true);
        grip.DragStarted += dragStart; grip.DragDelta += dragDelta; grip.DragCompleted += dragEnd;
        var utilityLabel = new Grid { Margin = new Thickness(8) };
        utilityLabel.Children.Add(new TextBlock { Text = "lr.", FontSize = 20, VerticalAlignment = VerticalAlignment.Top });
        utilityLabel.Children.Add(new TextBlock { Text = "設定", FontSize = 10, VerticalAlignment = VerticalAlignment.Bottom });
        var more = new Button { Content = utilityLabel, Background = Brushes.Transparent, Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(0), BorderThickness = new Thickness(0), ToolTip = "ウィジェット設定", Style = (Style)App.Current.FindResource("SquareButton") };
        more.Click += (_, _) => actions.Settings();
        utility.ContextMenu = actions.CreateMenu(); utility.Children.Add(more); utility.Children.Add(grip);
        root.Children.Add(utility); cells.Add(utility); UpdateDragLock(); Place();
    }
    public void UpdateDragLock()
    {
        if (grip is null) return;
        grip.IsEnabled = !App.Current.Settings.LockPosition;
        grip.Cursor = grip.IsEnabled ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Arrow;
        grip.ToolTip = grip.IsEnabled ? "ドラッグで配置を編集" : "配置はロックされています。設定や右クリックから解除できます。";
        ToolTipService.SetShowOnDisabled(grip, true);
    }
    public static Grid GenreLabel(StackRule rule, int index, int count, bool compact = false)
    {
        var label = new Grid { Margin = new Thickness(compact ? 8 : 10) };
        label.Children.Add(new TextBlock { Text = (index + 1).ToString("00"), FontSize = 10, VerticalAlignment = VerticalAlignment.Top });
        label.Children.Add(new TextBlock { Text = WidgetVisuals.Balance(rule.Name, compact ? 50 : App.Current.Settings.TileSize - 20, compact ? 11 : 12), FontSize = compact ? 11 : 12, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = compact ? 13 : 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = compact ? 26 : 32, VerticalAlignment = VerticalAlignment.Center });
        var counter = new TextBlock { Text = count + "件", FontSize = 10, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right };
        label.Children.Add(counter); label.Tag = counter;
        return label;
    }
    public static void UpdateCount(Grid label, int count) { if (label.Tag is TextBlock text && text.Text != count + "件") text.Text = count + "件"; }
    public void Place()
    {
        if (cells.Count == 0) return;
        WidgetMotion.SetEnabled(this, WidgetMotion.Allowed);
        var screen = WindowPlacement.Screen; double scale = WindowPlacement.Scale(this, screen);
        var area = WindowPlacement.Area(screen); var settings = App.Current.Settings;
        var bounds = DockLayout.DesktopRail(area, settings.Edge, scale, cells.Count - 1, settings.TileSize, settings.DockOffset);
        for (int i = 0; i < cells.Count; i++)
        {
            var rect = DockLayout.DesktopCell(area, settings.Edge, scale, i, cells.Count - 1, settings.TileSize, settings.DockOffset);
            TileSide = rect.Width / scale; cells[i].Width = cells[i].Height = TileSide;
            Canvas.SetLeft(cells[i], (rect.X - bounds.X) / scale); Canvas.SetTop(cells[i], (rect.Y - bounds.Y) / scale);
        }
        WindowPlacement.Place(this, bounds, scale);
    }
    public static Brush ColorBrush(string color)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        var brush = new SolidColorBrush(c);
        brush.Freeze(); return brush;
    }
}
