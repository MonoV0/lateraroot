namespace Lateraroot.Core;

public enum UpdateStage { Idle, Unsupported, Checking, Current, Available, Downloading, Ready, Applying, Failed }
public sealed record UpdateRelease(string Version, string Notes);
public sealed record UpdateSnapshot(UpdateStage Stage, string CurrentVersion, string? TargetVersion = null, int Progress = 0, string? Error = null)
{
    public bool IsBusy => Stage is UpdateStage.Checking or UpdateStage.Downloading or UpdateStage.Applying;
}
public interface IUpdateProvider
{
    bool IsSupported { get; }
    string CurrentVersion { get; }
    string? ReadyVersion { get; }
    Task<UpdateRelease?> CheckAsync();
    Task DownloadAsync(UpdateRelease release, IProgress<int> progress, CancellationToken cancellation);
    void ScheduleRestart();
}
public sealed class UpdateFailureException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>One session shared by tray, settings and automatic checks. No concurrent downloads or stale progress.</summary>
public sealed class UpdateSession
{
    private readonly IUpdateProvider provider;
    private readonly object stateLock = new();
    private int active;
    private int downloadGeneration;
    private UpdateSnapshot snapshot;
    public UpdateRelease? Release { get; private set; }
    public UpdateSnapshot Snapshot { get { lock (stateLock) return snapshot; } }
    public event Action? Changed;

    public UpdateSession(IUpdateProvider provider)
    {
        this.provider = provider;
        snapshot = new(!provider.IsSupported ? UpdateStage.Unsupported : provider.ReadyVersion is null ? UpdateStage.Idle : UpdateStage.Ready, provider.CurrentVersion, provider.ReadyVersion);
    }
    public async Task CheckAsync()
    {
        if (!provider.IsSupported || Interlocked.CompareExchange(ref active, 1, 0) != 0) return;
        Set(new(UpdateStage.Checking, provider.CurrentVersion));
        try
        {
            Release = await provider.CheckAsync().ConfigureAwait(false);
            bool ready = provider.ReadyVersion is { } cached && (Release is null || Release.Version == cached);
            Set(new(ready ? UpdateStage.Ready : Release is null ? UpdateStage.Current : UpdateStage.Available, provider.CurrentVersion, ready ? provider.ReadyVersion : Release?.Version));
        }
        catch (Exception ex) { Set(new(provider.ReadyVersion is null ? UpdateStage.Failed : UpdateStage.Ready, provider.CurrentVersion, provider.ReadyVersion, Error: Friendly(ex))); }
        finally { Interlocked.Exchange(ref active, 0); }
    }
    public async Task DownloadAsync(CancellationToken cancellation = default)
    {
        if (Snapshot.Stage != UpdateStage.Available || Release is not { } release || Interlocked.CompareExchange(ref active, 1, 0) != 0) return;
        int generation = Interlocked.Increment(ref downloadGeneration);
        Set(new(UpdateStage.Downloading, provider.CurrentVersion, release.Version));
        try
        {
            await provider.DownloadAsync(release, new ProgressSink(value =>
            {
                bool changed = false;
                lock (stateLock)
                {
                    if (generation == downloadGeneration && snapshot.Stage == UpdateStage.Downloading)
                    {
                        snapshot = snapshot with { Progress = Math.Max(snapshot.Progress, Math.Clamp(value, 0, 100)) }; changed = true;
                    }
                }
                if (changed) Changed?.Invoke();
            }), cancellation).ConfigureAwait(false);
            Set(new(UpdateStage.Ready, provider.CurrentVersion, release.Version, 100));
        }
        catch (OperationCanceledException) { Set(new(UpdateStage.Available, provider.CurrentVersion, release.Version, Error: "ダウンロードを中止しました。")); }
        catch (Exception ex) { Set(new(UpdateStage.Available, provider.CurrentVersion, release.Version, Error: Friendly(ex))); }
        finally { Interlocked.Exchange(ref active, 0); }
    }
    public bool BeginRestart()
    {
        if (Snapshot.Stage != UpdateStage.Ready || Interlocked.CompareExchange(ref active, 1, 0) != 0) return false;
        var ready = Snapshot;
        Set(ready with { Stage = UpdateStage.Applying, Error = null });
        try { provider.ScheduleRestart(); return true; }
        catch (Exception ex) { Set(ready with { Error = Friendly(ex) }); Interlocked.Exchange(ref active, 0); return false; }
    }
    private void Set(UpdateSnapshot value) { lock (stateLock) snapshot = value; Changed?.Invoke(); }
    private static string Friendly(Exception ex) => ex switch
    {
        UpdateFailureException => ex.Message,
        HttpRequestException or TaskCanceledException => "通信できませんでした。接続を確認して再試行してください。",
        UnauthorizedAccessException => "更新先に書き込めません。書き込み可能なフォルダーへ展開して起動してください。",
        IOException => "更新ファイルを保存できません。空き容量やファイルの使用状態を確認してください。",
        _ => "更新を完了できませんでした。再試行するか、リリースページから更新してください。"
    };
    private sealed class ProgressSink(Action<int> callback) : IProgress<int> { public void Report(int value) => callback(value); }
}
