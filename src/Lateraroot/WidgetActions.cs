using System;
using System.Windows.Controls;

namespace Lateraroot;

/// <summary>Only the organizer's explicit actions. Shared by desktop and compact widgets.</summary>
internal sealed record WidgetActions(Action<string> Select, Action Settings, Action Inspector, Action Refresh, Action ToggleLock,
    Action Minimize, Action Hide, Action<bool> MenuChanged)
{
    public ContextMenu CreateMenu(string? genre = null)
    {
        var menu = new ContextMenu();
        void Add(string title, Action action, string icon) { var item = new MenuItem { Header = title, Icon = new UiIcon { Kind = icon, Width = 14, Height = 14 } }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        if (genre is not null) Add("開く", () => Select(genre), "ArrowOut");
        Add("検索・整理", () => { if (genre is not null) Select(genre); Inspector(); }, "SearchIcon");
        Add("一覧を再読み込み", Refresh, "ReloadIcon");
        var positionLock = new MenuItem { Header = "配置をロック", IsCheckable = true };
        positionLock.Click += (_, _) => ToggleLock(); menu.Items.Add(positionLock);
        Add("ウィジェット設定", Settings, "SettingsIcon");
        Add("最小化", Minimize, "MinusIcon"); Add("非表示", Hide, "HideIcon");
        menu.Opened += (_, _) => { positionLock.IsChecked = App.Current.Settings.LockPosition; MenuChanged(true); };
        menu.Closed += (_, _) => MenuChanged(false);
        return menu;
    }
}
