namespace Lateraroot.Core;

/// <summary>Only explicit Organize/Restore calls move files. A journal is flushed before each move.</summary>
public sealed class DesktopLibrary
{
    private readonly object gate = new();
    private readonly string manifestPath;
    private readonly string vault;
    private readonly string desktop;
    private readonly string? publicDesktop;
    private readonly List<StorageRecord> records;

    public DesktopLibrary(string dataDirectory, string desktopDirectory, string? publicDesktopDirectory = null)
    {
        desktop = Path.GetFullPath(desktopDirectory);
        publicDesktop = publicDesktopDirectory;
        vault = Path.Combine(Path.GetFullPath(dataDirectory), "Vault");
        if (vault.StartsWith(desktop.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("収納先は対象デスクトップの外に設定してください。");
        manifestPath = Path.Combine(dataDirectory, "library.json");
        Directory.CreateDirectory(vault);
        records = JsonStore.Read(manifestPath, () => new List<StorageRecord>());
        foreach (var record in records)
        {
            if (!Guid.TryParseExact(record.Id, "N", out _) || !Path.IsPathFullyQualified(record.OriginalPath) ||
                !Path.IsPathFullyQualified(record.StoredPath) || !record.StoredPath.StartsWith(vault + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(record.StoredPath), Path.Combine(vault, record.Id), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("収納履歴が不正です。library.json を確認してください。");
        }
    }

    public string VaultPath => vault;

    public IReadOnlyList<DesktopItem> Scan(AppSettings settings, string? query = null)
    {
        lock (gate)
        {
            var items = new List<DesktopItem>();
            AddDesktop(desktop, true);
            if (settings.IncludePublicDesktop && publicDesktop is not null && !string.Equals(desktop, publicDesktop, StringComparison.OrdinalIgnoreCase)) AddDesktop(publicDesktop, false);
            foreach (var record in records) AddItem(record.StoredPath, record.Id, false, record.AssignedStackId);
            IEnumerable<DesktopItem> filtered = items;
            if (!string.IsNullOrWhiteSpace(query)) filtered = filtered.Where(x => x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
            var ordered = settings.Order switch
            {
                FileOrder.Modified => filtered.OrderByDescending(x => x.Modified),
                FileOrder.Type => filtered.OrderBy(x => x.Extension, StringComparer.CurrentCultureIgnoreCase),
                FileOrder.Size => filtered.OrderByDescending(x => x.Size),
                _ => filtered.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            };
            return ordered.ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();

            void AddDesktop(string directory, bool canMove)
            {
                if (!Directory.Exists(directory)) return;
                try { foreach (var path in Directory.EnumerateFileSystemEntries(directory)) AddItem(path, null, canMove, null); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            void AddItem(string path, string? recordId, bool canMove, string? assigned)
            {
                try
                {
                    var attr = File.GetAttributes(path);
                    if ((attr & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0) return;
                    var directory = attr.HasFlag(FileAttributes.Directory);
                    var info = new FileInfo(path);
                    items.Add(new(path, Path.GetFileName(path), directory, directory ? 0 : info.Length, File.GetLastWriteTime(path), settings.Classify(path, directory, assigned).Id, recordId, canMove));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public OperationResult Organize(IEnumerable<DesktopItem> items)
    {
        lock (gate)
        {
            var errors = new List<string>();
            int moved = 0;
            foreach (var item in items.Where(x => x.CanOrganize && !x.IsStored).DistinctBy(x => x.Path))
            {
                StorageRecord? record = null;
                try
                {
                    if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(item.Path)), desktop, StringComparison.OrdinalIgnoreCase) || item.Name != Path.GetFileName(item.Path)) throw new IOException("デスクトップ直下の項目のみ収納できます。");
                    var attrs = File.GetAttributes(item.Path);
                    if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden)) != 0) throw new IOException("システム項目・リンク先は収納しません。");
                    record = new StorageRecord { OriginalPath = Path.GetFullPath(item.Path) };
                    record.StoredPath = Path.Combine(vault, record.Id, item.Name);
                    Directory.CreateDirectory(Path.GetDirectoryName(record.StoredPath)!);
                    records.Add(record);
                    JsonStore.Write(manifestPath, records);
                    Move(item.Path, record.StoredPath);
                    moved++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{item.Name}: {ex.Message}");
                    // Keep the durable journal on failure: it also covers a crash during a move.
                    if (record is not null && !Exists(record.StoredPath)) records.Remove(record);
                }
            }
            return new(moved, errors);
        }
    }

    public OperationResult Restore(IEnumerable<string> recordIds)
    {
        lock (gate)
        {
            var errors = new List<string>();
            int restored = 0;
            foreach (var id in recordIds.Distinct())
            {
                var record = records.FirstOrDefault(x => x.Id == id);
                if (record is null || !Exists(record.StoredPath)) continue;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(record.OriginalPath)!);
                    var target = AvailablePath(record.OriginalPath, Directory.Exists(record.StoredPath));
                    // Flush recovery destination first. A crash after Move is harmless: the original is already restored.
                    record.OriginalPath = target;
                    JsonStore.Write(manifestPath, records);
                    Move(record.StoredPath, target);
                    records.Remove(record);
                    JsonStore.Write(manifestPath, records);
                    restored++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{Path.GetFileName(record.StoredPath)}: {ex.Message}"); }
            }
            return new(restored, errors);
        }
    }

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
    private static void Move(string source, string target)
    {
        if (Directory.Exists(source)) Directory.Move(source, target);
        else File.Move(source, target); // Never overwrite; locked/cross-volume directories stay in place.
    }

    private static string AvailablePath(string path, bool directory)
    {
        if (!Exists(path)) return path;
        var extension = directory ? "" : Path.GetExtension(path);
        var stem = directory ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        for (int index = 1; ; index++)
        {
            var candidate = Path.Combine(Path.GetDirectoryName(path)!, $"{stem} (restored {index}){extension}");
            if (!Exists(candidate)) return candidate;
        }
    }
}
