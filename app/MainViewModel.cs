using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace XtraSearch;

/// <summary>Coordinates search commands and publishes batched worker results to WPF.</summary>
public partial class MainViewModel : ObservableObject
{
    private const int BytesPerKilobyte = 1024;
    private readonly ConcurrentQueue<SearchResult> pending = new();
    private readonly ManualResetEventSlim pause = new(true);
    private readonly DispatcherTimer timer;
    private readonly Stopwatch stopwatch = new();
    private readonly SearchHistory history;
    private readonly Dictionary<int, int> workerRowIndices = new();
    private SearchStatistics statistics = new();
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? previewCancellation;

    public ObservableCollection<SearchResult> Results { get; } = [];
    public ObservableCollection<string> FileNameHistory => history.FileNames;
    public ObservableCollection<string> ContentHistory => history.Contents;
    public ObservableCollection<string> LocationHistory => history.Locations;
    public ObservableCollection<SearchWorkerRow> WorkerRows { get; } = [];
    public ObservableCollection<SearchModeChoice> NameModes { get; } =
        [new("Boolean", "Boolean"), new("Regex", "Regular Expression"), new("Wildcard", "Wildcards")];
    public ObservableCollection<SearchModeChoice> ContentModes { get; } =
        [new("Boolean", "Boolean"), new("Regex", "Regular Expression")];
    public IReadOnlyList<string> SizeUnits { get; } = ["KB", "MB", "GB"];
    [ObservableProperty] private string fileName = "*";
    [ObservableProperty] private string content = "";
    [ObservableProperty] private string locations = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    [ObservableProperty] private string nameMode = "Wildcard";
    [ObservableProperty] private string contentMode = "Boolean";
    [ObservableProperty] private bool nameCase;
    [ObservableProperty] private bool contentCase;
    [ObservableProperty] private bool subfolders = true;
    [ObservableProperty] private bool wholeWord;
    [ObservableProperty] private bool lineByLine;
    [ObservableProperty] private bool includeHidden = true;
    [ObservableProperty] private bool skipBinary = true;
    [ObservableProperty] private bool searchArchives;
    [ObservableProperty] private string minimumSize = "";
    [ObservableProperty] private string maximumSize = "";
    [ObservableProperty] private string sizeUnit = "KB";
    [ObservableProperty] private string modifiedAfter = "";
    [ObservableProperty] private string modifiedBefore = "";
    [ObservableProperty] private string createdAfter = "";
    [ObservableProperty] private string createdBefore = "";
    [ObservableProperty] private string workerCount = "0";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasParallelContentSearch), nameof(SearchThreadsText))]
    private int searchThreads;
    [ObservableProperty] private bool showSearchThreads;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(StartCommand)), NotifyCanExecuteChangedFor(nameof(StopCommand)), NotifyCanExecuteChangedFor(nameof(PauseCommand)), NotifyCanExecuteChangedFor(nameof(NewSearchCommand))]
    private bool isSearching;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(PauseLabel))] private bool isPaused;
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool hasResults;
    [ObservableProperty] private string status = "Ready";
    [ObservableProperty] private string foundText = "0";
    [ObservableProperty] private string checkedText = "0";
    [ObservableProperty] private string elapsedText = "0.00 s";
    [ObservableProperty] private string skippedText = "0";
    [ObservableProperty] private string scannedText = "0 B";
    [ObservableProperty] private string previewTitle = "File preview";
    [ObservableProperty] private string previewText = "Select a result to see its matching lines or a text preview.";
    [ObservableProperty] private string errorText = "No skipped files or errors.";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(OpenFileCommand)), NotifyCanExecuteChangedFor(nameof(OpenFolderCommand)), NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    private SearchResult? selectedResult;
    public string PauseLabel => IsPaused ? "Resume" : "Pause";
    public string WorkerHint => $"Automatic: {Environment.ProcessorCount} logical processors";
    public bool HasParallelContentSearch => SearchThreads > 1;
    public string SearchThreadsText => $"{SearchThreads:N0} search threads";

    partial void OnShowSearchThreadsChanged(bool value)
    {
        if (value) PublishWorkerProgress();
    }

    public MainViewModel() : this(new SearchHistory()) { }

    internal MainViewModel(SearchHistory history)
    {
        this.history = history;
        Results.CollectionChanged += (_, _) => HasResults = Results.Count > 0;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SearchEngine.NotificationMilliseconds) };
        timer.Tick += (_, _) => PublishProgress();
    }

    private bool CanStart() => !IsSearching;
    private bool CanStop() => IsSearching;
    private bool HasSelection() => SelectedResult != null;

    [RelayCommand]
    private void EditRegex(string? target)
    {
        var fileNameTarget = target == "FileName";
        if ((fileNameTarget ? NameMode : ContentMode) != "Regex") return;

        var editor = new RegexEditorViewModel(fileNameTarget, fileNameTarget ? FileName : Content,
            fileNameTarget ? NameCase : ContentCase, !fileNameTarget && WholeWord);
        var dialog = new RegexEditorWindow(editor) { Owner = Application.Current.MainWindow };
        if (dialog.ShowDialog() != true) return;

        if (fileNameTarget)
        {
            FileName = editor.Pattern;
            NameCase = editor.MatchCase;
        }
        else
        {
            Content = editor.Pattern;
            ContentCase = editor.MatchCase;
            WholeWord = editor.WholeWords;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        SearchOptions options;
        try
        {
            options = CaptureOptions();
            _ = new ExpressionMatcher(options.FileName, options.NameMode, options.NameCase);
            if (options.Content.Length > 0) _ = new ExpressionMatcher(options.Content, options.ContentMode, options.ContentCase, options.WholeWord, options.LineByLine);
        }
        catch (Exception exception) { Status = exception.Message; return; }
        history.Remember(options);
        Results.Clear();
        pending.Clear();
        SelectedResult = null;
        PreviewTitle = "File preview";
        PreviewText = "Results will appear as matching files are found.";
        statistics = new SearchStatistics();
        ResetWorkerProgress(options.Content.Length > 0 ? SearchEngine.ResolveWorkerCount(options) : 0);
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        pause.Set();
        IsPaused = false;
        IsSearching = true;
        Status = "Searching…";
        stopwatch.Restart();
        timer.Start();
        try
        {
            await Task.Run(() => new SearchEngine().RunAsync(options, pending.Enqueue, statistics, pause, cancellation.Token));
            Status = "Search complete";
        }
        catch (OperationCanceledException) { Status = "Search stopped · partial results retained"; }
        catch (Exception exception) { Status = exception.Message; }
        finally
        {
            stopwatch.Stop();
            while (!pending.IsEmpty) { PublishProgress(); await Task.Delay(16); }
            PublishProgress();
            timer.Stop();
            IsSearching = false;
            IsPaused = false;
            pause.Set();
            if (!HasResults) PreviewText = "No matching files. Try a broader name pattern, different text, or another location.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop() { cancellation?.Cancel(); pause.Set(); }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Pause()
    {
        IsPaused = !IsPaused;
        if (IsPaused) { pause.Reset(); Status = "Paused"; }
        else { pause.Set(); Status = "Searching…"; }
    }

    public void Shutdown() { cancellation?.Cancel(); pause.Set(); previewCancellation?.Cancel(); }

    private void PublishProgress()
    {
        for (var i = 0; i < 500 && pending.TryDequeue(out var result); i++) Results.Add(result);
        FoundText = Interlocked.Read(ref statistics.Found).ToString("N0");
        CheckedText = Interlocked.Read(ref statistics.Checked).ToString("N0");
        SkippedText = Interlocked.Read(ref statistics.Skipped).ToString("N0");
        ScannedText = $"{Interlocked.Read(ref statistics.Bytes) / 1048576d:N2} MB";
        ElapsedText = $"{stopwatch.Elapsed.TotalSeconds:N2} s";
        ErrorText = statistics.Errors.IsEmpty ? "No skipped files or errors." : string.Join(Environment.NewLine + Environment.NewLine, statistics.Errors);
        if (ShowSearchThreads) PublishWorkerProgress();
    }

    private void ResetWorkerProgress(int workers)
    {
        ShowSearchThreads = false;
        WorkerRows.Clear();
        workerRowIndices.Clear();
        SearchThreads = workers;
    }

    private void PublishWorkerProgress()
    {
        var files = statistics.WorkerFiles;
        for (var index = 0; index < files.Length; index++)
        {
            var file = Volatile.Read(ref files[index]);
            if (file is null) continue;
            var row = file.Snapshot(index + 1);
            if (workerRowIndices.TryGetValue(row.Worker, out var rowIndex))
            {
                if (WorkerRows[rowIndex] != row) WorkerRows[rowIndex] = row;
            }
            else
            {
                workerRowIndices.Add(row.Worker, WorkerRows.Count);
                WorkerRows.Add(row);
            }
        }
    }

    public SearchOptions CaptureOptions()
    {
        long? Size(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var bytesPerUnit = SizeUnit switch
            {
                "MB" => BytesPerKilobyte * BytesPerKilobyte,
                "GB" => BytesPerKilobyte * BytesPerKilobyte * BytesPerKilobyte,
                _ => BytesPerKilobyte
            };
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var size) || size < 0 || size > long.MaxValue)
                throw new ArgumentException($"Enter a non-negative file size in {SizeUnit} (use a dot for decimals).");
            var bytes = decimal.Truncate(size * bytesPerUnit);
            if (bytes > long.MaxValue)
                throw new ArgumentException("The file size exceeds the supported limit.");
            return (long)bytes;
        }
        static DateTime? Date(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new ArgumentException("Enter dates as yyyy-MM-dd.");
            return date;
        }
        if (!int.TryParse(WorkerCount, out var workers) || workers is < 0 or > 1024) throw new ArgumentException("Workers must be between 0 (automatic) and 1024.");
        var minimum = Size(MinimumSize);
        var maximum = Size(MaximumSize);
        if (minimum.HasValue && maximum.HasValue && minimum >= maximum) throw new ArgumentException("Minimum size must be smaller than maximum size.");
        var after = Date(ModifiedAfter);
        var before = Date(ModifiedBefore);
        var createdFrom = Date(CreatedAfter);
        var createdTo = Date(CreatedBefore);
        if (after > before || createdFrom > createdTo) throw new ArgumentException("The start date must precede the end date.");
        return new SearchOptions(Locations, FileName, Content, NameMode, ContentMode, NameCase, ContentCase, Subfolders,
            WholeWord, LineByLine, IncludeHidden, SkipBinary, SearchArchives, minimum, maximum, after, before, createdFrom, createdTo, workers);
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a search folder", Multiselect = true };
        if (dialog.ShowDialog() == true) Locations = string.Join(';', dialog.FolderNames);
    }

    partial void OnSelectedResultChanged(SearchResult? value) => _ = LoadPreviewAsync(value);

    private async Task LoadPreviewAsync(SearchResult? result)
    {
        previewCancellation?.Cancel();
        if (result == null) return;
        previewCancellation = new CancellationTokenSource();
        var token = previewCancellation.Token;
        PreviewTitle = result.Name;
        PreviewText = "Loading preview…";
        try
        {
            var text = result.Preview.Length > 0 ? result.Preview : await Task.Run(() =>
            {
                if (result.FullPath.Contains('|')) return "ZIP entry. Run a content search to display matching lines.";
                var lines = DocumentText.Read(result.FullPath, true, token).Take(SearchEngine.PreviewLines)
                    .Select((line, index) => $"{index + 1,6}  {line[..Math.Min(line.Length, 2000)]}");
                return string.Join(Environment.NewLine, lines);
            }, token);
            if (!token.IsCancellationRequested) PreviewText = text.Length > 0 ? text : "This file contains no readable text.";
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!token.IsCancellationRequested) PreviewText = exception.Message; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFile() => ShellOpen(SelectedResult!.PhysicalPath);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder() => RevealInExplorer(SelectedResult!.PhysicalPath);

    /// <summary>Opens the containing folder and selects the result's physical file.</summary>
    internal void RevealInExplorer(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
        catch (Exception exception) { Status = exception.Message; }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyPath() { Clipboard.SetText(SelectedResult!.FullPath); Status = "Path copied"; }

    private void ShellOpen(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception exception) { Status = exception.Message; }
    }

    [RelayCommand(CanExecute = nameof(HasResults))]
    private void Export()
    {
        var results = Results.ToArray();
        var count = results.Length;
        if (count > 100)
        {
            var limitDialog = new ExportLimitDialog(count) { Owner = Application.Current.MainWindow };
            if (limitDialog.ShowDialog() != true) return;
            count = limitDialog.ExportCount;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export results",
            Filter = "CSV file|*.csv|HTML report|*.html|Text file|*.txt",
            FileName = "XtraSearch-results"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var exportResults = new ArraySegment<SearchResult>(results, 0, count);
            using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
            {
                if (dialog.FilterIndex == 2)
                    ResultsExport.WriteHtml(writer, exportResults, results.Length, DateTimeOffset.Now);
                else ResultsExport.WriteCsv(writer, exportResults);
            }
            Status = $"Exported {count:N0} results";
        }
        catch (Exception exception) { Status = exception.Message; }
    }

    [RelayCommand]
    private void SaveSearch()
    {
        var dialog = new SaveFileDialog { Filter = "XtraSearch criteria|*.xsearch", FileName = "Search.xsearch" };
        if (dialog.ShowDialog() != true) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(CaptureOptions(), new JsonSerializerOptions { WriteIndented = true })); Status = "Search saved"; }
        catch (Exception exception) { Status = exception.Message; }
    }

    [RelayCommand]
    private void LoadSearch()
    {
        var dialog = new OpenFileDialog { Filter = "XtraSearch criteria|*.xsearch" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var options = JsonSerializer.Deserialize<SearchOptions>(File.ReadAllText(dialog.FileName)) ?? throw new InvalidDataException("Invalid search file.");
            Locations = options.Locations; FileName = options.FileName; Content = options.Content;
            if (options.NameMode == "Plain text") AddLegacyMode(NameModes, options.NameMode);
            if (options.ContentMode is "Plain text" or "Boolean Regex") AddLegacyMode(ContentModes, options.ContentMode);
            NameMode = options.NameMode; ContentMode = options.ContentMode; NameCase = options.NameCase; ContentCase = options.ContentCase;
            Subfolders = options.Subfolders; WholeWord = options.WholeWord; LineByLine = options.LineByLine;
            IncludeHidden = options.IncludeHidden; SkipBinary = options.SkipBinary; SearchArchives = options.SearchArchives;
            SizeUnit = "KB";
            MinimumSize = (options.MinimumBytes / (decimal)BytesPerKilobyte)?.ToString(CultureInfo.InvariantCulture) ?? "";
            MaximumSize = (options.MaximumBytes / (decimal)BytesPerKilobyte)?.ToString(CultureInfo.InvariantCulture) ?? "";
            ModifiedAfter = options.ModifiedAfter?.ToString("yyyy-MM-dd") ?? ""; ModifiedBefore = options.ModifiedBefore?.ToString("yyyy-MM-dd") ?? "";
            CreatedAfter = options.CreatedAfter?.ToString("yyyy-MM-dd") ?? ""; CreatedBefore = options.CreatedBefore?.ToString("yyyy-MM-dd") ?? "";
            WorkerCount = options.Workers.ToString(); Status = "Search loaded";
        }
        catch (Exception exception) { Status = exception.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void NewSearch()
    {
        FileName = "*"; Content = ""; NameMode = "Wildcard"; ContentMode = "Boolean";
        ResetWorkerProgress(0);
        NameCase = ContentCase = WholeWord = LineByLine = SearchArchives = false;
        Subfolders = IncludeHidden = SkipBinary = true;
        MinimumSize = MaximumSize = ModifiedAfter = ModifiedBefore = CreatedAfter = CreatedBefore = "";
        SizeUnit = "KB";
        WorkerCount = "0"; Results.Clear(); SelectedResult = null;
        statistics = new SearchStatistics(); stopwatch.Reset(); PublishProgress();
        PreviewTitle = "File preview"; PreviewText = "Select a result to see its matching lines or a text preview."; Status = "Ready";
    }

    private static void AddLegacyMode(ObservableCollection<SearchModeChoice> choices, string mode)
    {
        if (!choices.Any(choice => choice.Value == mode)) choices.Add(new SearchModeChoice(mode, mode));
    }

    /// <summary>Extracts the current guide beside the executable and opens it in the default browser.</summary>
    [RelayCommand]
    private void Help()
    {
        const string guideName = "XtraSearchGuide.html";
        try
        {
            var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var guidePath = Path.Combine(executableDirectory, guideName);
            using var resource = typeof(MainViewModel).Assembly.GetManifestResourceStream(guideName)
                ?? throw new InvalidOperationException("The embedded search guide is missing.");

            var temporaryPath = Path.Combine(executableDirectory, $".{guideName}.{Guid.NewGuid():N}.tmp");
            try
            {
                using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                    resource.CopyTo(output);

                File.Move(temporaryPath, guidePath, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }

            ShellOpen(guidePath);
        }
        catch (Exception exception)
        {
            Status = $"Could not extract the search guide: {exception.Message}";
        }
    }
}
