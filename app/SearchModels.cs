namespace XtraSearch;

/// <summary>Immutable criteria captured before a search starts.</summary>
public sealed record SearchOptions(
    string Locations, string FileName = "*", string Content = "", string NameMode = "Wildcard",
    string ContentMode = "Plain text", bool NameCase = false, bool ContentCase = false,
    bool Subfolders = true, bool WholeWord = false, bool LineByLine = false,
    bool IncludeHidden = true, bool SkipBinary = true, bool SearchArchives = false,
    long? MinimumBytes = null, long? MaximumBytes = null,
    DateTime? ModifiedAfter = null, DateTime? ModifiedBefore = null,
    DateTime? CreatedAfter = null, DateTime? CreatedBefore = null,
    int Workers = 0);

/// <summary>A file match with bounded display snippets and an uncapped hit count.</summary>
public sealed record SearchResult(string Name, string Location, long Bytes, DateTime Modified,
    long Hits, string Preview, string FullPath)
{
    public string Size => Bytes < 1024 ? $"{Bytes:N0} B" : Bytes < 1048576 ? $"{Bytes / 1024d:N1} KB" : $"{Bytes / 1048576d:N1} MB";
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>Thread-safe counters sampled by the view model.</summary>
public sealed class SearchStatistics
{
    public long Checked;
    public long Found;
    public long Bytes;
    public long Skipped;
    public System.Collections.Concurrent.ConcurrentQueue<string> Errors { get; } = new();
    internal volatile SearchFileProgress?[] WorkerFiles = [];

    public void Report(string path, Exception exception)
    {
        Interlocked.Increment(ref Skipped);
        if (Errors.Count < 200) Errors.Enqueue($"{path}: {exception.Message}");
    }
}

/// <summary>Tracks the latest content scan assigned to a search worker.</summary>
internal sealed class SearchFileProgress(string name, string location)
{
    private readonly long started = System.Diagnostics.Stopwatch.GetTimestamp();
    private long finished;
    private long linesRead;

    public string Name { get; } = name;
    public string Location { get; } = location;
    public long LinesRead
    {
        get => Interlocked.Read(ref linesRead);
        set => Interlocked.Exchange(ref linesRead, value);
    }

    public void Finish() => Volatile.Write(ref finished, System.Diagnostics.Stopwatch.GetTimestamp());

    public SearchWorkerRow Snapshot(int worker)
    {
        var end = Volatile.Read(ref finished);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started,
            end == 0 ? System.Diagnostics.Stopwatch.GetTimestamp() : end);
        return new SearchWorkerRow(worker, elapsed.TotalSeconds, Name, Location, LinesRead,
            end == 0 ? "Scanning" : "Idle");
    }
}

/// <summary>A search worker and the progress of its most recent file scan.</summary>
public sealed record SearchWorkerRow(int Worker, double Seconds, string Name, string Location, long Lines, string State);

/// <summary>A display label paired with the mode identifier stored in search files.</summary>
public sealed record SearchModeChoice(string Value, string Label);
