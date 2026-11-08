using System;
using System.Windows.Controls;
using Lateraroot.Core;

namespace Lateraroot;

/// <summary>Only the organizer's explicit actions. Shared by desktop and compact widgets.</summary>
internal sealed record WidgetActions(Action<string> Select, Action Settings, Action Inspector, Action Refresh, Action ToggleLock,
    Action Minimize, Action Hide, Action<bool> MenuChanged, Action<FileOrder, bool> Sort)
{
    public ContextMenu CreateMenu(string? genre = null)
    {
        var menu = new ContextMenu();
        void Add(string title, Action action, string icon) { var item = new MenuItem { Header = title, Icon = new UiIcon { Kind = icon, Width = 14, Height = 14 } }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        if (genre is not null) Add("開く", () => Select(genre), "ArrowOut");
        Add("検索・整理", () => { if (genre is not null) Select(genre); Inspector(); }, "SearchIcon");
        Add("一覧を再読み込み", Refresh, "ReloadIcon");
        var sorting = new MenuItem { Header = "ファイルの並び順" };
        string[] labels = ["名前", "更新日時", "種類", "サイズ"];
        foreach (FileOrder order in Enum.GetValues<FileOrder>())
        {
            var item = new MenuItem { Header = labels[(int)order], Tag = order, IsCheckable = true };
            item.Click += (_, _) => Sort(order, App.Current.Settings.EffectiveDescending); sorting.Items.Add(item);
        }
        sorting.Items.Add(new Separator());
        var ascending = new MenuItem { Header = "昇順", IsCheckable = true };
        var descending = new MenuItem { Header = "降順", IsCheckable = true };
        ascending.Click += (_, _) => Sort(App.Current.Settings.Order, false);
        descending.Click += (_, _) => Sort(App.Current.Settings.Order, true);
        sorting.Items.Add(ascending); sorting.Items.Add(descending); menu.Items.Add(sorting);
        var positionLock = new MenuItem { Header = "配置をロック", IsCheckable = true };
        positionLock.Click += (_, _) => ToggleLock(); menu.Items.Add(positionLock);
        Add("ウィジェット設定", Settings, "SettingsIcon");
        Add("最小化", Minimize, "MinusIcon"); Add("非表示", Hide, "HideIcon");
        menu.Opened += (_, _) =>
        {
            var settings = App.Current.Settings;
            foreach (object entry in sorting.Items) if (entry is MenuItem { Tag: FileOrder order } item) item.IsChecked = order == settings.Order;
            ascending.IsChecked = !settings.EffectiveDescending; descending.IsChecked = settings.EffectiveDescending;
            positionLock.IsChecked = settings.LockPosition; MenuChanged(true);
        };
        menu.Closed += (_, _) => MenuChanged(false);
        return menu;
    }
}
