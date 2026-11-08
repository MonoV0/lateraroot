using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace Lateraroot.Core;

/// <summary>Reads local shortcut metadata only. Never resolves, launches or enumerates game libraries.</summary>
public static class GameClassifier
{
    private sealed record Entry(long Length, long Written, bool Game);
    private static readonly Dictionary<string, Entry> cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object gate = new();

    public static bool IsGame(string path)
    {
        string extension = Path.GetExtension(path);
        if (!extension.Equals(".url", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 1_048_576 || path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
            long written = file.LastWriteTimeUtc.Ticks, length = file.Length;
            lock (gate) if (cache.TryGetValue(path, out var entry) && entry.Written == written && entry.Length == length) return entry.Game;
            bool game = extension.Equals(".url", StringComparison.OrdinalIgnoreCase)
                ? ReadInternetShortcut(path)
                : OperatingSystem.IsWindows() && ReadShellShortcut(path);
            lock (gate)
            {
                if (cache.Count >= 512) cache.Clear();
                cache[path] = new(length, written, game);
            }
            return game;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException or RegexMatchTimeoutException or System.Security.SecurityException) { return false; }
    }

    private static bool ReadInternetShortcut(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var buffer = new char[32768]; int count = reader.ReadBlock(buffer, 0, buffer.Length);
        bool section = false;
        foreach (string raw in new string(buffer, 0, count).Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('[')) section = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
            else if (section && line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return IsGameTarget(line[4..].Trim());
        }
        return false;
    }

    // Deterministic recognition is shared by .url files, shell links and tests.
    // Executable filenames alone (e.g. game.exe) are never enough evidence.
    public static bool IsGameTarget(string target, string arguments = "")
    {
        target = target.Trim().Trim('"'); arguments = arguments.Trim();
        bool Match(string text, string pattern) => Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        if (Match(target, @"^steam://(?:rungameid|run|launch)/\d+(?:[/?#]|$)")) return true;
        if (Uri.TryCreate(target, UriKind.Absolute, out var epic) && epic.Scheme.Equals("com.epicgames.launcher", StringComparison.OrdinalIgnoreCase)
            && epic.Host.Equals("apps", StringComparison.OrdinalIgnoreCase) && epic.AbsolutePath.Length > 1
            && epic.Query.TrimStart('?').Split('&').Any(part => part.Equals("action=launch", StringComparison.OrdinalIgnoreCase))) return true;
        if (Match(target, @"^uplay://launch/\d+(?:[/?#]|$)")) return true;
        if (Match(target, @"^(?:origin|link2ea)://launchgame/[^\s]+")) return true;
        if (target.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        string normalized = target.Replace('/', '\\');
        string name = normalized[(normalized.LastIndexOf('\\') + 1)..];
        if (name.Equals("steam.exe", StringComparison.OrdinalIgnoreCase)) return Match(arguments, @"(?:^|\s)-applaunch\s+\d+(?:\s|$)");
        if (name.Equals("GalaxyClient.exe", StringComparison.OrdinalIgnoreCase)) return Match(arguments, @"(?:^|\s)/command=runGame(?:\s|$)") && Match(arguments, @"(?:^|\s)/gameId=\d+(?:\s|$)");
        if (name.Equals("RiotClientServices.exe", StringComparison.OrdinalIgnoreCase)) return Match(arguments, @"(?:^|\s)--launch-product=[\w-]+(?:\s|$)");
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        return Match(normalized, @"\\(?:steamapps\\common|Epic Games|EpicGames|GOG Games)\\.+\.exe$");
    }

    [SupportedOSPlatform("windows")]
    private static bool ReadShellShortcut(string path)
    {
        object link = new ShellLink();
        try
        {
            ((IPersistFile)link).Load(path, 0);
            var shell = (IShellLinkW)link;
            var target = new StringBuilder(32768); var arguments = new StringBuilder(32768);
            shell.GetPath(target, target.Capacity, IntPtr.Zero, 4); // SLGP_RAWPATH, deliberately no Resolve().
            shell.GetArguments(arguments, arguments.Capacity);
            return IsGameTarget(target.ToString(), arguments.ToString());
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr data, uint flags);
        void GetIDList(out IntPtr list);
        void SetIDList(IntPtr list);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
    }
}
