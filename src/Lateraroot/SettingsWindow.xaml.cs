using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Text.RegularExpressions;
using Lateraroot.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Lateraroot;

public partial class SettingsWindow : Window
{
    private readonly AppSettings draft;
    private ObservableCollection<StackRule> rules;
    private bool startupWasEnabled, loading = true;
    internal StackRule? SelectedRule => RulesList.SelectedItem as StackRule;

    public SettingsWindow()
    {
        draft = JsonStore.Clone(App.Current.Settings); rules = new(draft.Stacks);
        InitializeComponent();
        RulesList.ItemsSource = rules;
        GenreIconBox.ItemsSource = Enum.GetValues<GenreIcon>().Select((icon, i) => new IconChoice(icon, new[] { "自動（分類に合わせる）", "画像", "書類", "メディア", "アーカイブ", "ゲーム", "ショートカット", "フォルダー", "その他" }[i])).ToList();
        IconOnlyBox.IsChecked = draft.GenreIconsOnly;
        SortBox.SelectedIndex = (int)draft.Order; SortDirectionBox.SelectedIndex = draft.EffectiveDescending ? 1 : 0;
        SizeBox.ItemsSource = Enumerable.Range(9, 6).Select(i => new SizeChoice(i * 8, $"{i * 8} × {i * 8} DIP")).ToList();
        SizeBox.SelectedValue = draft.TileSize; OffsetBox.Value = draft.DockOffset;
        ReduceMotionBox.IsChecked = draft.ReduceMotion; FollowSystemBox.IsChecked = draft.FollowSystemAnimations;
        TransparencyBox.IsChecked = draft.ReduceTransparency; UpdatesBox.IsChecked = draft.CheckForUpdates;
        LiquidGlassBox.IsChecked = draft.LiquidGlass; GlassStatus.Text = GlassBackdrop.Status;
        LockPositionBox.IsChecked = draft.LockPosition;
        double scale = WindowPlacement.Scale(this, WindowPlacement.Screen);
        MaxHeight = Math.Max(340, WindowPlacement.Screen.WorkingArea.Height / scale - 24);
        MinHeight = Math.Min(MinHeight, MaxHeight); Height = Math.Min(Height, MaxHeight);
        EdgeBox.SelectedIndex = (int)draft.Edge;
        MonitorBox.ItemsSource = new[] { new MonitorChoice("", "メインディスプレイ") }.Concat(Forms.Screen.AllScreens.Select((s, i) => new MonitorChoice(s.DeviceName, $"{i + 1}: {s.Bounds.Width} × {s.Bounds.Height}"))).ToList();
        MonitorBox.SelectedValue = draft.Monitor; if (MonitorBox.SelectedIndex < 0) MonitorBox.SelectedIndex = 0;
        DelayBox.SelectedItem = DelayBox.Items.Cast<ComboBoxItem>().MinBy(x => Math.Abs(int.Parse((string)x.Tag) - draft.CloseDelayMs));
        PublicBox.IsChecked = draft.IncludePublicDesktop; PublicBox.IsEnabled = !App.Current.IsSandbox;
        startupWasEnabled = !App.Current.IsSandbox && StartupEnabled();
        StartupBox.IsChecked = startupWasEnabled; StartupBox.IsEnabled = !App.Current.IsSandbox;
        SandboxLabel.Text = App.Current.IsPreview ? "ガラスのテスト版 · デスクトップは読み取り専用。収納・復元は無効です。" : App.Current.IsSandbox ? "デモモード · 実際のデスクトップには触れません" : $"Lateraroot {AppInfo.Version} · MIT / Font: SIL OFL";
        loading = false; RulesList.SelectedIndex = 0; ApplyTransparency();
    }

    private void GenreSelected(object sender, SelectionChangedEventArgs e)
    {
        if (loading || SelectedRule is not { } rule) return;
        loading = true;
        ColorBox.Text = rule.Color.ToUpperInvariant();
        KindBox.SelectedIndex = rule.Tokens.Contains("*") ? 2 : rule.Filter.Trim() == "<folders>" ? 1 : rule.Filter.Trim() == "<games>" ? 3 : 0;
        GenreIconBox.SelectedValue = rule.Icon;
        KindBox.IsEnabled = !rule.Tokens.Contains("*");
        loading = false; UpdateEditor(); SaveError.Text = "";
    }
    private void UpdateEditor()
    {
        bool fallback = SelectedRule?.Tokens.Contains("*") == true;
        FilterBox.Visibility = KindBox.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterHint.Text = KindBox.SelectedIndex switch { 3 => "Steam・Epicなどのゲーム用ショートカットを、起動情報から判定します。ゲーム本体の検索・移動や起動はしません。上にあるルールが優先されます。", 2 => "ほかのジャンルに当てはまらないファイルを表示します。このジャンルは1つ残します。", 1 => "デスクトップ直下のフォルダーを表示します。", _ => "例: .pdf, .docx, .txt  — 拡張子をカンマで区切ります。上のジャンルのルールを優先します。" };
        UpdateIconPreview();
        RemoveButton.IsEnabled = !fallback && rules.Count > 1;
        AddButton.IsEnabled = rules.Count < 16;
        UpButton.IsEnabled = RulesList.SelectedIndex > 0;
        DownButton.IsEnabled = RulesList.SelectedIndex >= 0 && RulesList.SelectedIndex < rules.Count - 1;
        bool validColor = Regex.IsMatch(ColorBox.Text, "^#[0-9A-Fa-f]{6}$");
        ColorError.Text = validColor ? "" : "#と6桁の英数字で入力してください（例: #FFFFFF）。"; SaveButton.IsEnabled = validColor;
        foreach (Button swatch in PalettePanel.Children)
            swatch.Content = string.Equals((string)swatch.Tag, SelectedRule?.Color, StringComparison.OrdinalIgnoreCase) ? new TextBlock { Text = "✓", Foreground = WidgetVisuals.Ink(DesktopEdgeWindow.ColorBrush((string)swatch.Tag)) } : null;
    }
    private void KindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || SelectedRule is not { } rule) return;
        if (KindBox.SelectedIndex == 2 && rules.Any(r => r != rule && r.Tokens.Contains("*")))
        {
            loading = true; KindBox.SelectedIndex = rule.Filter.Trim() == "<folders>" ? 1 : rule.Filter.Trim() == "<games>" ? 3 : 0; loading = false;
            SaveError.Text = "未分類用のジャンルは既にあります。一覧からそのジャンルを選んで編集してください。"; return;
        }
        rule.Filter = KindBox.SelectedIndex switch { 1 => "<folders>", 2 => "*", 3 => "<games>", _ => ".txt" };
        UpdateEditor();
    }
    private void ColorEdited(object sender, TextChangedEventArgs e)
    {
        if (loading || SelectedRule is null) return;
        if (Regex.IsMatch(ColorBox.Text, "^#[0-9A-Fa-f]{6}$")) SetSelectedColor(ColorBox.Text);
        else { ColorError.Text = "#と6桁の英数字で入力してください（例: #FFFFFF）。"; SaveButton.IsEnabled = false; }
    }
    internal void SetSelectedColor(string color)
    {
        if (!Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$") || SelectedRule is null) return;
        SelectedRule.Color = color.ToUpperInvariant();
        loading = true; ColorBox.Text = SelectedRule.Color; loading = false; UpdateEditor();
    }
    private void ChooseSwatch(object sender, RoutedEventArgs e) => SetSelectedColor((string)((Button)sender).Tag);
    private sealed record IconChoice(GenreIcon Value, string Label);
    private void IconAppearanceChanged(object sender, RoutedEventArgs e) { if (!loading) UpdateIconPreview(); }
    private void GenreIconChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || SelectedRule is not { } rule || GenreIconBox.SelectedValue is not GenreIcon icon) return;
        rule.Icon = icon; UpdateIconPreview();
    }
    private void UpdateIconPreview()
    {
        if (SelectedRule is not { } rule) return;
        bool only = IconOnlyBox.IsChecked == true;
        foreach (var text in ((Grid)SquarePreview.Child).Children.OfType<TextBlock>()) text.Visibility = only ? Visibility.Collapsed : Visibility.Visible;
        PreviewGenreIcon.Kind = DesktopEdgeWindow.GenreIconKey(rule);
        PreviewGenreIcon.Visibility = only ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ChooseCustomColor(object sender, RoutedEventArgs e)
    {
        if (SelectedRule is null) return;
        using var picker = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(SelectedRule.Color) };
        if (picker.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) == Forms.DialogResult.OK)
            SetSelectedColor($"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}");
    }
    private sealed record DialogOwner(IntPtr Handle) : Forms.IWin32Window;
    private void SizePreviewChanged(object sender, SelectionChangedEventArgs e) { if (SizeBox.SelectedValue is int side && SquarePreview is not null) SquarePreview.Width = SquarePreview.Height = side; }
    private void TransparencyChanged(object sender, RoutedEventArgs e) { if (!loading) ApplyTransparency(); }
    internal void ApplyTransparency() => GlassSurface.SetReduced(this, TransparencyBox.IsChecked == true || SystemParameters.HighContrast);

    internal void AddGenre()
    {
        if (rules.Count >= 16) return;
        var rule = new StackRule();
        rules.Insert(Math.Max(0, rules.Count - 1), rule); RulesList.SelectedItem = rule; RulesList.ScrollIntoView(rule); NameBox.Focus(); NameBox.SelectAll();
    }
    private void AddRule(object sender, RoutedEventArgs e) => AddGenre();
    internal bool RemoveSelectedGenre()
    {
        if (SelectedRule is not { } rule || rule.Tokens.Contains("*") || rules.Count <= 1) return false;
        int index = RulesList.SelectedIndex; rules.Remove(rule); RulesList.SelectedIndex = Math.Min(index, rules.Count - 1); UpdateEditor(); return true;
    }
    private void RemoveRule(object sender, RoutedEventArgs e) => RemoveSelectedGenre();
    private void MoveUp(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown(object sender, RoutedEventArgs e) => Move(1);
    internal void Move(int delta)
    {
        int index = RulesList.SelectedIndex;
        if (index < 0 || index + delta < 0 || index + delta >= rules.Count) return;
        rules.Move(index, index + delta); RulesList.SelectedIndex = index + delta; UpdateEditor();
    }
    internal void ResetColors(object? sender = null, RoutedEventArgs? e = null)
    {
        for (int i = 0; i < rules.Count; i++) rules[i].Color = i % 2 == 0 ? "#FFFFFF" : "#000000";
        GenreSelected(this, null!);
    }
    private void ResetRules(object sender, RoutedEventArgs e) { rules = new(AppSettings.Defaults()); RulesList.ItemsSource = rules; RulesList.SelectedIndex = 0; }
    private void OpenStorage(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(App.Current.Library.VaultPath) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { SaveError.Text = ex.Message; }
    }
    private void SaveSettings(object sender, RoutedEventArgs e)
    {
        try { SaveDraft(); DialogResult = true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidDataException) { SaveError.Text = ex.Message; }
    }
    private void OpenUpdates(object sender, RoutedEventArgs e) => UpdateWindow.ShowFor(this, SaveDraft);
    private void OpenIconCredits(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://www.flaticon.com/uicons") { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception ex) { SaveError.Text = ex.Message; }
    }
    internal void SaveDraft()
    {
        if (!Regex.IsMatch(ColorBox.Text, "^#[0-9A-Fa-f]{6}$")) { ColorBox.Focus(); throw new InvalidDataException("カラーコードを確認するか、色を選んでください。"); }
        draft.Edge = (DockEdge)EdgeBox.SelectedIndex; draft.Monitor = (string?)MonitorBox.SelectedValue ?? "";
        draft.IncludePublicDesktop = PublicBox.IsChecked == true;
        draft.Order = (FileOrder)SortBox.SelectedIndex; draft.SortDescending = SortDirectionBox.SelectedIndex == 1;
        draft.GenreIconsOnly = IconOnlyBox.IsChecked == true;
        draft.CloseDelayMs = int.Parse((string)((ComboBoxItem)DelayBox.SelectedItem).Tag);
        draft.Stacks = rules.ToList(); draft.TileSize = (int?)SizeBox.SelectedValue ?? 88; draft.DockOffset = OffsetBox.Value;
        draft.ReduceMotion = ReduceMotionBox.IsChecked == true; draft.FollowSystemAnimations = FollowSystemBox.IsChecked == true;
        draft.ReduceTransparency = TransparencyBox.IsChecked == true; draft.CheckForUpdates = UpdatesBox.IsChecked == true;
        draft.LiquidGlass = LiquidGlassBox.IsChecked == true;
        draft.LockPosition = LockPositionBox.IsChecked == true;
        bool startupChanged = !App.Current.IsSandbox && (StartupBox.IsChecked == true) != startupWasEnabled;
        draft.Validate();
        if (startupChanged) SetStartup(StartupBox.IsChecked == true);
        try { JsonStore.Write(App.Current.SettingsPath, draft); }
        catch { if (startupChanged) SetStartup(startupWasEnabled); throw; }
        App.Current.Settings = JsonStore.Clone(draft); startupWasEnabled = StartupBox.IsChecked == true;
    }
    private static bool StartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValue("Lateraroot") is string value && !string.IsNullOrWhiteSpace(value);
    }
    private static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled) key.SetValue("Lateraroot", "\"" + Environment.ProcessPath + "\""); else key.DeleteValue("Lateraroot", false);
    }
}
public sealed record MonitorChoice(string Id, string Label);
public sealed record SizeChoice(int Value, string Label);
