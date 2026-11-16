using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace XtraSearch;

/// <summary>Displays the regex editor, its sample highlights, and insertion tools.</summary>
public partial class RegexEditorWindow : Window
{
    private static readonly Brush MatchBrush = new SolidColorBrush(Color.FromRgb(42, 75, 109));
    private static readonly Brush SelectedMatchBrush = new SolidColorBrush(Color.FromRgb(0, 102, 126));
    private readonly RegexEditorViewModel editor;
    private readonly List<Run> highlights = [];
    private int selectedMatch;

    internal RegexEditorWindow(RegexEditorViewModel editor)
    {
        this.editor = editor;
        InitializeComponent();
        DataContext = editor;
        Examples.DisplayMemberPath = nameof(RegexExample.Name);
        Examples.ItemsSource = editor.IsFileName
            ? new RegexExample[]
            {
                new("File extension", @"\.(pdf|docx)$", "report.pdf\r\nnotes.txt\r\nletter.docx"),
                new("Numbered files", @"^report-(?<number>\d+)\.pdf$", "report-12.pdf\r\nreport-draft.pdf\r\nreport-48.pdf")
            }
            : new RegexExample[]
            {
                new("Invoice number", @"Invoice\s+(?<number>\d+)", "Invoice 12345\r\nDraft\r\nInvoice 67890"),
                new("Date (YYYY-MM-DD)", @"\b\d{4}-\d{2}-\d{2}\b", "Logged 2026-11-16\r\nDate pending\r\nLogged 2026-12-01")
            };
        SourceInitialized += (_, _) => MainWindow.EnableDarkTitleBar(this);
        Loaded += (_, _) =>
        {
            PatternInput.Focus();
            PatternInput.SelectAll();
            UpdatePreview();
            editor.Refresh();
        };
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RegexEditorViewModel.Preview)) UpdatePreview();
            if (args.PropertyName == nameof(RegexEditorViewModel.Sample))
                SampleHint.SetCurrentValue(TextBlock.TextProperty, editor.SampleHint);
        };
        Closed += (_, _) => editor.CancelRefresh();
    }

    private void UpdatePreview()
    {
        var preview = editor.Preview;
        var occurrences = preview?.Occurrences;
        PatternStatus.Text = preview == null ? "Testing…" : preview.CanApply ? "Valid pattern" : preview.Message;
        PatternStatus.Foreground = preview is { CanApply: false, ErrorOffset: not null }
            ? Brushes.LightSalmon : Brushes.LightSteelBlue;
        ErrorPosition.Visibility = preview?.ErrorOffset != null ? Visibility.Visible : Visibility.Collapsed;
        ErrorPosition.Content = preview?.ErrorOffset is int offset ? $"Go to position {offset + 1}" : "";
        ResultSummary.Text = preview?.CanApply == true ? preview.Message : "";
        ApplyButton.IsEnabled = preview?.CanApply == true;
        HighlightedSample.Inlines.Clear();
        highlights.Clear();
        selectedMatch = 0;

        if (occurrences != null)
        {
            var start = 0;
            foreach (var occurrence in occurrences)
            {
                if (occurrence.Start > start)
                    HighlightedSample.Inlines.Add(new Run(editor.Sample[start..occurrence.Start]));
                var value = occurrence.Length == 0 ? "▏" : editor.Sample.Substring(occurrence.Start, occurrence.Length);
                var highlight = new Run(value) { Background = MatchBrush };
                HighlightedSample.Inlines.Add(highlight);
                highlights.Add(highlight);
                start = occurrence.Start + occurrence.Length;
            }
            HighlightedSample.Inlines.Add(new Run(editor.Sample[start..]));
        }
        else if (preview?.CanApply == true && editor.Sample.Length <= RegexEditorViewModel.SampleLimit)
        {
            HighlightedSample.Inlines.Add(new Run(editor.Sample));
        }

        PreviewScroll.ScrollToHome();
        UpdateSelectedMatch(scroll: false);
    }

    private void UpdateSelectedMatch(bool scroll)
    {
        var occurrences = editor.Preview?.Occurrences;
        PreviousMatch.IsEnabled = selectedMatch > 0;
        NextMatch.IsEnabled = occurrences != null && selectedMatch < occurrences.Count - 1;
        MatchDetails.Visibility = occurrences == null ? Visibility.Collapsed : Visibility.Visible;
        if (occurrences == null) return;

        var occurrence = occurrences[selectedMatch];
        var highlight = highlights[selectedMatch];
        highlight.Background = SelectedMatchBrush;
        MatchDetails.Text = $"Match {selectedMatch + 1:N0} of {occurrences.Count:N0} · Line {occurrence.Line:N0}, column {occurrence.Column:N0}";
        if (occurrence.Length == 0) MatchDetails.Text += " · Zero-width match";
        if (occurrence.Groups.Length > 0) MatchDetails.Text += Environment.NewLine + occurrence.Groups;
        MatchDetails.ScrollToHome();
        if (scroll)
        {
            var bounds = highlight.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            PreviewScroll.ScrollToVerticalOffset(Math.Max(0, bounds.Top - 8));
        }
    }

    private void OnPreviousMatch(object sender, RoutedEventArgs e) => MoveMatch(-1);

    private void OnNextMatch(object sender, RoutedEventArgs e) => MoveMatch(1);

    private void MoveMatch(int offset)
    {
        highlights[selectedMatch].Background = MatchBrush;
        selectedMatch += offset;
        UpdateSelectedMatch(scroll: true);
    }

    private void OnFindError(object sender, RoutedEventArgs e)
    {
        if (editor.Preview?.ErrorOffset is not int offset) return;
        PatternInput.Focus();
        PatternInput.Select(offset, 0);
    }

    private void OnInsertSnippet(object sender, RoutedEventArgs e)
    {
        var fragment = (string)((Button)sender).Tag;
        InsertPattern(fragment, fragment == "(?<name>)" ? 3 : fragment.Length, fragment == "(?<name>)" ? 4 : 0);
    }

    private void OnInsertLiteral(object sender, RoutedEventArgs e)
    {
        if (LiteralInput.Text.Length == 0) return;
        var fragment = Regex.Escape(LiteralInput.Text);
        InsertPattern(fragment, fragment.Length, 0);
    }

    private void InsertPattern(string fragment, int selectionOffset, int selectionLength)
    {
        var start = PatternInput.SelectionStart;
        PatternInput.SelectedText = fragment;
        PatternInput.Focus();
        PatternInput.Select(start + selectionOffset, selectionLength);
    }

    private void OnExampleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Examples.SelectedItem is not RegexExample example) return;
        editor.Pattern = example.Pattern;
        editor.Sample = example.Sample;
        Examples.SelectedIndex = -1;
        PatternInput.Focus();
        PatternInput.SelectAll();
    }

    private async void OnLoadSample(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Load sample text", Filter = "Text files|*.txt;*.log;*.csv|All files|*.*" };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            using var reader = File.OpenText(dialog.FileName);
            var buffer = new char[RegexEditorViewModel.SampleLimit + 1];
            var length = await reader.ReadBlockAsync(buffer.AsMemory());
            if (!IsLoaded) return;
            editor.Sample = new string(buffer, 0, Math.Min(length, RegexEditorViewModel.SampleLimit));
            SampleHint.SetCurrentValue(TextBlock.TextProperty, length > RegexEditorViewModel.SampleLimit
                ? $"Loaded the first {RegexEditorViewModel.SampleLimit:N0} characters." : editor.SampleHint);
            SampleInput.Focus();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SampleHint.SetCurrentValue(TextBlock.TextProperty, $"Could not load the sample. {exception.Message}");
        }
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (editor.Preview?.CanApply == true) DialogResult = true;
    }

    private sealed record RegexExample(string Name, string Pattern, string Sample);
}
