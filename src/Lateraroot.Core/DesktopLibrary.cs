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
            if (record is null || !Guid.TryParseExact(record.Id, "N", out _) || !Path.IsPathFullyQualified(record.OriginalPath) ||
                !Path.IsPathFullyQualified(record.StoredPath) || !IsVaultEntry(record))
                throw new InvalidDataException("収納履歴が不正です。library.json を確認してください。");
        }
    }

    public string VaultPath => vault;
    public bool HasRecoveryEntries { get { lock (gate) return records.Count > 0; } }
    public OperationResult RestoreAll()
    {
        lock (gate) return Restore(records.Select(record => record.Id).ToArray());
    }

    public IReadOnlyList<DesktopItem> Scan(AppSettings settings, string? query = null)
    {
        IEnumerable<DesktopItem> items = ReadFiles(settings);
        if (!string.IsNullOrWhiteSpace(query)) items = items.Where(x => x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        return FileCatalog.Order(items, settings.Order);
    }

    public IReadOnlyList<DesktopItem> ReadFiles(AppSettings settings)
    {
        lock (gate)
        {
            var items = new List<DesktopItem>();
            AddDesktop(desktop, true);
            if (settings.IncludePublicDesktop && publicDesktop is not null && !string.Equals(desktop, publicDesktop, StringComparison.OrdinalIgnoreCase)) AddDesktop(publicDesktop, false);
            foreach (var record in records) AddItem(record.StoredPath, record.Id, false, record.AssignedStackId);
            return items;

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
            string? batch = null;
            foreach (var item in items.Where(x => x.CanOrganize && !x.IsStored).DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase))
            {
                StorageRecord? record = null;
                try
                {
                    if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(item.Path)), desktop, StringComparison.OrdinalIgnoreCase) || item.Name != Path.GetFileName(item.Path)) throw new IOException("デスクトップ直下の項目のみ収納できます。");
                    var attrs = File.GetAttributes(item.Path);
                    if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.System | FileAttributes.Hidden)) != 0) throw new IOException("システム項目・リンク先は収納しません。");
                    batch ??= CreateBatchDirectory();
                    record = new StorageRecord { OriginalPath = Path.GetFullPath(item.Path) };
                    record.StoredPath = Path.Combine(batch, item.Name);
                    records.Add(record);
                    JsonStore.Write(manifestPath, records);
                    Move(item.Path, record.StoredPath);
                    moved++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"{item.Name}: {ex.Message}");
                    // Keep the durable journal on failure: it also covers a crash during a move.
                    if (record is not null && !Exists(record.StoredPath) && Exists(record.OriginalPath))
                    {
                        records.Remove(record);
                        try { JsonStore.Write(manifestPath, records); }
                        catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException) { errors.Add("履歴更新に失敗しました: " + journalError.Message); }
                        RemoveEmptyContainer(record);
                        if (batch is not null && !Directory.Exists(batch)) batch = null;
                    }
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
                if (record is null) continue;
                bool moved = false;
                try
                {
                    if (!Exists(record.StoredPath))
                    {
                        if (!Exists(record.OriginalPath)) { errors.Add($"{Path.GetFileName(record.OriginalPath)}: 収納先と元の場所のどちらにも見つかりません。履歴を保持しました。"); continue; }
                        // A manual recovery or a crash after Move may leave a stale entry.
                        records.Remove(record); JsonStore.Write(manifestPath, records); RemoveEmptyContainer(record); continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(record.OriginalPath)!);
                    var target = AvailablePath(record.OriginalPath, Directory.Exists(record.StoredPath));
                    // Flush recovery destination first. A crash after Move is harmless: the original is already restored.
                    record.OriginalPath = target;
                    JsonStore.Write(manifestPath, records);
                    Move(record.StoredPath, target);
                    moved = true; restored++;
                    records.Remove(record);
                    JsonStore.Write(manifestPath, records);
                    RemoveEmptyContainer(record);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { errors.Add($"{Path.GetFileName(record.StoredPath)}: {(moved ? "ファイルは復元済みですが履歴更新に失敗しました。" : "")}{ex.Message}"); }
            }
            return new(restored, errors);
        }
    }

    private bool IsVaultEntry(StorageRecord record)
    {
        string? parent = Path.GetDirectoryName(Path.GetFullPath(record.StoredPath));
        if (parent is null || !string.Equals(Path.GetDirectoryName(parent), vault, StringComparison.OrdinalIgnoreCase)) return false;
        string folder = Path.GetFileName(parent);
        return string.Equals(folder, record.Id, StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(folder, @"^\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}(?:-\d+)?$");
    }
    private string CreateBatchDirectory()
    {
        string name = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture);
        string candidate = Path.Combine(vault, name);
        for (int i = 2; Exists(candidate); i++) candidate = Path.Combine(vault, name + "-" + i);
        Directory.CreateDirectory(candidate); return candidate;
    }
    private void RemoveEmptyContainer(StorageRecord record)
    {
        if (!IsVaultEntry(record)) return;
        string parent = Path.GetDirectoryName(Path.GetFullPath(record.StoredPath))!;
        try { if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent, recursive: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Only optional empty-container cleanup failed. */ }
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
