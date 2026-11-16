using System.Windows;

namespace XtraSearch;

/// <summary>Prompts for a valid row count before exporting a large result set.</summary>
public partial class ExportLimitDialog : Window
{
    private readonly int total;

    public int ExportCount { get; private set; }

    public ExportLimitDialog(int total)
    {
        this.total = total;
        InitializeComponent();
        TotalText.Text = $"{total:N0} results available";
        LimitHint.Text = $"Enter a whole number from 1 to {total:N0}.";
        LimitInput.Text = total.ToString();
        LimitInput.SelectAll();
        LimitInput.TextChanged += (_, _) => ContinueButton.IsEnabled = TryGetCount(out _);
        SourceInitialized += (_, _) => MainWindow.EnableDarkTitleBar(this);
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        if (!TryGetCount(out var count)) return;
        ExportCount = count;
        DialogResult = true;
    }

    private bool TryGetCount(out int count) =>
        int.TryParse(LimitInput.Text, out count) && count > 0 && count <= total;
}
