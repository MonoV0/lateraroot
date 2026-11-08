namespace Lateraroot.Core;

public sealed record FileSnapshot(long Revision, IReadOnlyList<DesktopItem> Items);

/// <summary>One event-driven file acquisition shared by every view. No timer or file actions.</summary>
public sealed class FileCatalog(Func<AppSettings, IReadOnlyList<DesktopItem>> read)
{
    private readonly object gate = new();
    private FileSnapshot current = new(0, Array.AsReadOnly(Array.Empty<DesktopItem>()));
    private Task<FileSnapshot>? pending;
    private AppSettings? requestedSettings;
    private (string Id, string Filter)[] rules = [];
    private bool includePublic;
    private long requested = 1, completed;
    private int reads;
    public int ReadCount => Volatile.Read(ref reads);
    public FileSnapshot Current { get { lock (gate) return current; } }
    public bool IsDirty { get { lock (gate) return requested != completed; } }
    public bool IsReading { get { lock (gate) return pending is not null; } }
    public void Invalidate() { lock (gate) requested++; }

    public Task<FileSnapshot> RefreshAsync(AppSettings settings, bool force = false)
    {
        lock (gate)
        {
            var nextRules = settings.Stacks.Select(s => (s.Id, s.Filter)).ToArray();
            if (requestedSettings is null || includePublic != settings.IncludePublicDesktop || !rules.SequenceEqual(nextRules))
            {
                requestedSettings = JsonStore.Clone(settings);
                requestedSettings.Order = FileOrder.Name;
                includePublic = settings.IncludePublicDesktop; rules = nextRules; requested++;
            }
            if (force) requested++;
            if (pending is not null) return pending;
            if (requested == completed) return Task.FromResult(current);
            // Assign under the lock so even a very fast reader cannot leave a completed task registered.
            pending = Task.Run(ReadLatest);
            return pending;
        }
    }

    private FileSnapshot ReadLatest()
    {
        try
        {
            while (true)
            {
                long ticket; AppSettings settings;
                lock (gate) { ticket = requested; settings = requestedSettings!; }
                Interlocked.Increment(ref reads);
                var items = read(settings).OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToArray();
                lock (gate)
                {
                    if (ticket != requested) continue; // Never publish an obsolete classification/read.
                    if (!current.Items.SequenceEqual(items)) current = new(current.Revision + 1, Array.AsReadOnly(items));
                    completed = ticket; pending = null; return current;
                }
            }
        }
        catch { lock (gate) pending = null; throw; } // Keep the last good snapshot and allow retry.
    }

    public static IReadOnlyList<DesktopItem> Order(IEnumerable<DesktopItem> items, FileOrder order)
    {
        var ordered = order switch
        {
            FileOrder.Modified => items.OrderByDescending(x => x.Modified),
            FileOrder.Type => items.OrderBy(x => x.Extension, StringComparer.CurrentCultureIgnoreCase),
            FileOrder.Size => items.OrderByDescending(x => x.Size),
            _ => items.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return ordered.ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
