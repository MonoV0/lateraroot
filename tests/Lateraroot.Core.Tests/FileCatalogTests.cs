using Lateraroot.Core;

internal static class FileCatalogTests
{
    public static IEnumerable<(string, Action)> Cases()
    {
        yield return ("Catalog reads only on request, on a worker, and reuses view-only changes", () => Run(async () =>
        {
            int calls = 0, thread = Environment.CurrentManagedThreadId;
            var settings = new AppSettings();
            var catalog = new FileCatalog(_ => { Interlocked.Increment(ref calls); Need(Environment.CurrentManagedThreadId != thread); return [Item("a.txt")]; });
            Need(calls == 0); var first = await catalog.RefreshAsync(settings); Need(calls == 1 && !catalog.IsDirty);
            settings.Stacks[0].Name = "Personal"; settings.Stacks[0].Color = "#123456"; settings.Order = FileOrder.Size;
            settings.TileSize = 112; settings.LockPosition = true; settings.ReduceTransparency = true;
            Need(ReferenceEquals(first, await catalog.RefreshAsync(settings)) && calls == 1);
            catalog.Invalidate(); Need(calls == 1 && catalog.IsDirty);
            Need(ReferenceEquals(first, await catalog.RefreshAsync(settings)) && calls == 2);
        }));
        yield return ("Catalog serializes/coalesces requests and discards obsolete classification", () => Run(async () =>
        {
            using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            int calls = 0, active = 0, max = 0;
            var catalog = new FileCatalog(settings =>
            {
                int running = Interlocked.Increment(ref active); max = Math.Max(max, running);
                int call = Interlocked.Increment(ref calls);
                try
                {
                    if (call == 1) { entered.Set(); Need(release.Wait(TimeSpan.FromSeconds(5))); }
                    return [Item(call == 1 ? "old.txt" : "new.pdf") with { StackId = settings.Stacks[0].Id }];
                }
                finally { Interlocked.Decrement(ref active); }
            });
            var settings = new AppSettings(); var original = catalog.RefreshAsync(settings);
            try
            {
                Need(entered.Wait(TimeSpan.FromSeconds(5)));
                settings.Stacks.Reverse();
                for (int i = 0; i < 100; i++) { catalog.Invalidate(); Need(ReferenceEquals(original, catalog.RefreshAsync(settings))); }
                Need(catalog.Current.Items.Count == 0 && calls == 1);
            }
            finally { release.Set(); }
            var latest = await original;
            Need(calls == 2 && max == 1 && latest.Items.Single().Name == "new.pdf" && latest.Items.Single().StackId == settings.Stacks[0].Id && !catalog.IsDirty);
        }));
        yield return ("Catalog snapshots are immutable and unchanged reads keep identity", () => Run(async () =>
        {
            var source = new List<DesktopItem> { Item("b.txt"), Item("a.txt") };
            var catalog = new FileCatalog(_ => source); var settings = new AppSettings();
            var first = await catalog.RefreshAsync(settings); source.Reverse();
            Need(ReferenceEquals(first, await catalog.RefreshAsync(settings, force: true)));
            Need(first.Items[0].Name == "a.txt");
            source.Clear(); Need(first.Items.Count == 2);
            try { ((IList<DesktopItem>)first.Items).Clear(); throw new Exception("Mutable snapshot"); } catch (NotSupportedException) { }
            var next = await catalog.RefreshAsync(settings, force: true); Need(next.Items.Count == 0 && next.Revision > first.Revision);
        }));
        yield return ("Failed catalog reads preserve the last good data and allow retry", () => Run(async () =>
        {
            bool fail = false;
            var catalog = new FileCatalog(_ => fail ? throw new IOException("read failed") : [Item("kept.txt")]); var settings = new AppSettings();
            var first = await catalog.RefreshAsync(settings); fail = true;
            try { await catalog.RefreshAsync(settings, force: true); throw new Exception("Expected failure"); } catch (IOException) { }
            Need(ReferenceEquals(first, catalog.Current) && catalog.IsDirty && !catalog.IsReading);
            fail = false; Need(ReferenceEquals(first, await catalog.RefreshAsync(settings)) && !catalog.IsDirty);
        }));
        yield return ("Classification/public-desktop changes reacquire; cached sorting does not", () => Run(async () =>
        {
            int calls = 0;
            var catalog = new FileCatalog(_ => { calls++; return [Item("a.txt") with { Size = 2 }, Item("b.pdf") with { Size = 10 }]; }); var settings = new AppSettings();
            var first = await catalog.RefreshAsync(settings);
            Need(FileCatalog.Order(first.Items, FileOrder.Size)[0].Name == "b.pdf" && first.Items[0].Name == "a.txt" && calls == 1);
            settings.Stacks[0].Filter = ".blend"; await catalog.RefreshAsync(settings); Need(calls == 2);
            settings.IncludePublicDesktop = !settings.IncludePublicDesktop; await catalog.RefreshAsync(settings); Need(calls == 3);
            Need(JsonStore.Clone(new AppSettings { LockPosition = true }).LockPosition);
            Need(!System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!.LockPosition);
        }));
    }
    private static DesktopItem Item(string name) => new(Path.Combine(Path.GetTempPath(), name), name, false, 1, new DateTime(2026, 1, 1), "documents", null, true);
    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();
    private static void Need(bool condition) { if (!condition) throw new Exception("Catalog assertion failed"); }
}
