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

    public void Report(string path, Exception exception)
    {
        Interlocked.Increment(ref Skipped);
        if (Errors.Count < 200) Errors.Enqueue($"{path}: {exception.Message}");
    }
}
