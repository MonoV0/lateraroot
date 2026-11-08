using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Lateraroot.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Lateraroot;

public partial class SettingsWindow : Window
{
    private readonly AppSettings draft;
    private ObservableCollection<StackRule> rules;
    private readonly bool startupWasEnabled;

    public SettingsWindow()
    {
        InitializeComponent();
        draft = JsonStore.Clone(App.Current.Settings);
        SizeBox.ItemsSource = Enumerable.Range(9, 6).Select(i => new SizeChoice(i * 8, $"{i * 8} × {i * 8} px")).ToList();
        SizeBox.SelectedValue = draft.TileSize;
        OffsetBox.Value = draft.DockOffset;
        ReduceMotionBox.IsChecked = draft.ReduceMotion;
        double scale = WindowPlacement.Scale(this, WindowPlacement.Screen);
        MaxHeight = Math.Max(340, WindowPlacement.Screen.WorkingArea.Height / scale - 24);
        MinHeight = Math.Min(MinHeight, MaxHeight); Height = Math.Min(Height, MaxHeight);
        rules = new(draft.Stacks);
        RulesGrid.ItemsSource = rules;
        EdgeBox.SelectedIndex = (int)draft.Edge;
        MonitorBox.ItemsSource = new[] { new MonitorChoice("", "メインディスプレイ") }.Concat(Forms.Screen.AllScreens.Select((s, i) => new MonitorChoice(s.DeviceName, $"{i + 1}: {s.Bounds.Width} × {s.Bounds.Height}"))).ToList();
        MonitorBox.SelectedValue = draft.Monitor;
        if (MonitorBox.SelectedIndex < 0) MonitorBox.SelectedIndex = 0;
        DelayBox.SelectedItem = DelayBox.Items.Cast<ComboBoxItem>().MinBy(x => Math.Abs(int.Parse((string)x.Tag) - draft.CloseDelayMs));
        PublicBox.IsChecked = draft.IncludePublicDesktop;
        PublicBox.IsEnabled = !App.Current.IsSandbox;
        startupWasEnabled = !App.Current.IsSandbox && StartupEnabled();
        StartupBox.IsChecked = startupWasEnabled;
        StartupBox.IsEnabled = !App.Current.IsSandbox;
        SandboxLabel.Text = App.Current.IsSandbox ? "デモモード · 実際のデスクトップには触れません" : "Lateraroot 0.3.0 · MIT / Font: SIL OFL";
    }

    private void SizePreviewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SizeBox.SelectedValue is int side && SquarePreview is not null) SquarePreview.Width = SquarePreview.Height = side;
    }

    private void AddRule(object sender, RoutedEventArgs e)
    {
        if (rules.Count >= 16) { MessageBox.Show(this, "スタックは最大16個です。", "Lateraroot"); return; }
        var rule = new StackRule();
        rules.Insert(Math.Max(0, rules.Count - 1), rule); RulesGrid.SelectedItem = rule; RulesGrid.ScrollIntoView(rule);
    }
    private void RemoveRule(object sender, RoutedEventArgs e) { if (RulesGrid.SelectedItem is StackRule rule) rules.Remove(rule); }
    private void MoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => Move(1);
    private void Move(int delta)
    {
        int index = RulesGrid.SelectedIndex;
        if (index >= 0 && index + delta >= 0 && index + delta < rules.Count) { rules.Move(index, index + delta); RulesGrid.SelectedIndex = index + delta; }
    }
    private void ResetRules(object sender, RoutedEventArgs e) { rules = new(AppSettings.Defaults()); RulesGrid.ItemsSource = rules; }
    private void OpenStorage(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(App.Current.Library.VaultPath) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { MessageBox.Show(this, ex.Message, "Lateraroot"); }
    }

    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        RulesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        draft.Edge = (DockEdge)EdgeBox.SelectedIndex;
        draft.Monitor = (string?)MonitorBox.SelectedValue ?? "";
        draft.IncludePublicDesktop = PublicBox.IsChecked == true;
        draft.CloseDelayMs = int.Parse((string)((ComboBoxItem)DelayBox.SelectedItem).Tag);
        draft.Stacks = rules.ToList();
        draft.TileSize = (int?)SizeBox.SelectedValue ?? 88;
        draft.DockOffset = OffsetBox.Value;
        draft.ReduceMotion = ReduceMotionBox.IsChecked == true;
        bool startupChanged = !App.Current.IsSandbox && (StartupBox.IsChecked == true) != startupWasEnabled;
        try
        {
            draft.Validate();
            if (startupChanged) SetStartup(StartupBox.IsChecked == true);
            try { JsonStore.Write(App.Current.SettingsPath, draft); }
            catch { if (startupChanged) SetStartup(startupWasEnabled); throw; }
            App.Current.Settings = draft;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidDataException)
        { MessageBox.Show(this, ex.Message, "設定を保存できませんでした", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("Lateraroot") is string value && !string.IsNullOrWhiteSpace(value);
    }
    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled) key.SetValue("Lateraroot", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("Lateraroot", false);
    }
}

public sealed record MonitorChoice(string Id, string Label);

public sealed record SizeChoice(int Value, string Label);
