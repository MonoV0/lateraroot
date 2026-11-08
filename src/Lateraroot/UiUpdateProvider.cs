using System;
using System.Threading;
using System.Threading.Tasks;
using Lateraroot.Core;

namespace Lateraroot;

internal sealed class UiUpdateProvider : IUpdateProvider
{
    public bool IsSupported => true;
    public string CurrentVersion => AppInfo.Version;
    public string? ReadyVersion { get; private set; }
    public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<UpdateRelease?> CheckAsync()
    {
        var current = Version.Parse(AppInfo.Version);
        return Task.FromResult<UpdateRelease?>(new($"{current.Major}.{current.Minor}.{current.Build + 1}", "ウィジェットの動作改善と不具合の修正。\n\n設定と収納済みファイルは引き継がれます。"));
    }
    public async Task DownloadAsync(UpdateRelease release, IProgress<int> progress, CancellationToken cancellation)
    { progress.Report(48); await Finish.Task.WaitAsync(cancellation); ReadyVersion = release.Version; }
    public void ScheduleRestart() => throw new InvalidOperationException("UI verification never applies an update");
}
