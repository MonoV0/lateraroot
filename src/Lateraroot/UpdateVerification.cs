using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Lateraroot.Core;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace Lateraroot;

/// <summary>Opt-in, demo-only end-to-end verification of the real packaged update engine.</summary>
internal static class UpdateVerification
{
    internal static async Task RunAsync(string[] args)
    {
        var app = App.Current;
        string resultPath = Path.Combine(app.DataDirectory, "update-result.json");
        string statePath = Path.Combine(app.DataDirectory, "update-before.json");
        string? Arg(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(app.DataDirectory, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p) is "settings.json" or "library.json" || p.StartsWith(app.Library.VaultPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => Path.GetRelativePath(app.DataDirectory, p), Hash);
        try
        {
            if (Arg("--verify-update-after") is { } expected)
            {
                var manager = new UpdateManager(app.DataDirectory);
                Need(manager.IsInstalled && manager.IsPortable, "Restarted from a managed portable package");
                Need(AppInfo.Version == expected && manager.CurrentVersion?.ToString() == expected, "New binary and package version match");
                var before = JsonStore.Read<Dictionary<string, string>>(statePath, () => []);
                var after = Snapshot();
                Need(before.Count >= 3 && before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var hash) && hash == kv.Value), "Settings, library journal and stored file bytes survived update");
                Need(app.Library.Scan(app.Settings).Any(i => i.IsStored), "Stored files remain accessible in the new app");
                JsonStore.Write(resultPath, new { Passed = true, Version = AppInfo.Version, Checks = new[] { "Real portable package upgraded and restarted", "New executable version confirmed", "Settings, journal and Vault hashes preserved", "Stored files accessible after restart" } });
                app.Shutdown(); return;
            }
            bool github = args.Contains("--verify-update-github");
            var feed = Arg("--verify-update") ?? Arg("--verify-update-rejection");
            if (!github && feed is null) throw new ArgumentException("Missing test feed");
            app.Settings.CheckForUpdates = false; app.Settings.TileSize = 104; app.Settings.Stacks[0].Name = "更新前の分類";
            JsonStore.Write(app.SettingsPath, app.Settings);
            var sample = app.Library.Scan(app.Settings).First(i => i.Name == "はじめに.txt");
            Need(app.Library.Organize([sample]).Succeeded == 1, "Sample was stored before update");
            JsonStore.Write(statePath, Snapshot());
            var updateManager = github ? new UpdateManager(new GithubSource(AppInfo.Repository, accessToken: null, prerelease: false)) : new UpdateManager(feed!);
            var updater = new ApplicationUpdater(updateManager, allowSandbox: true,
                restartArguments: ["--demo", "--data-dir", app.DataDirectory, "--verify-update-after", Arg("--expect-version") ?? "0.4.1"]);
            Need(updater.IsSupported && updateManager.IsPortable, "Original package is managed and portable");
            var release = await updater.CheckAsync() ?? throw new InvalidOperationException("No test update available");
            if (Arg("--verify-update-rejection") is not null)
            {
                try { await updater.DownloadAsync(release, new Progress<int>(), CancellationToken.None); throw new InvalidOperationException("Corrupt package was accepted"); }
                catch (UpdateFailureException ex) when (ex.InnerException is ChecksumFailedException)
                {
                    Need(updater.ReadyVersion is null, "Rejected download cannot be applied");
                    JsonStore.Write(resultPath, new { Passed = true, Version = AppInfo.Version, Checks = new[] { "Corrupt package rejected by checksum", "No update scheduled" } });
                    app.Shutdown(); return;
                }
            }
            await updater.DownloadAsync(release, new Progress<int>(), CancellationToken.None);
            Need(updater.ReadyVersion == release.Version, "Package verified and ready");
            updater.ScheduleRestart(); app.Shutdown();
        }
        catch (Exception ex) { JsonStore.Write(resultPath, new { Passed = false, Error = ex.ToString() }); app.Shutdown(1); }
    }
}
