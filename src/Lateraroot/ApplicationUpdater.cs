using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Lateraroot.Core;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace Lateraroot;

internal static class AppInfo
{
    public const string Repository = "https://github.com/MonoV0/lateraroot";
    public const string Releases = Repository + "/releases/latest";
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version!.ToString(3);
}

internal sealed class ApplicationUpdater : IUpdateProvider
{
    private readonly UpdateManager manager;
    private readonly bool allowSandbox;
    private readonly string[]? restartArguments;
    public ApplicationUpdater() : this(new(new GithubSource(AppInfo.Repository, accessToken: null, prerelease: false))) { }
    internal ApplicationUpdater(UpdateManager manager, bool allowSandbox = false, string[]? restartArguments = null)
    { this.manager = manager; this.allowSandbox = allowSandbox; this.restartArguments = restartArguments; }
    private UpdateInfo? found;
    public bool IsSupported => manager.IsInstalled && (allowSandbox || !App.Current.IsSandbox);
    public string CurrentVersion => manager.CurrentVersion?.ToString() ?? AppInfo.Version;
    public string? ReadyVersion => manager.IsInstalled ? manager.UpdatePendingRestart?.Version.ToString() : null;
    public async Task<UpdateRelease?> CheckAsync()
    {
        found = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return found is null ? null : new(found.TargetFullRelease.Version.ToString(), found.TargetFullRelease.NotesMarkdown ?? "");
    }
    public async Task DownloadAsync(UpdateRelease release, IProgress<int> progress, CancellationToken cancellation)
    {
        if (found is null || release.Version != found.TargetFullRelease.Version.ToString()) throw new UpdateFailureException("もう一度、更新を確認してください。");
        try { await manager.DownloadUpdatesAsync(found, progress.Report, cancellation).ConfigureAwait(false); }
        catch (ChecksumFailedException ex) { throw new UpdateFailureException("更新ファイルの検証に失敗しました。更新は適用していません。再ダウンロードしてください。", ex); }
        catch (AcquireLockFailedException ex) { throw new UpdateFailureException("別の更新処理が動作中です。完了後に再試行してください。", ex); }
    }
    public void ScheduleRestart() => manager.WaitExitThenApplyUpdates(manager.UpdatePendingRestart, silent: false, restart: true, restartArgs: restartArguments ?? ["--data-dir", App.Current.DataDirectory]);
}
