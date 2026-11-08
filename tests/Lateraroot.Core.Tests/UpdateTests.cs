using Lateraroot.Core;

internal static class UpdateTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("Update availability, progress, explicit restart and pending restart", () => Run(async () =>
        {
            var p = new Fake(); var s = new UpdateSession(p);
            Need(s.Snapshot.Stage == UpdateStage.Idle && !s.BeginRestart());
            await s.CheckAsync(); Need(s.Snapshot.Stage == UpdateStage.Available && s.Release?.Version == "0.4.1" && p.Restarts == 0);
            await s.DownloadAsync(); Need(s.Snapshot.Stage == UpdateStage.Ready && s.Snapshot.Progress == 100 && p.Restarts == 0);
            Need(s.BeginRestart() && p.Restarts == 1 && s.Snapshot.Stage == UpdateStage.Applying);
            Need(!s.BeginRestart());
            var pending = new UpdateSession(new Fake { ReadyVersion = "0.4.1" }); Need(pending.Snapshot.Stage == UpdateStage.Ready);
        }));
        yield return ("Current, unsupported and failed checks remain recoverable", () => Run(async () =>
        {
            var p = new Fake { Result = null }; var s = new UpdateSession(p);
            await s.CheckAsync(); Need(s.Snapshot.Stage == UpdateStage.Current);
            p.CheckError = new HttpRequestException("private diagnostic"); await s.CheckAsync();
            Need(s.Snapshot.Stage == UpdateStage.Failed && !s.Snapshot.Error!.Contains("private"));
            p.CheckError = null; p.Result = new("0.4.1", "notes"); await s.CheckAsync(); Need(s.Snapshot.Stage == UpdateStage.Available);
            var unsupported = new UpdateSession(new Fake { IsSupported = false }); await unsupported.CheckAsync(); Need(unsupported.Snapshot.Stage == UpdateStage.Unsupported);
        }));
        yield return ("Canceled and rejected downloads never authorize restart and can retry", () => Run(async () =>
        {
            var p = new Fake { DownloadError = new OperationCanceledException() }; var s = new UpdateSession(p);
            await s.CheckAsync(); await s.DownloadAsync(); Need(s.Snapshot.Stage == UpdateStage.Available && !s.BeginRestart());
            p.DownloadError = new UpdateFailureException("checksum rejected"); await s.DownloadAsync(); Need(s.Snapshot.Error == "checksum rejected" && p.Restarts == 0);
            p.DownloadError = null; await s.DownloadAsync(); Need(s.Snapshot.Stage == UpdateStage.Ready);
            p.RestartError = new IOException(); Need(!s.BeginRestart() && s.Snapshot.Stage == UpdateStage.Ready);
            p.RestartError = null; Need(s.BeginRestart());
        }));
        yield return ("Shared update session prevents overlapping work and stale progress", () => Run(async () =>
        {
            var gate = new TaskCompletionSource<UpdateRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var p = new Fake { CheckGate = gate.Task }; var s = new UpdateSession(p);
            var check = s.CheckAsync(); await s.CheckAsync(); await s.DownloadAsync(); Need(p.Checks == 1 && s.Snapshot.Stage == UpdateStage.Checking);
            gate.SetResult(new("0.4.1", "")); await check;
            p.DownloadError = new OperationCanceledException(); await s.DownloadAsync(); var old = p.Progress!;
            p.DownloadError = null; p.DownloadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var downloading = s.DownloadAsync(); p.Progress!.Report(55); p.Progress.Report(-3); old.Report(100);
            Need(s.Snapshot.Progress == 55); await s.CheckAsync(); Need(p.Checks == 1);
            p.DownloadGate.SetResult(); await downloading; old.Report(12); Need(s.Snapshot.Progress == 100);
        }));
        yield return ("Prepared update survives an offline check", () => Run(async () =>
        {
            var s = new UpdateSession(new Fake { ReadyVersion = "0.4.1", CheckError = new HttpRequestException() });
            await s.CheckAsync(); Need(s.Snapshot.Stage == UpdateStage.Ready && s.Snapshot.TargetVersion == "0.4.1");
        }));
    }
    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();
    private static void Need(bool value) { if (!value) throw new Exception("Update assertion failed"); }
    private sealed class Fake : IUpdateProvider
    {
        public bool IsSupported { get; set; } = true;
        public string CurrentVersion => "0.4.0";
        public string? ReadyVersion { get; set; }
        public UpdateRelease? Result { get; set; } = new("0.4.1", "Release notes");
        public Exception? CheckError, DownloadError, RestartError;
        public Task<UpdateRelease?>? CheckGate;
        public TaskCompletionSource? DownloadGate;
        public IProgress<int>? Progress;
        public int Checks, Restarts;
        public async Task<UpdateRelease?> CheckAsync() { Checks++; if (CheckError is not null) throw CheckError; return CheckGate is null ? Result : await CheckGate; }
        public async Task DownloadAsync(UpdateRelease r, IProgress<int> progress, CancellationToken token)
        {
            Progress = progress;
            if (DownloadError is not null) throw DownloadError;
            if (DownloadGate is not null) await DownloadGate.Task;
            token.ThrowIfCancellationRequested(); ReadyVersion = r.Version;
        }
        public void ScheduleRestart() { if (RestartError is not null) throw RestartError; Restarts++; }
    }
}
