using System;
using System.Collections.Generic;
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
    private readonly Action<string> select;
    private readonly Action settings, inspector;
    private readonly Action minimize, hide;
    private readonly DragStartedEventHandler dragStart;
    private readonly DragDeltaEventHandler dragDelta;
    private readonly DragCompletedEventHandler dragEnd;
    public double TileSide { get; private set; } = 88;
    public int CellCount => cells.Count;
    public IReadOnlyList<FrameworkElement> SquareCells => cells;

    public DesktopEdgeWindow(Action<string> select, Action settings, Action inspector, Action minimize, Action hide, Action enter, Action leave, DragStartedEventHandler dragStart, DragDeltaEventHandler dragDelta, DragCompletedEventHandler dragEnd)
    {
        this.select = select; this.settings = settings; this.inspector = inspector; this.minimize = minimize; this.hide = hide;
        this.dragStart = dragStart; this.dragDelta = dragDelta; this.dragEnd = dragEnd;
        Title = "Lateraroot — ウィジェット"; Style = (Style)App.Current.FindResource(typeof(Window));
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = false; Content = root;
        MouseEnter += (_, _) => enter(); MouseLeave += (_, _) => leave();
    }
    public void Update(IReadOnlyList<StackRule> rules, IReadOnlyDictionary<string, int> counts)
    {
        root.Children.Clear(); cells.Clear();
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var label = GenreLabel(rule, i, counts.GetValueOrDefault(rule.Id));
            var button = new Button { Content = label, Tag = rule.Id, Background = ColorBrush(rule.Color, .12), Padding = new Thickness(0), BorderThickness = new Thickness(0), Style = (Style)App.Current.FindResource("SquareButton"), ToolTip = rule.Name + " — クリックまたはホバーで開く" };
            button.Foreground = WidgetVisuals.Ink(button.Background);
            System.Windows.Automation.AutomationProperties.SetName(button, rule.Name + " ウィジェット");
            button.MouseEnter += (_, _) => select(rule.Id); button.Click += (_, _) => select(rule.Id);
            var menu = new ContextMenu();
            Add(menu, "開く", () => select(rule.Id));
            Add(menu, "検索・整理", () => { select(rule.Id); inspector(); });
            Add(menu, "ウィジェット設定", settings);
            button.ContextMenu = menu;
            root.Children.Add(button); cells.Add(button);
        }
        var utility = new Grid { Background = Brushes.White };
        var grip = new Thumb { Height = 22, VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.SizeAll, ToolTip = "ドラッグで配置を編集" };
        var template = new ControlTemplate(typeof(Thumb));
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var text = new FrameworkElementFactory(typeof(TextBlock)); text.SetValue(TextBlock.TextProperty, "····"); text.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(text); template.VisualTree = border; grip.Template = template;
        grip.DragStarted += dragStart; grip.DragDelta += dragDelta; grip.DragCompleted += dragEnd;
        var utilityLabel = new Grid { Margin = new Thickness(8) };
        utilityLabel.Children.Add(new TextBlock { Text = "lr.", FontSize = 20, VerticalAlignment = VerticalAlignment.Top });
        utilityLabel.Children.Add(new TextBlock { Text = "設定", FontSize = 10, VerticalAlignment = VerticalAlignment.Bottom });
        var more = new Button { Content = utilityLabel, Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(0), BorderThickness = new Thickness(0), ToolTip = "ウィジェット設定", Style = (Style)App.Current.FindResource("SquareButton") };
        more.Click += (_, _) => settings();
        var controls = new ContextMenu(); Add(controls, "ウィジェット設定", settings); Add(controls, "検索・整理", inspector); Add(controls, "最小化", minimize); Add(controls, "非表示", hide);
        utility.ContextMenu = controls; utility.Children.Add(more); utility.Children.Add(grip);
        root.Children.Add(utility); cells.Add(utility); Place();
    }
    public static Grid GenreLabel(StackRule rule, int index, int count, bool compact = false)
    {
        var label = new Grid { Margin = new Thickness(compact ? 8 : 10) };
        label.Children.Add(new TextBlock { Text = (index + 1).ToString("00"), FontSize = 10, VerticalAlignment = VerticalAlignment.Top });
        label.Children.Add(new TextBlock { Text = compact ? rule.Name : WidgetVisuals.Balance(rule.Name, App.Current.Settings.TileSize - 20), FontSize = compact ? 11 : 12, TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = compact ? 18 : 32, VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(new TextBlock { Text = count + "件", FontSize = 10, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Right });
        return label;
    }
    private static void Add(ContextMenu menu, string text, Action action) { var item = new MenuItem { Header = text }; item.Click += (_, _) => action(); menu.Items.Add(item); }
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
    public static Brush ColorBrush(string color, double white = 0)
    {
        var c = (Color)ColorConverter.ConvertFromString(color);
        var brush = new SolidColorBrush(Color.FromRgb((byte)(c.R + (255 - c.R) * white), (byte)(c.G + (255 - c.G) * white), (byte)(c.B + (255 - c.B) * white)));
        brush.Freeze(); return brush;
    }
}
