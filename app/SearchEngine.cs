using System.IO.Compression;
using System.Text;
using System.Threading.Channels;
using System.Xml;
using UglyToad.PdfPig;

namespace XtraSearch;

/// <summary>Streams directory entries through bounded parallel workers without blocking WPF.</summary>
public sealed class SearchEngine
{
    public const int NotificationMilliseconds = 100;
    public const int PreviewLines = 200;

    public async Task RunAsync(SearchOptions options, Action<SearchResult> onResult,
        SearchStatistics statistics, ManualResetEventSlim pause, CancellationToken cancellationToken)
    {
        var name = new ExpressionMatcher(string.IsNullOrWhiteSpace(options.FileName) ? "*" : options.FileName,
            options.NameMode, options.NameCase);
        var content = string.IsNullOrEmpty(options.Content) ? null : new ExpressionMatcher(options.Content,
            options.ContentMode, options.ContentCase, options.WholeWord, options.LineByLine);
        var paths = options.Locations.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Environment.ExpandEnvironmentVariables).Select(path => path.Trim('"')).ToArray();
        var roots = paths.Where(path => !path.StartsWith('!')).ToArray();
        var excluded = paths.Where(path => path.StartsWith('!')).Select(path => Path.GetFullPath(path[1..]).TrimEnd('\\')).ToArray();
        if (roots.Length == 0) throw new ArgumentException("Choose at least one file or folder to search.");
        foreach (var root in roots)
            if (!Directory.Exists(root) && !File.Exists(root)) throw new DirectoryNotFoundException($"Location not found: {root}");

        var queue = Channel.CreateBounded<FileInfo>(new BoundedChannelOptions(512) { SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool IsExcluded(string path) => excluded.Any(exclude => path.Equals(exclude, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(exclude + "\\", StringComparison.OrdinalIgnoreCase));

        async Task Produce()
        {
            try
            {
                foreach (var root in roots)
                {
                    var pending = new Stack<string>();
                    if (File.Exists(root))
                    {
                        var file = new FileInfo(root);
                        if (!IsExcluded(file.FullName) && visited.Add(file.FullName)) await queue.Writer.WriteAsync(file, cancellationToken);
                        continue;
                    }
                    pending.Push(Path.GetFullPath(root));
                    while (pending.TryPop(out var folder))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        pause.Wait(cancellationToken);
                        if (IsExcluded(folder) || !visited.Add(folder)) continue;
                        try
                        {
                            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", new EnumerationOptions
                            {
                                IgnoreInaccessible = false, RecurseSubdirectories = false, AttributesToSkip = 0
                            }))
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                if (IsExcluded(entry.FullName)) continue;
                                if (!options.IncludeHidden && (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                                if (entry is DirectoryInfo)
                                {
                                    if (options.Subfolders && (entry.Attributes & FileAttributes.ReparsePoint) == 0) pending.Push(entry.FullName);
                                }
                                else if (visited.Add(entry.FullName)) await queue.Writer.WriteAsync((FileInfo)entry, cancellationToken);
                            }
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            statistics.Report(folder, exception);
                        }
                    }
                }
                queue.Writer.TryComplete();
            }
            catch (Exception exception) { queue.Writer.TryComplete(exception); throw; }
        }

        void Publish(string displayName, string location, long bytes, DateTime modified, string path, IEnumerable<string>? lines)
        {
            long hits = 0;
            var preview = new StringBuilder();
            var state = content?.CreateState();
            if (content != null && lines != null)
            {
                long lineNumber = 0;
                foreach (var line in lines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    pause.Wait(cancellationToken);
                    lineNumber++;
                    if (!content.Feed(line, state!)) continue;
                    hits++;
                    if (hits <= PreviewLines) preview.AppendLine($"{lineNumber,6}  {line[..Math.Min(2000, line.Length)]}");
                }
                if (!content.Accept(state!, hits)) return;
            }
            if (hits > PreviewLines) preview.AppendLine($"… Showing the first {PreviewLines} matching lines.");
            Interlocked.Increment(ref statistics.Found);
            onResult(new SearchResult(displayName, location, bytes, modified, hits, preview.ToString(), path));
        }

        async Task Consume()
        {
            await foreach (var file in queue.Reader.ReadAllAsync(cancellationToken))
            {
                pause.Wait(cancellationToken);
                Interlocked.Increment(ref statistics.Checked);
                try
                {
                    Interlocked.Add(ref statistics.Bytes, file.Length);
                    bool MetadataMatches(long length, DateTime modified, DateTime created) =>
                        (!options.MinimumBytes.HasValue || length > options.MinimumBytes.Value) &&
                        (!options.MaximumBytes.HasValue || length < options.MaximumBytes.Value) &&
                        (!options.ModifiedAfter.HasValue || modified >= options.ModifiedAfter.Value.Date) &&
                        (!options.ModifiedBefore.HasValue || modified < options.ModifiedBefore.Value.Date.AddDays(1)) &&
                        (!options.CreatedAfter.HasValue || created >= options.CreatedAfter.Value.Date) &&
                        (!options.CreatedBefore.HasValue || created < options.CreatedBefore.Value.Date.AddDays(1));

                    if (name.IsMatch(file.Name) && MetadataMatches(file.Length, file.LastWriteTime, file.CreationTime))
                    {
                        Publish(file.Name, file.DirectoryName ?? "", file.Length, file.LastWriteTime, file.FullName,
                            content == null ? null : DocumentText.Read(file.FullName, options.SkipBinary, cancellationToken));
                    }

                    if (options.SearchArchives && file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        using var archive = ZipFile.OpenRead(file.FullName);
                        foreach (var entry in archive.Entries)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (entry.Name.Length == 0 || !name.IsMatch(entry.Name) ||
                                !MetadataMatches(entry.Length, entry.LastWriteTime.LocalDateTime, file.CreationTime)) continue;
                            using var stream = entry.Open();
                            Publish(entry.Name, file.FullName + " ▸ " + (Path.GetDirectoryName(entry.FullName) ?? ""), entry.Length,
                                entry.LastWriteTime.LocalDateTime, file.FullName + "|" + entry.FullName,
                                content == null ? null : DocumentText.ReadStream(stream, options.SkipBinary, cancellationToken));
                        }
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    statistics.Report(file.FullName, exception);
                }
            }
        }

        var workers = Math.Clamp(options.Workers > 0 ? options.Workers : Environment.ProcessorCount, 1, 1024);
        var producer = Task.Run(Produce, cancellationToken);
        var consumers = Enumerable.Range(0, workers).Select(_ => Task.Run(Consume, cancellationToken));
        await Task.WhenAll(consumers.Prepend(producer));
    }
}

/// <summary>Reads text sequentially, with format-aware PDF and Open XML extraction.</summary>
public static class DocumentText
{
    public static IEnumerable<string> Read(string path, bool skipBinary, CancellationToken token)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".pdf")
        {
            using var document = PdfDocument.Open(path);
            foreach (var page in document.GetPages())
            {
                token.ThrowIfCancellationRequested();
                yield return page.Text;
            }
            yield break;
        }
        if (extension is ".docx" or ".xlsx" or ".pptx" or ".odt" or ".ods" or ".odp")
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries.Where(entry =>
                entry.FullName == "word/document.xml" || entry.FullName.StartsWith("word/header") ||
                entry.FullName.StartsWith("word/footer") || entry.FullName == "xl/sharedStrings.xml" ||
                entry.FullName.StartsWith("xl/worksheets/sheet") || entry.FullName.StartsWith("ppt/slides/slide") || entry.FullName == "content.xml"))
            {
                token.ThrowIfCancellationRequested();
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                var paragraph = new StringBuilder();
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.Text) paragraph.Append(reader.Value);
                    if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName is "p" or "row" or "si")
                    {
                        yield return paragraph.ToString();
                        paragraph.Clear();
                    }
                }
                if (paragraph.Length > 0) yield return paragraph.ToString();
            }
            yield break;
        }
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.SequentialScan);
        foreach (var line in ReadStream(file, skipBinary, token)) yield return line;
    }

    public static IEnumerable<string> ReadStream(Stream stream, bool skipBinary, CancellationToken token)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 65536, true);
        var first = true;
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            if (first && skipBinary && line.Contains('\0')) throw new InvalidDataException("Binary content skipped.");
            first = false;
            yield return line;
        }
    }
}
