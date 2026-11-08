using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using Lateraroot.Core;

namespace Lateraroot;

public partial class App : Application
{
    private Mutex? instance;
    private UpdateSession? updates;
    public UpdateSession Updates => updates ??= new(new ApplicationUpdater());
    public string DataDirectory { get; private set; } = "";
    public string DesktopDirectory { get; private set; } = "";
    public bool IsSandbox { get; private set; }
    public bool IsVerifying { get; private set; }
    public AppSettings Settings { get; set; } = new();
    public DesktopLibrary Library { get; private set; } = null!;
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string? Arg(string name) { var index = Array.IndexOf(e.Args, name); return index >= 0 ? index + 1 < e.Args.Length ? e.Args[index + 1] : throw new ArgumentException($"{name} requires a path") : null; }
            IsSandbox = e.Args.Contains("--demo") || e.Args.Contains("--desktop-dir");
            IsVerifying = e.Args.Contains("--verify-ui");
            if (e.Args.Contains("--verify-ui") && !e.Args.Contains("--demo")) throw new ArgumentException("--verify-ui requires --demo");
            bool updateVerification = e.Args.Any(a => a.StartsWith("--verify-update", StringComparison.Ordinal));
            if (updateVerification && !e.Args.Contains("--demo")) throw new ArgumentException("Update verification requires --demo");
            DataDirectory = Path.GetFullPath(Arg("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lateraroot", IsSandbox ? "Demo" : "Data"));
            DesktopDirectory = Path.GetFullPath(Arg("--desktop-dir") ?? (IsSandbox ? Path.Combine(DataDirectory, "DemoDesktop") : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)));
            if (DataDirectory.Equals(DesktopDirectory, StringComparison.OrdinalIgnoreCase) || DataDirectory.StartsWith(DesktopDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("保存先はデスクトップの外に設定してください。");
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())))[..16];
            instance = new Mutex(true, "Local\\Lateraroot-" + identity, out bool first);
            if (!first) { MessageBox.Show("Lateraroot は起動済みです。通知領域のアイコンから開いてください。", "Lateraroot"); Shutdown(); return; }
            Directory.CreateDirectory(DataDirectory);
            string? warning = null;
            try { Settings = JsonStore.Read(SettingsPath, () => new AppSettings()); Settings.Validate(); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or ArgumentException)
            {
                var backup = SettingsPath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                if (File.Exists(SettingsPath)) File.Copy(SettingsPath, backup, false);
                Settings = new(); warning = "設定ファイルを読み込めなかったため、元の設定を .invalid ファイルに保存して初期設定で起動しました。";
            }
            if (IsSandbox && e.Args.Contains("--demo")) CreateDemo();
            Library = new DesktopLibrary(DataDirectory, DesktopDirectory, IsSandbox ? null : Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            Trace("Library initialized");
            SystemParameters.StaticPropertyChanged += AppearanceChanged;
            if (updateVerification) { Dispatcher.BeginInvoke(async () => await UpdateVerification.RunAsync(e.Args)); return; }
            var window = new MainWindow();
            Trace("Window constructed");
            MainWindow = window;
            window.Show();
            Trace($"Window shown: visible={window.IsVisible}, {window.Left},{window.Top}, {window.ActualWidth}x{window.ActualHeight}");
            if (e.Args.Contains("--verify-ui")) Dispatcher.BeginInvoke(async () => await window.VerifyUiAsync());
            if (warning is not null) MessageBox.Show(window, warning, "設定の復旧");
        }
        catch (Exception ex)
        {
            MessageBox.Show("起動できませんでした。データは削除していません。\n\n" + ex.Message, "Lateraroot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal void Trace(string message)
    {
        if (IsSandbox) File.AppendAllText(Path.Combine(DataDirectory, "demo-diagnostics.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + message + "\n");
    }

    private void CreateDemo()
    {
        Directory.CreateDirectory(DesktopDirectory);
        Write("はじめに.txt", "Lateraroot のデモファイルです。\n右端のスタックにカーソルを合わせてください。");
        Write("アイデアノート.md", "# Ideas\nA quieter desktop.");
        Write("プロジェクト計画.csv", "task,status\nDesign,done\nBuild,in progress");
        Write("Moonlight.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"300\" height=\"200\"><rect width=\"300\" height=\"200\" fill=\"#171b26\"/><circle cx=\"180\" cy=\"70\" r=\"35\" fill=\"#bcb0ff\"/></svg>");
        Directory.CreateDirectory(Path.Combine(DesktopDirectory, "新しいプロジェクト"));
        void Write(string name, string text) { var path = Path.Combine(DesktopDirectory, name); if (!File.Exists(path)) File.WriteAllText(path, text); }
    }

    private void AppearanceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation))) return;
        Dispatcher.BeginInvoke(() =>
        {
            foreach (MainWindow window in Windows.OfType<MainWindow>()) window.RefreshMotionPolicy();
            foreach (Window window in Windows)
                if (window is SettingsWindow settings) settings.ApplyTransparency();
                else GlassSurface.SetReduced(window, Settings.ReduceTransparency || SystemParameters.HighContrast);
        });
    }
    protected override void OnExit(ExitEventArgs e) { SystemParameters.StaticPropertyChanged -= AppearanceChanged; instance?.Dispose(); base.OnExit(e); }
}
