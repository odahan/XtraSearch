using System.Collections.ObjectModel;
using System.Text.Json;

namespace XtraSearch;

/// <summary>Stores the most recently used values for the three editable search fields.</summary>
internal sealed class SearchHistory
{
    private const int Limit = 10;
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XtraSearch", "history.json");
    private readonly string historyPath;

    public ObservableCollection<string> FileNames { get; } = [];
    public ObservableCollection<string> Contents { get; } = [];
    public ObservableCollection<string> Locations { get; } = [];

    public SearchHistory(string? path = null)
    {
        historyPath = path ?? DefaultPath;
        try
        {
            var saved = JsonSerializer.Deserialize<Entries>(File.ReadAllText(historyPath));
            if (saved is null) return;
            Restore(FileNames, saved.FileNames, StringComparer.Ordinal);
            Restore(Contents, saved.Contents, StringComparer.Ordinal);
            Restore(Locations, saved.Locations, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            System.Diagnostics.Debug.WriteLine(exception.Message);
        }
    }

    public void Remember(SearchOptions options)
    {
        var namesChanged = Remember(FileNames, options.FileName, StringComparer.Ordinal);
        var contentsChanged = Remember(Contents, options.Content, StringComparer.Ordinal);
        var locationsChanged = Remember(Locations, options.Locations, StringComparer.OrdinalIgnoreCase);
        if (!namesChanged && !contentsChanged && !locationsChanged) return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            var temporaryPath = historyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new Entries(
                    FileNames.ToArray(), Contents.ToArray(), Locations.ToArray())));
                File.Move(temporaryPath, historyPath, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(exception.Message);
        }
    }

    private static void Restore(ObservableCollection<string> target, string[]? values, StringComparer comparer)
    {
        foreach (var value in (values ?? []).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(comparer).Take(Limit))
            target.Add(value);
    }

    private static bool Remember(ObservableCollection<string> history, string value, StringComparer comparer)
    {
        if (string.IsNullOrWhiteSpace(value) || (history.Count > 0 && history[0] == value)) return false;
        var existing = history.FirstOrDefault(item => comparer.Equals(item, value));
        if (existing == value)
        {
            history.Move(history.IndexOf(existing), 0);
            return true;
        }
        if (existing is not null) history.Remove(existing);
        history.Insert(0, value);
        if (history.Count > Limit) history.RemoveAt(Limit);
        return true;
    }

    private sealed record Entries(string[] FileNames, string[] Contents, string[] Locations);
}
