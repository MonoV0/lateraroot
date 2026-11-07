using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;
using Lateraroot.Core;
using Forms = System.Windows.Forms;

namespace Lateraroot;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer closeTimer = new();
    private readonly DispatcherTimer refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly Forms.NotifyIcon tray;
    private IReadOnlyList<DesktopItem> allItems = [];
    private string selectedStack;
    private bool expanded, pinned, ready, busy, desktopMode, showAll, dragging, libraryDirty;
    private bool? deferredDesktop;
    private int modalDepth;
    private HwndSource? source;
    private DesktopEdgeWindow desktopEdge = null!;
    private DesktopContextMonitor? contextMonitor;
    private App Host => App.Current;

    public MainWindow()
    {
        selectedStack = Host.Settings.Stacks[0].Id;
        InitializeComponent();
        ShowInTaskbar = Host.IsSandbox;
        OrderBox.SelectedIndex = (int)Host.Settings.Order;
        closeTimer.Tick += (_, _) => { closeTimer.Stop(); if (!IsMouseOver && !desktopEdge.IsMouseOver && !pinned && modalDepth == 0 && !busy && !dragging) Collapse(); };
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); if (!busy && Host.Settings.Visibility != DockVisibility.Hidden) RefreshFiles(); };
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Lateraroot", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("表示する  (Ctrl+Alt+Space)", null, (_, _) => Dispatcher.Invoke(RestoreDock));
        menu.Items.Add("最小化", null, (_, _) => Dispatcher.Invoke(() => SetDockVisibility(DockVisibility.Minimized)));
        menu.Items.Add("非表示", null, (_, _) => Dispatcher.Invoke(() => SetDockVisibility(DockVisibility.Hidden)));
        menu.Items.Add("設定", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Dispatcher.Invoke(() => Host.Shutdown()));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreDock);
        Loaded += (_, _) =>
        {
            if (ready) return;
            ready = true;
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowMessage);
            if (!RegisterHotKey(new WindowInteropHelper(this).Handle, 1, 0x4003, 0x20)) StatusText.Text = "ホットキーを使用できません。通知領域から開けます。";
            desktopEdge = new DesktopEdgeWindow(SelectStack, OpenSettings, () => SetDockVisibility(DockVisibility.Minimized), () => SetDockVisibility(DockVisibility.Hidden), () => closeTimer.Stop(), () => WindowLeave(this, null!));
            RefreshFiles(); RestartWatchers();
            if (!Host.IsVerifying)
            {
                contextMonitor = new DesktopContextMonitor(value => Dispatcher.BeginInvoke(() => UpdateDesktopContext(value)));
                desktopMode = contextMonitor.IsDesktop;
            }
            ApplyPresentation();
        };
        DpiChanged += (_, _) => { if (ready) Dispatcher.BeginInvoke(PlaceDock); };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            closeTimer.Stop(); refreshTimer.Stop(); contextMonitor?.Dispose();
            foreach (var watcher in watchers) watcher.Dispose();
            desktopEdge?.Close(); tray.Dispose();
            UnregisterHotKey(new WindowInteropHelper(this).Handle, 1); source?.RemoveHook(WindowMessage);
            // Alt+F4 must also release the process, hook and instance mutex.
            Host.Shutdown();
        };
    }

    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { desktopEdge.Place(); ApplyPresentation(); });
    private void RestartWatchers()
    {
        foreach (var watcher in watchers) watcher.Dispose(); watchers.Clear();
        Watch(Host.DesktopDirectory, false); Watch(Host.Library.VaultPath, true);
        if (!Host.IsSandbox && Host.Settings.IncludePublicDesktop) Watch(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false);
    }
    private void Watch(string path, bool recursive)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = recursive, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, _) => QueueRefresh();
            watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed; watcher.Renamed += (_, _) => QueueRefresh(); watcher.Error += (_, _) => QueueRefresh();
            watcher.EnableRaisingEvents = true; watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = "フォルダーを監視できません: " + ex.Message; }
    }
    private void QueueRefresh()
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(() => { libraryDirty = true; refreshTimer.Stop(); refreshTimer.Start(); });
    }
    private void RefreshFiles()
    {
        if (!ready || busy) return;
        allItems = Host.Library.Scan(Host.Settings);
        libraryDirty = false;
        if (!Host.Settings.Stacks.Any(x => x.Id == selectedStack)) selectedStack = Host.Settings.Stacks[0].Id;
        BuildRail();
        if (expanded) { RenderFiles(); PlaceDock(); }
    }
    private void BuildRail()
    {
        var counts = allItems.GroupBy(x => x.StackId).ToDictionary(x => x.Key, x => x.Count());
        StackRail.Children.Clear();
        foreach (var stack in Host.Settings.Stacks)
        {
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = stack.Symbol, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center });
            label.Children.Add(new TextBlock { Text = counts.GetValueOrDefault(stack.Id).ToString(), FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Content = label, Width = 66, Height = 54, Padding = new Thickness(3), BorderThickness = new Thickness(0), Background = DesktopEdgeWindow.ColorBrush(stack.Color, 0.65), ToolTip = stack.Name };
            System.Windows.Automation.AutomationProperties.SetName(button, stack.Name + " ジャンル");
            button.MouseEnter += (_, _) => { if (!dragging) SelectStack(stack.Id); };
            button.Click += (_, _) => { SelectStack(stack.Id); Activate(); };
            StackRail.Children.Add(button);
        }
        desktopEdge.Update(Host.Settings.Stacks, counts);
    }
    private void SelectStack(string id)
    {
        if (Host.Settings.Visibility != DockVisibility.Visible || dragging) return;
        bool changed = selectedStack != id;
        if (changed) { selectedStack = id; showAll = false; SearchBox.Text = ""; FileScroll.ScrollToTop(); }
        if (!expanded) Expand(); else if (changed) { ApplyPresentation(); }
        closeTimer.Stop();
    }
    private int DesktopColumns => Math.Clamp((int)((WindowPlacement.Screen.WorkingArea.Width / VisualTreeHelper.GetDpi(this).DpiScaleX - 116) / 100), 1, 8);
    private void RenderFiles()
    {
        if (!ready) return;
        var stack = Host.Settings.Stacks.First(x => x.Id == selectedStack);
        var items = allItems.Where(x => x.StackId == selectedStack).ToList();
        var filtered = items.Where(x => x.Name.Contains(SearchBox.Text, StringComparison.CurrentCultureIgnoreCase));
        if (desktopMode && !showAll) filtered = filtered.Take(DesktopColumns);
        var background = DesktopEdgeWindow.ColorBrush(stack.Color, desktopMode ? 0.5 : 0.86);
        var tiles = filtered.Select(item => new FileTile(item, ShellIcons.Get(item.Path, item.IsDirectory), background)).ToList();
        FileItems.ItemsSource = tiles;
        StackTitle.Text = stack.Name; StackSubtitle.Text = $"{items.Count} 個 / {items.Count(x => x.IsStored)} 個を収納済み";
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = SearchBox.Text.Length > 0 ? "一致するファイルはありません" : "ファイルはありません";
        EmptyDescription.Text = SearchBox.Text.Length > 0 ? "別のファイル名で検索してください。" : "分類ルールは設定から変更できます。";
        RestoreButton.IsEnabled = items.Any(x => x.IsStored); OrganizeButton.IsEnabled = allItems.Any(x => x.CanOrganize);
        Frame.Background = desktopMode ? DesktopEdgeWindow.ColorBrush(stack.Color, 0.68) : Brushes.White;
    }
    private void UpdateDesktopContext(bool value)
    {
        if (modalDepth > 0 || dragging) { deferredDesktop = value; if (!value) desktopEdge.Hide(); return; }
        if (desktopMode == value) return;
        desktopMode = value; expanded = pinned = showAll = false; PinButton.Content = "○";
        ApplyPresentation();
    }
    private void ApplyDeferredContext()
    {
        if (deferredDesktop is { } value && modalDepth == 0 && !busy && !dragging) { deferredDesktop = null; UpdateDesktopContext(value); }
    }
    private void ApplyPresentation()
    {
        if (!ready) return;
        var visibility = Host.Settings.Visibility;
        if (visibility == DockVisibility.Hidden) { desktopEdge.Hide(); Hide(); return; }
        bool minimized = visibility == DockVisibility.Minimized;
        bool desktop = desktopMode && !minimized;
        Rail.Visibility = desktop || minimized ? Visibility.Collapsed : Visibility.Visible;
        MinimizedTab.Visibility = minimized ? Visibility.Visible : Visibility.Collapsed;
        Panel.Visibility = expanded && !minimized ? Visibility.Visible : Visibility.Collapsed;
        if (!expanded || minimized) FileItems.ItemsSource = null;
        ExpandAllButton.Visibility = desktop ? Visibility.Visible : Visibility.Collapsed;
        ExpandAllButton.Content = showAll ? "↗ CLOSE" : "↙ OPEN";
        bool tools = !desktop || showAll;
        FileScroll.VerticalScrollBarVisibility = desktop && !showAll ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        SearchTools.Visibility = Footer.Visibility = StatusText.Visibility = tools ? Visibility.Visible : Visibility.Collapsed;
        StackSubtitle.Visibility = tools ? Visibility.Visible : Visibility.Collapsed;
        Panel.Margin = new Thickness(desktop ? 12 : 18);
        PanelHeader.Margin = new Thickness(0, 0, 0, desktop ? 8 : 16);
        StackTitle.FontSize = desktop ? 14 : 20;
        if (expanded) RenderFiles();
        if (desktop)
        {
            if (!expanded) Hide(); else { PlaceDock(); if (!IsVisible) Show(); }
            if (!desktopEdge.IsVisible) desktopEdge.Show();
            desktopEdge.Place();
        }
        else { desktopEdge.Hide(); PlaceDock(); if (!IsVisible) Show(); }
    }
    private void Expand()
    {
        closeTimer.Stop();
        if (expanded) return;
        if (libraryDirty) RefreshFiles();
        expanded = true; showAll = false;
        ApplyPresentation();
    }
    private void Collapse()
    {
        if (modalDepth > 0 || busy) return;
        expanded = pinned = showAll = false; PinButton.Content = "○"; ApplyPresentation();
    }
    private void PlaceDock()
    {
        if (!ready || Host.Settings.Visibility == DockVisibility.Hidden) return;
        var screen = WindowPlacement.Screen; var area = WindowPlacement.Area(screen); var scale = WindowPlacement.Scale(this, screen);
        bool minimized = Host.Settings.Visibility == DockVisibility.Minimized;
        var edge = Host.Settings.Edge; bool vertical = DockLayout.IsVertical(edge);
        Grid.SetRowSpan(Panel, 1); Grid.SetColumnSpan(Panel, 1); Grid.SetRow(Rail, 0); Grid.SetRow(Panel, 0);
        if (desktopMode && !minimized)
        {
            Grid.SetColumn(Panel, 0); PanelColumn.Width = new GridLength(1, GridUnitType.Star); RailColumn.Width = new GridLength(0); FirstRow.Height = new GridLength(1, GridUnitType.Star); SecondRow.Height = new GridLength(0);
            int count = allItems.Count(x => x.StackId == selectedStack && x.Name.Contains(SearchBox.Text, StringComparison.CurrentCultureIgnoreCase));
            int columns = Math.Min(DesktopColumns, Math.Max(3, Math.Min(count, 8)));
            double width = Math.Max(344, columns * 100 + 44);
            double height = showAll ? 228 + Math.Min(6, Math.Max(1, Math.Ceiling(count / (double)Math.Max(1, columns)))) * 96 : 164;
            WindowPlacement.Place(this, DockLayout.Flyout(area, edge, Host.Settings.Stacks.FindIndex(x => x.Id == selectedStack), Host.Settings.Stacks.Count, scale, width, height), scale);
            return;
        }
        var rect = DockLayout.Compact(area, edge, Host.Settings.DockOffset, scale, expanded, minimized);
        var railSize = new GridLength(minimized ? 26 : 66);
        if (vertical)
        {
            bool left = edge == DockEdge.Left;
            Grid.SetColumn(Rail, left ? 0 : 1); Grid.SetColumn(Panel, left ? 1 : 0);
            PanelColumn.Width = left ? railSize : new GridLength(expanded && !minimized ? 1 : 0, GridUnitType.Star);
            RailColumn.Width = left ? new GridLength(expanded && !minimized ? 1 : 0, GridUnitType.Star) : railSize;
            FirstRow.Height = new GridLength(1, GridUnitType.Star); SecondRow.Height = new GridLength(0);
        }
        else
        {
            bool top = edge == DockEdge.Top;
            Grid.SetColumn(Rail, 0); Grid.SetColumn(Panel, 0); Grid.SetRow(Rail, top ? 0 : 1); Grid.SetRow(Panel, top ? 1 : 0);
            PanelColumn.Width = new GridLength(1, GridUnitType.Star); RailColumn.Width = new GridLength(0);
            FirstRow.Height = top ? railSize : new GridLength(expanded && !minimized ? 1 : 0, GridUnitType.Star);
            SecondRow.Height = top ? new GridLength(expanded && !minimized ? 1 : 0, GridUnitType.Star) : railSize;
        }
        DockPanel.SetDock(RailHeader, vertical ? Dock.Top : Dock.Left); DockPanel.SetDock(RailActions, vertical ? Dock.Bottom : Dock.Right);
        RailHeader.Width = vertical ? double.NaN : 48;
        RailActions.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        StackRail.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        MiniFirstCol.Width = vertical ? new GridLength(1, GridUnitType.Star) : new GridLength(20);
        MiniSecondCol.Width = vertical ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MiniFirstRow.Height = vertical ? new GridLength(20) : new GridLength(1, GridUnitType.Star);
        MiniSecondRow.Height = vertical ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetRow(MiniRestore, vertical ? 1 : 0); Grid.SetColumn(MiniRestore, vertical ? 0 : 1);
        foreach (Button button in StackRail.Children) { button.Width = vertical ? 66 : 54; button.Height = vertical ? 54 : 66; }
        Frame.Background = Brushes.White;
        WindowPlacement.Place(this, rect, scale);
    }
    private void SetDockVisibility(DockVisibility value)
    {
        Host.Settings.Visibility = value; expanded = pinned = showAll = false;
        PinButton.Content = "○";
        if (value == DockVisibility.Visible && libraryDirty) RefreshFiles();
        SaveSettings(); ApplyPresentation();
    }
    private void RestoreDock() { SetDockVisibility(DockVisibility.Visible); if (!desktopMode) Expand(); }
    private void RestoreDockClick(object sender, RoutedEventArgs e) => RestoreDock();
    private void MinimizeDock(object sender, RoutedEventArgs e) => SetDockVisibility(DockVisibility.Minimized);
    private void HideDock(object sender, RoutedEventArgs e) => SetDockVisibility(DockVisibility.Hidden);
    private void DockDragStarted(object sender, DragStartedEventArgs e) { dragging = true; expanded = pinned = false; ApplyPresentation(); }
    private void DockDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!GetCursorPos(out var point)) return;
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
        var snap = DockLayout.Snap(WindowPlacement.Area(screen), point.X, point.Y, WindowPlacement.Scale(this, screen), Host.Settings.Visibility == DockVisibility.Minimized ? 112 : 620);
        Host.Settings.Edge = snap.Edge; Host.Settings.DockOffset = snap.Offset; Host.Settings.Monitor = screen.DeviceName;
        PlaceDock();
    }
    private void DockDragCompleted(object sender, DragCompletedEventArgs e) { dragging = false; SaveSettings(); BuildRail(); ApplyDeferredContext(); ApplyPresentation(); }
    private void SaveSettings()
    {
        try { JsonStore.Write(Host.SettingsPath, Host.Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = "設定を保存できません: " + ex.Message; }
    }
    private void WindowEnter(object sender, MouseEventArgs e) => closeTimer.Stop();
    private void WindowLeave(object sender, MouseEventArgs e) { closeTimer.Interval = TimeSpan.FromMilliseconds(Host.Settings.CloseDelayMs); closeTimer.Stop(); closeTimer.Start(); }
    private void TogglePin(object sender, RoutedEventArgs e) { pinned = !pinned; PinButton.Content = pinned ? "●" : "○"; }
    private void ToggleAllFiles(object sender, RoutedEventArgs e) { showAll = !showAll; SearchBox.Text = ""; ApplyPresentation(); }
    private void ClosePanel(object sender, RoutedEventArgs e) => Collapse();
    private void WindowKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Collapse(); e.Handled = true; } }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (expanded) { RenderFiles(); if (desktopMode) PlaceDock(); } }
    private void OrderChanged(object sender, SelectionChangedEventArgs e) { if (ready && OrderBox.SelectedIndex >= 0) { Host.Settings.Order = (FileOrder)OrderBox.SelectedIndex; SaveSettings(); RefreshFiles(); } }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);

    private void OpenFile(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is DesktopItem item) Launch(item.Path);
    }
    private void Launch(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { ShowMessage("ファイルを開けませんでした。\n" + ex.Message); RefreshFiles(); }
    }
    private DesktopItem? MenuItemFile(object sender) => ((sender as MenuItem)?.Parent as ContextMenu)?.PlacementTarget is Button button ? button.Tag as DesktopItem : null;
    private void FileMenuOpened(object sender, RoutedEventArgs e)
    {
        modalDepth++;
        var menu = (ContextMenu)sender;
        ((MenuItem)menu.Items[1]).IsEnabled = menu.PlacementTarget is Button button && button.Tag is DesktopItem { IsStored: true };
    }
    private void EndModal()
    {
        modalDepth = Math.Max(0, modalDepth - 1); ApplyDeferredContext(); ApplyPresentation();
        if (!IsMouseOver && !pinned) WindowLeave(this, null!);
    }
    private void FileMenuClosed(object sender, RoutedEventArgs e) => EndModal();
    private void RevealFile(object sender, RoutedEventArgs e)
    {
        if (MenuItemFile(sender) is not { } item) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + item.Path + "\"") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { ShowMessage(ex.Message); }
    }
    private async void RestoreFile(object sender, RoutedEventArgs e)
    {
        if (MenuItemFile(sender)?.RecordId is { } id) await RunOperation(() => Host.Library.Restore([id]), "復元");
    }

    private async void OrganizeDesktop(object sender, RoutedEventArgs e)
    {
        RefreshFiles();
        var items = allItems.Where(x => x.CanOrganize).ToList();
        if (items.Count == 0) return;
        modalDepth++;
        var answer = MessageBox.Show(this, $"デスクトップの {items.Count} 個のファイル・フォルダーを Lateraroot に収納します。\n\n収納後もスタックから開けます。「元に戻す」で復元できます。\n共有デスクトップ・隠し項目は移動しません。\n\n保存先: {Host.Library.VaultPath}\n\n実行しますか？", "デスクトップを整理", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        EndModal();
        if (answer == MessageBoxResult.Yes) await RunOperation(() => Host.Library.Organize(items), "収納");
    }

    private async void RestoreStack(object sender, RoutedEventArgs e)
    {
        var ids = allItems.Where(x => x.StackId == selectedStack && x.RecordId is not null).Select(x => x.RecordId!).ToList();
        await RunOperation(() => Host.Library.Restore(ids), "復元");
    }

    private async Task RunOperation(Func<OperationResult> operation, string verb)
    {
        if (busy) return;
        busy = true; Panel.IsEnabled = false;
        StatusText.Text = verb + "中…";
        try
        {
            var result = await Task.Run(operation);
            StatusText.Text = $"{result.Succeeded} 個を{verb}しました。";
            if (result.Errors.Count > 0) ShowMessage($"{result.Errors.Count} 個の項目を処理できませんでした。\n" + string.Join("\n", result.Errors.Take(5)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowMessage(verb + "できませんでした。\n" + ex.Message); }
        finally { busy = false; Panel.IsEnabled = true; RefreshFiles(); ApplyDeferredContext(); if (!IsMouseOver && !pinned) WindowLeave(this, null!); }
    }

    private void ShowMessage(string message)
    {
        modalDepth++;
        try { MessageBox.Show(this, message, "Lateraroot", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { EndModal(); }
    }

    private void ShowSettings(object sender, RoutedEventArgs e) => OpenSettings();
    private void OpenSettings()
    {
        if (busy) return;
        modalDepth++; closeTimer.Stop();
        try
        {
            var dialog = new SettingsWindow { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                RestartWatchers();
                RefreshFiles(); ApplyPresentation();
            }
        }
        finally { EndModal(); }
    }

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == 1) { if (Host.Settings.Visibility != DockVisibility.Visible) RestoreDock(); else if (expanded) Collapse(); else { Expand(); Activate(); if (!desktopMode) SearchBox.Focus(); } handled = true; }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

public sealed record FileTile(DesktopItem Item, ImageSource Icon, Brush Background)
{
    public string Caption => (Item.IsStored ? "収納済み · " : "") + Item.Detail;
    public string Tooltip => Item.Name + "\n" + Item.Path + "\n" + Item.Modified.ToString("yyyy/MM/dd HH:mm") + (Item.IsStored ? "\nLateraroot に収納済み" : "");
}
