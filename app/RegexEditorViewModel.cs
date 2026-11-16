using System.Diagnostics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XtraSearch;

/// <summary>Validates a pattern and evaluates it against editable sample lines.</summary>
internal sealed partial class RegexEditorViewModel : ObservableObject
{
    internal const int SampleLimit = 100_000;
    internal const int PreviewLimit = 500;
    private CancellationTokenSource? refresh;

    [ObservableProperty] private string pattern = "";
    [ObservableProperty] private string sample = "";
    [ObservableProperty] private bool matchCase;
    [ObservableProperty] private bool wholeWords;
    [ObservableProperty] private RegexPreview? preview;

    public bool IsFileName { get; }
    public string Heading => IsFileName ? "File name" : "File contents";
    public string SampleHint => IsFileName
        ? "Enter one file name per line."
        : "Paste sample text. The search tests each line separately.";

    public RegexEditorViewModel(bool isFileName, string pattern, bool matchCase, bool wholeWords)
    {
        IsFileName = isFileName;
        this.pattern = pattern;
        this.matchCase = matchCase;
        this.wholeWords = wholeWords;
    }

    partial void OnPatternChanged(string value) => Refresh();
    partial void OnSampleChanged(string value) => Refresh();
    partial void OnMatchCaseChanged(bool value) => Refresh();
    partial void OnWholeWordsChanged(bool value) => Refresh();

    public void CancelRefresh() => refresh?.Cancel();

    public async void Refresh()
    {
        CancelRefresh();
        var request = new CancellationTokenSource();
        refresh = request;
        var currentPattern = Pattern;
        var currentSample = Sample;
        var currentMatchCase = MatchCase;
        var currentWholeWords = WholeWords;
        Preview = null;

        try
        {
            await Task.Delay(250, request.Token);
            var result = await Task.Run(() => Evaluate(currentPattern, currentSample, currentMatchCase,
                currentWholeWords, IsFileName, request.Token), request.Token);
            if (!request.IsCancellationRequested) Preview = result;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (refresh == request) refresh = null;
            request.Dispose();
        }
    }

    /// <summary>Evaluates each sample line using the same regex options as the search engine.</summary>
    internal static RegexPreview Evaluate(string pattern, string sample, bool matchCase,
        bool wholeWords, bool isFileName, CancellationToken cancellation = default)
    {
        if (pattern.Length == 0 || (isFileName && string.IsNullOrWhiteSpace(pattern)))
            return new("Enter a regular expression.", false);

        Regex regex;
        try
        {
            regex = ExpressionMatcher.CreateRegex(pattern, matchCase, wholeWords, compile: false);
        }
        catch (RegexParseException exception)
        {
            var prefixLength = wholeWords ? "(?<!\\w)(?:".Length : 0;
            var offset = Math.Clamp(exception.Offset - prefixLength, 0, pattern.Length);
            return new(exception.Message, false, offset);
        }
        catch (ArgumentException exception)
        {
            return new(exception.Message, false);
        }

        if (sample.Length == 0) return new("Valid pattern. Add a sample to test it.", true);
        if (sample.Length > SampleLimit) return new($"Use a sample of {SampleLimit:N0} characters or fewer.", true);

        var groupNames = regex.GetGroupNames().Skip(1).ToArray();
        var highlights = new List<RegexOccurrence>();
        var clock = Stopwatch.StartNew();
        var linesMatched = 0;
        var matchesFound = 0;
        var lineNumber = 0;
        var start = 0;
        using var reader = new StringReader(sample);

        try
        {
            while (reader.ReadLine() is { } line)
            {
                cancellation.ThrowIfCancellationRequested();
                if (clock.Elapsed.TotalSeconds >= 2) return new("The sample test timed out. Try a smaller sample or a simpler pattern.", true);
                lineNumber++;

                var match = isFileName && line.Length == 0 ? Match.Empty : regex.Match(line);
                if (match.Success) linesMatched++;
                while (match.Success)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (clock.Elapsed.TotalSeconds >= 2) return new("The sample test timed out. Try a smaller sample or a simpler pattern.", true);
                    matchesFound++;
                    if (highlights.Count < PreviewLimit)
                    {
                        highlights.Add(new(start + match.Index, match.Length, lineNumber, match.Index + 1,
                            DescribeGroups(match, groupNames)));
                    }
                    match = match.NextMatch();
                }

                start += line.Length;
                if (start < sample.Length && sample[start] == '\r') start++;
                if (start < sample.Length && sample[start] == '\n') start++;
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return new("The sample test timed out. Try a smaller sample or a simpler pattern.", true);
        }

        if (matchesFound == 0) return new("No matches.", true);

        var unit = isFileName ? (linesMatched == 1 ? "file name" : "file names") : (linesMatched == 1 ? "matching line" : "matching lines");
        var occurrence = matchesFound == 1 ? "match" : "matches";
        var summary = $"{linesMatched:N0} {unit} · {matchesFound:N0} {occurrence}";
        if (matchesFound > PreviewLimit)
            summary += $" · Showing the first {PreviewLimit:N0}";
        return new(summary, true, Occurrences: highlights);
    }

    private static string DescribeGroups(Match match, string[] names)
    {
        const int groupLimit = 25;
        const int valueLimit = 160;
        var descriptions = names.Take(groupLimit).Select(name =>
        {
            var group = match.Groups[name];
            if (!group.Success) return $"{name} (not matched)";
            var value = group.ValueSpan;
            return value.Length <= valueLimit ? $"{name} = {value}" : $"{name} = {value[..valueLimit]}…";
        });
        if (names.Length > groupLimit) descriptions = descriptions.Append($"… Showing the first {groupLimit} groups.");
        return string.Join(Environment.NewLine, descriptions);
    }
}

/// <summary>Describes the newest completed preview and any position reported by the regex parser.</summary>
internal sealed record RegexPreview(string Message, bool CanApply, int? ErrorOffset = null,
    IReadOnlyList<RegexOccurrence>? Occurrences = null);

/// <summary>Locates a regex occurrence and describes its captured groups.</summary>
internal sealed record RegexOccurrence(int Start, int Length, int Line, int Column, string Groups);
