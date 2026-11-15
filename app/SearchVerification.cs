using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace XtraSearch;

/// <summary>Exercises actual filesystem search behavior with deterministic local fixtures.</summary>
public static class SearchVerification
{
    public static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine("verification-fixtures", DateTime.UtcNow.Ticks.ToString()));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        File.WriteAllText(Path.Combine(root, "alpha.txt"), "alpha\nbeta\nCafé 東京\nInvoice 12345", new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(root, "negative.txt"), "alpha\nforbidden", Encoding.Unicode);
        File.WriteAllText(Path.Combine(root, "notes.log"), "ALPHA beta\nplain text", Encoding.UTF8);
        File.WriteAllText(Path.Combine(root, "nested", "child.txt"), "alpha beta\n東京", Encoding.BigEndianUnicode);
        var zipPath = Path.Combine(root, "sample.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("inside.txt").Open());
            writer.WriteLine("archive needle");
        }
        var docxPath = Path.Combine(root, "sample.docx");
        using (var archive = ZipFile.Open(docxPath, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open());
            writer.Write("<w:document xmlns:w='urn:test'><w:p><w:r><w:t>office needle</w:t></w:r></w:p></w:document>");
        }
        var assertions = new List<string>();
        var pdf = new PdfDocumentBuilder();
        var font = pdf.AddStandard14Font(Standard14Font.Helvetica);
        pdf.AddPage(595, 842).AddText("PDF needle", 12, new PdfPoint(50, 700), font);
        File.WriteAllBytes(Path.Combine(root, "sample.pdf"), pdf.Build());
        void Assert(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            assertions.Add(label);
        }
        async Task<SearchResult[]> Search(SearchOptions options)
        {
            var results = new ConcurrentBag<SearchResult>();
            using var pause = new ManualResetEventSlim(true);
            await new SearchEngine().RunAsync(options, results.Add, new SearchStatistics(), pause, CancellationToken.None);
            return results.ToArray();
        }

        var basic = new SearchOptions(root, "*.txt;*.log");
        Assert((await Search(basic)).Length == 4, "Recursive wildcard and multiple patterns");
        Assert((await Search(basic with { Subfolders = false })).Length == 3, "Nonrecursive search");
        Assert((await Search(basic with { Content = "alpha AND beta", ContentMode = "Boolean" })).Length == 3, "Boolean AND across different lines");
        Assert((await Search(basic with { Content = "alpha AND beta", ContentMode = "Boolean", LineByLine = true })).Length == 2, "Boolean line-by-line mode");
        Assert((await Search(basic with { Content = "alpha NOT forbidden", ContentMode = "Boolean" })).Length == 3, "Boolean NOT across whole file");
        Assert((await Search(basic with { Content = "alpha AND (beta OR missing)", ContentMode = "Boolean" })).Length == 3, "Boolean parentheses");
        Assert((await Search(basic with { Content = "東京" })).Length == 2, "UTF-8 and UTF-16 BOM decoding");
        Assert((await Search(basic with { Content = "ALPHA", ContentCase = true })).Length == 1, "Case-sensitive content");
        Assert((await Search(basic with { Content = @"Invoice \d{5}", ContentMode = "Regex" })).Length == 1, "Regex content search");
        Assert((await Search(basic with { Locations = root + ";!" + Path.Combine(root, "nested") })).Length == 3, "Excluded locations");
        Assert((await Search(basic with { Locations = root + ";" + Path.Combine(root, "nested") })).Length == 4, "Overlapping roots deduplicated");
        Assert((await Search(basic with { MinimumBytes = 10000 })).Length == 0, "Minimum file size filter");
        Assert((await Search(basic with { MaximumBytes = new FileInfo(Path.Combine(root, "alpha.txt")).Length, FileName = "alpha.txt" })).Length == 0, "Strict maximum boundary");
        Assert((await Search(basic with { ModifiedAfter = DateTime.Today.AddDays(1) })).Length == 0, "Modified date filter");
        var zipResults = await Search(new SearchOptions(root, "*.txt", "archive needle", SearchArchives: true));
        Assert(zipResults.Length == 1, "ZIP entry content search");
        Assert(zipResults[0].PhysicalPath == zipPath && zipResults[0].FullPath != zipPath, "ZIP entry targets its containing archive");
        Assert((await Search(basic)).Single(result => result.Name == "alpha.txt").PhysicalPath == Path.Combine(root, "alpha.txt"), "Ordinary result targets its own file");
        Assert((await Search(new SearchOptions(root, "*.docx", "office needle"))).Length == 1, "Office XML text extraction");
        Assert((await Search(new SearchOptions(root, "*.pdf", "PDF needle"))).Length == 1, "PDF content extraction");
        Assert(new ExpressionMatcher("alpha", "Wildcard", false).IsMatch("my-alpha.txt"), "Unadorned name is a substring");
        Assert(!new ExpressionMatcher("alpha*", "Wildcard", false).IsMatch("my-alpha.txt"), "Wildcard expression has strict boundaries");
        Assert(!new ExpressionMatcher("cat", "Plain text", false, true).IsMatch("catalog"), "Whole word boundaries");
        var invalidRejected = false;
        try { _ = new ExpressionMatcher("[", "Regex", false); } catch (ArgumentException) { invalidRejected = true; }
        Assert(invalidRejected, "Invalid regex rejected before search");

        using (var cancel = new CancellationTokenSource())
        using (var pause = new ManualResetEventSlim(false))
        {
            cancel.CancelAfter(100);
            var cancelled = false;
            try { await new SearchEngine().RunAsync(basic, _ => { }, new SearchStatistics(), pause, cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            Assert(cancelled, "Cancellation works while paused");
        }

        var benchmarkRoot = Path.Combine(root, "benchmark");
        Directory.CreateDirectory(benchmarkRoot);
        for (var i = 0; i < 2500; i++) File.WriteAllText(Path.Combine(benchmarkRoot, $"file-{i:D4}.txt"), $"A deterministic search fixture {i}.\nneedle value\n");
        var stopwatch = Stopwatch.StartNew();
        var benchmark = await Search(new SearchOptions(benchmarkRoot, "*.txt", "needle"));
        stopwatch.Stop();
        Assert(benchmark.Length == 2500, "All 2500 benchmark matches returned");
        var historyPath = Path.Combine(root, "history.json");
        var history = new SearchHistory(historyPath);
        var viewModel = new MainViewModel(history) { Locations = root, FileName = "alpha.txt", Content = "alpha", WorkerCount = "2" };
        await viewModel.StartCommand.ExecuteAsync(null);
        Assert(viewModel.Results.Count == 1 && viewModel.HasResults && !viewModel.IsSearching, "MVVM search command publishes actual results");
        Assert(viewModel.SearchThreads == 2 && !viewModel.ShowSearchThreads, "Parallel content search exposes its worker count with the pane initially hidden");
        viewModel.ShowSearchThreads = true;
        Assert(viewModel.WorkerRows.Single() is { Name: "alpha.txt", Lines: 4, State: "Idle" }, "The worker pane reports the scanned file and its line count");
        var reloadedHistory = new SearchHistory(historyPath);
        Assert(reloadedHistory.FileNames.Single() == "alpha.txt" && reloadedHistory.Contents.Single() == "alpha" &&
               reloadedHistory.Locations.Single() == root, "Search history persists the three input values independently");
        for (var i = 0; i < 12; i++) history.Remember(new SearchOptions(root, $"file-{i}"));
        history.Remember(new SearchOptions(root, "file-5"));
        var recentNames = new SearchHistory(historyPath).FileNames;
        Assert(recentNames.Count == 10 && recentNames[0] == "file-5" && recentNames.Distinct().Count() == 10,
            "Search history keeps ten unique entries and moves reused values to the top");
        viewModel.SelectedResult = viewModel.Results[0];
        Assert(viewModel.PreviewText.Contains("alpha") && viewModel.OpenFileCommand.CanExecute(null), "Selected result exposes preview and open command");
        viewModel.Shutdown();
        File.WriteAllText("verification-results.json", JsonSerializer.Serialize(new
        {
            passed = assertions.Count, assertions,
            benchmark = new { files = 2500, elapsedMilliseconds = stopwatch.Elapsed.TotalMilliseconds, workers = Environment.ProcessorCount },
            note = "Local warm-cache fixture timing; not a comparison against the original application."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
