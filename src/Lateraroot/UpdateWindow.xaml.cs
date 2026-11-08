using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Lateraroot.Core;

namespace Lateraroot;

public partial class UpdateWindow : Window
{
    private readonly UpdateSession session;
    private readonly Action? saveDraft;
    private CancellationTokenSource? download;
    internal static void ShowFor(Window owner, Action? saveDraft = null)
    {
        var existing = App.Current.Windows.OfType<UpdateWindow>().FirstOrDefault();
        if (existing is not null) { existing.Activate(); return; }
        new UpdateWindow(App.Current.Updates, saveDraft) { Owner = owner }.ShowDialog();
    }
    public UpdateWindow() : this(App.Current.Updates, null) { }
    internal UpdateWindow(UpdateSession session, Action? saveDraft)
    {
        this.session = session; this.saveDraft = saveDraft;
        InitializeComponent(); session.Changed += Changed;
        GlassSurface.SetReduced(this, App.Current.Settings.ReduceTransparency || SystemParameters.HighContrast);
        Loaded += async (_, _) => { Render(); if (session.Snapshot.Stage is UpdateStage.Idle or UpdateStage.Failed) await session.CheckAsync(); };
        Closing += (_, e) => { if (session.Snapshot.Stage == UpdateStage.Applying) e.Cancel = true; else download?.Cancel(); };
        Closed += (_, _) => { session.Changed -= Changed; download?.Dispose(); };
        if (saveDraft is not null) InstallHint.Text = "設定画面の変更を保存してから更新・再起動します。収納済みファイルは引き継がれます。";
    }
    private void Changed()
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Render);
    }
    private void Render()
    {
        var state = session.Snapshot;
        VersionText.Text = "現在のバージョン  " + state.CurrentVersion;
        UpdateTitle.Text = state.Stage switch
        {
            UpdateStage.Unsupported => "更新対応版から起動してください",
            UpdateStage.Checking => "新しいバージョンを確認しています",
            UpdateStage.Current => "最新バージョンです",
            UpdateStage.Available => $"バージョン {state.TargetVersion} が利用できます",
            UpdateStage.Downloading => $"ダウンロード中  {state.Progress}%",
            UpdateStage.Ready => $"バージョン {state.TargetVersion} の準備ができました",
            UpdateStage.Applying => "アプリを終了して更新します",
            UpdateStage.Failed => "更新を確認できませんでした",
            _ => "新しいバージョンを確認できます"
        };
        UpdateStatus.Text = state.Error ?? (state.Stage == UpdateStage.Unsupported ? "更新対応版のZIPをすべて展開して、フォルダー直下の Lateraroot.exe を起動してください。" : state.Stage == UpdateStage.Current ? "次のリリースも、この画面から更新できます。" : state.Stage == UpdateStage.Ready ? "検証済みの更新を適用して、自動で再起動します。" : "更新元: GitHub / MonoV0/lateraroot");
        NotesText.Text = session.Release?.Notes ?? "";
        DownloadProgress.Visibility = state.Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Applying ? Visibility.Visible : Visibility.Collapsed;
        DownloadProgress.IsIndeterminate = state.Stage is UpdateStage.Checking or UpdateStage.Applying;
        DownloadProgress.Value = state.Progress;
        UpdateAction.Content = state.Stage switch { UpdateStage.Available => "ダウンロード", UpdateStage.Downloading => "ダウンロード中", UpdateStage.Ready => saveDraft is null ? "更新して再起動" : "設定を保存して更新", UpdateStage.Applying => "更新しています", _ => "更新を確認" };
        UpdateAction.IsEnabled = !state.IsBusy && state.Stage != UpdateStage.Unsupported;
        DismissButton.Content = state.Stage == UpdateStage.Downloading ? "中止" : "閉じる";
        DismissButton.IsEnabled = state.Stage != UpdateStage.Applying;
    }
    internal async Task PerformActionAsync()
    {
        if (session.Snapshot.Stage == UpdateStage.Ready)
        {
            if (App.Current.MainWindow is MainWindow { CanRestartForUpdate: false }) { UpdateStatus.Text = "ファイル処理が完了してから更新してください。"; return; }
            try
            {
                if (saveDraft is not null) saveDraft();
                else JsonStore.Write(App.Current.SettingsPath, App.Current.Settings);
                if (session.BeginRestart()) App.Current.Shutdown();
            }
            catch (Exception ex) when (ex is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException) { UpdateStatus.Text = "設定を保存できませんでした。" + ex.Message; }
            return;
        }
        if (session.Snapshot.Stage == UpdateStage.Available)
        {
            download?.Dispose(); download = new();
            await session.DownloadAsync(download.Token);
        }
        else await session.CheckAsync();
    }
    private async void Act(object sender, RoutedEventArgs e) => await PerformActionAsync();
    private void Dismiss(object sender, RoutedEventArgs e) { if (session.Snapshot.Stage == UpdateStage.Downloading) download?.Cancel(); else Close(); }
    private void OpenReleases(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppInfo.Releases) { UseShellExecute = true }); }
        catch (Win32Exception) { UpdateStatus.Text = "ブラウザーを開けませんでした。GitHubのリリースページから確認してください。"; }
    }
}
