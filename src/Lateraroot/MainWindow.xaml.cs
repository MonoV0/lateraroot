using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
    private bool expanded, pinned, ready, busy;
    private int modalDepth;
    private HwndSource? source;
    private App Host => App.Current;

    public MainWindow()
    {
        selectedStack = Host.Settings.Stacks[0].Id;
        InitializeComponent();
        // Keep the demo discoverable to desktop accessibility and inspection tools.
        ShowInTaskbar = Host.IsSandbox;
        OrderBox.SelectedIndex = (int)Host.Settings.Order;
        closeTimer.Tick += (_, _) => { closeTimer.Stop(); if (!IsMouseOver && !pinned && modalDepth == 0 && !busy) Collapse(); };
        refreshTimer.Tick += (_, _) => { refreshTimer.Stop(); if (!busy) RefreshFiles(); };
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Lateraroot — 画面端のスタック", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("スタックを開く  (Ctrl+Alt+Space)", null, (_, _) => Dispatcher.Invoke(() => { Expand(); Activate(); }));
        menu.Items.Add("設定", null, (_, _) => Dispatcher.Invoke(() => OpenSettings()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Dispatcher.Invoke(() => Host.Shutdown()));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { Expand(); Activate(); });
        Loaded += (_, _) =>
        {
            Host.Trace("Window loaded");
            ready = true;
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowMessage);
            if (!RegisterHotKey(new WindowInteropHelper(this).Handle, 1, 0x4003, 0x20)) StatusText.Text = "ホットキーを登録できませんでした。通知領域から開けます。";
            Host.Trace("Refreshing files");
            RefreshFiles();
            Host.Trace("Placing dock");
            PlaceDock();
            Host.Trace("Dock placed");
            Watch(Host.DesktopDirectory, false); Watch(Host.Library.VaultPath, true);
            if (!Host.IsSandbox && Host.Settings.IncludePublicDesktop) Watch(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceDock);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplayChanged;
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            closeTimer.Stop(); refreshTimer.Stop();
            foreach (var watcher in watchers) watcher.Dispose();
            tray.Dispose();
            UnregisterHotKey(new WindowInteropHelper(this).Handle, 1);
            source?.RemoveHook(WindowMessage);
        };
    }

    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(PlaceDock);

    private void Watch(string path, bool recursive)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = recursive, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            FileSystemEventHandler changed = (_, _) => QueueRefresh();
            watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed; watcher.Renamed += (_, _) => QueueRefresh();
            watcher.Error += (_, _) => QueueRefresh();
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }
        catch (IOException) { StatusText.Text = "フォルダーの監視を開始できませんでした。スタックを開くと再読み込みします。"; }
        catch (UnauthorizedAccessException) { StatusText.Text = "読み取り権限のないフォルダーがあります。"; }
    }

    private void QueueRefresh()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() => { refreshTimer.Stop(); refreshTimer.Start(); });
    }

    private void RefreshFiles()
    {
        if (!ready || busy) return;
        allItems = Host.Library.Scan(Host.Settings);
        if (!Host.Settings.Stacks.Any(x => x.Id == selectedStack)) selectedStack = Host.Settings.Stacks[0].Id;
        BuildRail(); RenderFiles();
    }

    private void BuildRail()
    {
        StackRail.Children.Clear();
        foreach (var stack in Host.Settings.Stacks)
        {
            var content = new Grid { Height = 42, Width = 36 };
            content.Children.Add(new TextBlock { Text = stack.Symbol, FontSize = 23, Foreground = (Brush)new BrushConverter().ConvertFromString(stack.Color)!, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top });
            content.Children.Add(new TextBlock { Text = allItems.Count(x => x.StackId == stack.Id).ToString(), FontSize = 9, Foreground = (Brush)FindResource("Muted"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom });
            var button = new Button { Content = content, Height = 54, Padding = new Thickness(4, 5, 4, 4), Margin = new Thickness(0, 0, 0, 4), Background = new SolidColorBrush(stack.Id == selectedStack && expanded ? Color.FromRgb(48, 53, 77) : Color.FromRgb(18, 22, 32)), ToolTip = stack.Name };
            System.Windows.Automation.AutomationProperties.SetName(button, stack.Name + " スタック");
            button.MouseEnter += (_, _) => SelectStack(stack.Id);
            button.Click += (_, _) => { SelectStack(stack.Id); Activate(); };
            StackRail.Children.Add(button);
        }
    }

    private void SelectStack(string id)
    {
        bool changed = selectedStack != id;
        selectedStack = id;
        if (changed) SearchBox.Text = "";
        if (!expanded) Expand(); else { RenderFiles(); UpdateRailHighlight(); }
    }

    private void UpdateRailHighlight()
    {
        for (int i = 0; i < StackRail.Children.Count; i++)
            ((Button)StackRail.Children[i]).Background = new SolidColorBrush(Host.Settings.Stacks[i].Id == selectedStack && expanded ? Color.FromRgb(48, 53, 77) : Color.FromRgb(18, 22, 32));
    }

    private void RenderFiles()
    {
        if (!ready) return;
        var stack = Host.Settings.Stacks.First(x => x.Id == selectedStack);
        var items = allItems.Where(x => x.StackId == selectedStack).ToList();
        var filtered = items.Where(x => x.Name.Contains(SearchBox.Text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        StackTitle.Text = stack.Name;
        StackTitle.Foreground = (Brush)new BrushConverter().ConvertFromString(stack.Color)!;
        StackSubtitle.Text = $"{items.Count} 個の項目  ·  {items.Count(x => x.IsStored)} 個を収納済み";
        SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        FileItems.ItemsSource = filtered.Select(item => new FileTile(item, ShellIcons.Get(item.Path, item.IsDirectory))).ToList();
        EmptyState.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = SearchBox.Text.Length > 0 ? "見つかりませんでした。" : "ここは、まだ静かです。";
        EmptyDescription.Text = SearchBox.Text.Length > 0 ? "別のファイル名で検索してください。" : "デスクトップに対象のファイルを置くと表示されます。\n分類ルールは設定から変更できます。";
        RestoreButton.IsEnabled = items.Any(x => x.IsStored);
        OrganizeButton.IsEnabled = allItems.Any(x => x.CanOrganize);
    }

    private void Expand()
    {
        closeTimer.Stop();
        if (expanded) return;
        expanded = true;
        Panel.Visibility = Visibility.Visible;
        PlaceDock(); RefreshFiles();
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void Collapse()
    {
        if (modalDepth > 0 || busy) return;
        expanded = false; pinned = false;
        PinButton.Content = "○";
        Panel.Visibility = Visibility.Collapsed;
        PlaceDock(); UpdateRailHighlight();
    }

    private void PlaceDock()
    {
        if (!ready) return;
        var screen = Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == Host.Settings.Monitor) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var area = screen.WorkingArea;
        var handle = new WindowInteropHelper(this).Handle;
        // Move first so WPF receives WM_DPICHANGED, then size using the target monitor's DPI.
        if (Forms.Screen.FromHandle(handle).DeviceName != screen.DeviceName)
            SetWindowPos(handle, IntPtr.Zero, area.Left + 4, area.Top + 4, 0, 0, 0x0015);
        var scale = GetDpiForWindow(handle) / 96d;
        if (scale <= 0) scale = 1;
        double maxWidth = Math.Max(240, area.Width / scale - 12);
        double panelWidth = Math.Min(408, maxWidth - 68);
        double width = expanded ? panelWidth + 68 : 68;
        double height = Math.Min(620, area.Height / scale - 16);
        Width = width; Height = height;
        var leftEdge = Host.Settings.Edge == DockEdge.Left;
        Grid.SetColumn(Rail, leftEdge ? 0 : 1); Grid.SetColumn(Panel, leftEdge ? 1 : 0);
        PanelColumn.Width = new GridLength(leftEdge ? 66 : expanded ? panelWidth : 0);
        RailColumn.Width = new GridLength(leftEdge ? expanded ? panelWidth : 0 : 66);
        var pixelsWide = (int)Math.Round(width * scale);
        var pixelsHigh = (int)Math.Round(height * scale);
        int x = leftEdge ? area.Left : area.Right - pixelsWide;
        int y = area.Top + (area.Height - pixelsHigh) / 2;
        SetWindowPos(handle, new IntPtr(-1), x, y, pixelsWide, pixelsHigh, 0x0010);
    }

    private void WindowEnter(object sender, MouseEventArgs e) => closeTimer.Stop();
    private void WindowLeave(object sender, MouseEventArgs e)
    {
        closeTimer.Interval = TimeSpan.FromMilliseconds(Host.Settings.CloseDelayMs);
        closeTimer.Stop(); closeTimer.Start();
    }
    private void TogglePin(object sender, RoutedEventArgs e) { pinned = !pinned; PinButton.Content = pinned ? "●" : "○"; StatusText.Text = pinned ? "パネルを固定中 · Esc で閉じる" : "ホバーで開く · 離れると閉じる"; }
    private void ClosePanel(object sender, RoutedEventArgs e) => Collapse();
    private void WindowKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Collapse(); e.Handled = true; } }
    private void SearchChanged(object sender, TextChangedEventArgs e) => RenderFiles();
    private void OrderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || OrderBox.SelectedIndex < 0) return;
        Host.Settings.Order = (FileOrder)OrderBox.SelectedIndex;
        try { JsonStore.Write(Host.SettingsPath, Host.Settings); RefreshFiles(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = "並び順を保存できませんでした: " + ex.Message; }
    }

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
    private void FileMenuClosed(object sender, RoutedEventArgs e) { modalDepth = Math.Max(0, modalDepth - 1); WindowLeave(this, null!); }
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
        modalDepth--;
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
        finally { busy = false; Panel.IsEnabled = true; RefreshFiles(); if (!IsMouseOver && !pinned) WindowLeave(this, null!); }
    }

    private void ShowMessage(string message)
    {
        modalDepth++;
        try { MessageBox.Show(this, message, "Lateraroot", MessageBoxButton.OK, MessageBoxImage.Information); }
        finally { modalDepth--; }
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
                // Subscribe again when shared-desktop visibility changes.
                foreach (var watcher in watchers) watcher.Dispose();
                watchers.Clear();
                Watch(Host.DesktopDirectory, false); Watch(Host.Library.VaultPath, true);
                if (!Host.IsSandbox && Host.Settings.IncludePublicDesktop) Watch(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), false);
                RefreshFiles(); PlaceDock();
            }
        }
        finally { modalDepth--; if (!IsMouseOver && !pinned) WindowLeave(this, null!); }
    }

    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam.ToInt32() == 1) { if (expanded) Collapse(); else { Expand(); Activate(); SearchBox.Focus(); } handled = true; }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}

public sealed record FileTile(DesktopItem Item, ImageSource Icon)
{
    public string Caption => (Item.IsStored ? "収納済み · " : "") + Item.Detail;
    public string Tooltip => Item.Name + "\n" + Item.Path + "\n" + Item.Modified.ToString("yyyy/MM/dd HH:mm") + (Item.IsStored ? "\nLateraroot に収納済み" : "");
}
