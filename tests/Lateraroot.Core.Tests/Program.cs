using Lateraroot.Core;

var root = Path.Combine(Path.GetTempPath(), "lateraroot-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var tests = new List<(string Name, Action Run)>
{
    ("Widget cells remain square, contained and disjoint at every edge and scale", () =>
    {
        foreach (var area in new[] { new DockRect(0, 0, 2560, 1392), new DockRect(-1920, 0, 1920, 1040), new DockRect(0, -1200, 800, 1200) })
        foreach (double scale in new[] { 1d, 1.5, 2d })
        foreach (var edge in Enum.GetValues<DockEdge>())
        foreach (int size in new[] { 72, 88, 112 })
        {
            var cells = Enumerable.Range(0, 17).Select(i => DockLayout.DesktopCell(area, edge, scale, i, 16, size, .5)).ToList();
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i]; True(c.Width == c.Height && c.X >= area.X && c.Y >= area.Y && c.Right <= area.Right + .001 && c.Bottom <= area.Bottom + .001);
                for (int j = i + 1; j < cells.Count; j++) True(c.Right <= cells[j].X + .001 || cells[j].Right <= c.X + .001 || c.Bottom <= cells[j].Y + .001 || cells[j].Bottom <= c.Y + .001);
            }
        }
    }),
    ("All dock layouts stay within positive, negative-origin and scaled work areas", () =>
    {
        foreach (var area in new[] { new DockRect(0, 0, 2560, 1392), new DockRect(-1920, 120, 1920, 1040), new DockRect(0, -1200, 800, 1200) })
        foreach (var scale in new[] { 1d, 1.5d, 2d })
        foreach (var edge in Enum.GetValues<DockEdge>())
        {
            Inside(DockLayout.DesktopRail(area, edge, scale), area);
            foreach (double offset in new[] { 0d, 0.5d, 1d })
            foreach (bool expanded in new[] { false, true })
            foreach (bool minimized in new[] { false, true })
                Inside(DockLayout.Compact(area, edge, offset, scale, expanded, minimized), area);
            for (int index = 0; index < 16; index++) Inside(DockLayout.Flyout(area, edge, index, 16, scale, 824, 804), area);
        }
        static void Inside(DockRect rect, DockRect area) => True(rect.X >= area.X && rect.Y >= area.Y && rect.Right <= area.Right + 0.001 && rect.Bottom <= area.Bottom + 0.001 && rect.Width > 0 && rect.Height > 0);
    }),
    ("Dragging snaps to all four screen edges with clamped position", () =>
    {
        var area = new DockRect(-1920, 0, 1920, 1080);
        Equal(DockEdge.Left, DockLayout.Snap(area, -1919, 540, 1).Edge);
        Equal(DockEdge.Right, DockLayout.Snap(area, -1, 540, 1).Edge);
        Equal(DockEdge.Top, DockLayout.Snap(area, -960, 1, 1).Edge);
        Equal(DockEdge.Bottom, DockLayout.Snap(area, -960, 1079, 1).Edge);
        Equal(0d, DockLayout.Snap(area, -1919, 3, 1).Offset);
        Equal(1d, DockLayout.Snap(area, -1919, 1077, 1).Offset);
    }),
    ("Existing settings retain custom rules and get safe presentation defaults", () =>
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"Stacks\":[{\"Id\":\"custom\",\"Name\":\"My files\",\"Filter\":\"*\",\"Color\":\"#123456\"}]}")!;
        settings.Validate(); Equal(DockVisibility.Visible, settings.Visibility); Equal(0.5, settings.DockOffset); Equal("#123456", settings.Stacks.Single().Color);
        settings.DockOffset = double.NaN; settings.Validate(); Equal(0.5, settings.DockOffset);
    }),
    ("Rules match extensions case-insensitively; fallback never steals matches", () =>
    {
        var settings = new AppSettings();
        settings.Stacks.Insert(0, settings.Stacks[^1]); settings.Stacks.RemoveAt(settings.Stacks.Count - 1);
        settings.Validate();
        Equal("images", settings.Classify("PHOTO.JPG", false).Id);
        Equal("other", settings.Classify("unknown.xyz", false).Id);
        Equal("folders", settings.Classify("folder.jpg", true).Id);
        settings.Stacks.Insert(0, new() { Id = "custom", Filter = "*.JPG, pdf", Name = "Custom" });
        Equal("custom", settings.Classify("photo.jpg", false).Id);
        Equal("custom", settings.Classify("book.pdf", false).Id);
    }),
    ("Validation rejects missing fallback and duplicate IDs", () =>
    {
        var settings = new AppSettings(); settings.Stacks.RemoveAt(settings.Stacks.Count - 1);
        Throws<InvalidDataException>(settings.Validate);
        settings = new(); settings.Stacks[0].Id = settings.Stacks[1].Id;
        Throws<InvalidDataException>(settings.Validate);
        settings = new(); settings.Stacks[0].Filter = "**.pdf";
        Throws<InvalidDataException>(settings.Validate);
        settings = new(); settings.Stacks[0].Color = "red";
        Throws<InvalidDataException>(settings.Validate);
    }),
    ("Scan, sort, search and public desktop are read-only", () =>
    {
        var (desktop, data) = Fixture("scan");
        var shared = Path.Combine(root, "public"); Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(desktop, "z.txt"), "longer content");
        File.WriteAllText(Path.Combine(desktop, "a.txt"), "a");
        File.WriteAllText(Path.Combine(shared, "shared.txt"), "public");
        var library = new DesktopLibrary(data, desktop, shared);
        var settings = new AppSettings { Order = FileOrder.Name };
        Equal("a.txt", library.Scan(settings)[0].Name);
        settings.Order = FileOrder.Size;
        Equal("z.txt", library.Scan(settings)[0].Name);
        Equal(1, library.Scan(settings, "SHARED").Count);
        var result = library.Organize(library.Scan(settings).Where(x => x.Name == "shared.txt"));
        Equal(0, result.Succeeded);
        True(File.Exists(Path.Combine(shared, "shared.txt")));
        settings.IncludePublicDesktop = false; Equal(2, library.Scan(settings).Count);
        Equal(2, Directory.GetFiles(desktop).Length);
    }),
    ("Organize, restart, restore preserves bytes, folders and collisions", () =>
    {
        var (desktop, data) = Fixture("restore");
        var file = Path.Combine(desktop, "memo.txt"); File.WriteAllText(file, "original bytes 日本語");
        var folder = Path.Combine(desktop, "project"); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "inside.txt"), "nested");
        var library = new DesktopLibrary(data, desktop);
        var result = library.Organize(library.Scan(new())); Equal(2, result.Succeeded); Equal(0, result.Errors.Count);
        True(!File.Exists(file)); True(!Directory.Exists(folder));
        library = new(data, desktop); Equal(2, library.Scan(new()).Count(x => x.IsStored));
        File.WriteAllText(file, "new desktop file"); Directory.CreateDirectory(folder);
        result = library.Restore(library.Scan(new()).Where(x => x.IsStored).Select(x => x.RecordId!));
        Equal(2, result.Succeeded);
        Equal("new desktop file", File.ReadAllText(file));
        Equal("original bytes 日本語", File.ReadAllText(Path.Combine(desktop, "memo (restored 1).txt")));
        Equal("nested", File.ReadAllText(Path.Combine(desktop, "project (restored 1)", "inside.txt")));
        Equal(0, new DesktopLibrary(data, desktop).Scan(new()).Count(x => x.IsStored));
    }),
    ("Locked files report failures without losing originals", () =>
    {
        var (desktop, data) = Fixture("locked"); var path = Path.Combine(desktop, "locked.txt"); File.WriteAllText(path, "safe");
        var library = new DesktopLibrary(data, desktop); var items = library.Scan(new());
        using (var handle = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { var result = library.Organize(items); Equal(0, result.Succeeded); Equal(1, result.Errors.Count); }
        Equal("safe", File.ReadAllText(path));
        Equal(1, library.Organize(library.Scan(new())).Succeeded);
    }),
    ("Pre-move journal is recoverable after restart", () =>
    {
        var (desktop, data) = Fixture("journal");
        var id = Guid.NewGuid().ToString("N"); var stored = Path.Combine(data, "Vault", id, "recover.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(stored)!); File.WriteAllText(stored, "recover");
        JsonStore.Write(Path.Combine(data, "library.json"), new[] { new StorageRecord { Id = id, OriginalPath = Path.Combine(desktop, "recover.txt"), StoredPath = stored } });
        var library = new DesktopLibrary(data, desktop);
        Equal(1, library.Restore([id]).Succeeded); Equal("recover", File.ReadAllText(Path.Combine(desktop, "recover.txt")));
    }),
    ("Malformed or escaping journals fail closed", () =>
    {
        var (desktop, data) = Fixture("invalid");
        Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "library.json"), "{broken");
        Throws<System.Text.Json.JsonException>(() => new DesktopLibrary(data, desktop));
        JsonStore.Write(Path.Combine(data, "library.json"), new[] { new StorageRecord { StoredPath = Path.Combine(root, "outside.txt"), OriginalPath = Path.Combine(desktop, "file.txt") } });
        Throws<InvalidDataException>(() => new DesktopLibrary(data, desktop));
    }),
    ("Unsafe storage locations and forged item names are rejected", () =>
    {
        var (desktop, data) = Fixture("containment");
        Throws<ArgumentException>(() => new DesktopLibrary(Path.Combine(desktop, "storage"), desktop));
        var file = Path.Combine(desktop, "safe.txt"); File.WriteAllText(file, "safe");
        var library = new DesktopLibrary(data, desktop);
        var item = library.Scan(new()).Single() with { Name = "..\\escape.txt" };
        var result = library.Organize([item]); Equal(0, result.Succeeded); Equal(1, result.Errors.Count);
        Equal("safe", File.ReadAllText(file));
    }),
    ("Settings round-trip and rule changes reclassify stored files", () =>
    {
        var (desktop, data) = Fixture("settings"); File.WriteAllText(Path.Combine(desktop, "a.txt"), "a");
        var settings = new AppSettings { Edge = DockEdge.Left, Order = FileOrder.Modified };
        JsonStore.Write(Path.Combine(data, "settings.json"), settings);
        var loaded = JsonStore.Read(Path.Combine(data, "settings.json"), () => new AppSettings()); loaded.Validate();
        Equal(DockEdge.Left, loaded.Edge); Equal(FileOrder.Modified, loaded.Order);
        var library = new DesktopLibrary(data, desktop); library.Organize(library.Scan(settings));
        settings.Stacks.Insert(0, new() { Id = "notes", Name = "Notes", Filter = ".txt" });
        Equal("notes", library.Scan(settings).Single().StackId);
    })
};
int failures = 0;
tests.AddRange(UpdateTests.Cases());
try
{
    foreach (var (name, run) in tests)
    {
        try { run(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.Error.WriteLine("FAIL " + name + "\n" + ex); }
    }
    Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed");
}
finally { Directory.Delete(root, recursive: true); }
return failures == 0 ? 0 : 1;

(string Desktop, string Data) Fixture(string name)
{
    var desktop = Path.Combine(root, name, "desktop"); var data = Path.Combine(root, name, "data");
    Directory.CreateDirectory(desktop); return (desktop, data);
}
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
static void True(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
