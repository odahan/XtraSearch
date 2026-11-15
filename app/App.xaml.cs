using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace XtraSearch;

/// <summary>Starts the desktop application and its deterministic verification mode.</summary>
public partial class App : Application
{
    public static RoutedCommand PasteFromMenu { get; } = new(nameof(PasteFromMenu), typeof(App));

    private void CanPasteFromMenu(object sender, CanExecuteRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not ContextMenu { PlacementTarget: TextBoxBase editor }) return;

        try { e.CanExecute = ApplicationCommands.Paste.CanExecute(null, editor); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ExternalException)
        {
            e.CanExecute = false;
        }
    }

    private void ExecutePasteFromMenu(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not ContextMenu { PlacementTarget: TextBoxBase editor }) return;

        try { ApplicationCommands.Paste.Execute(null, editor); }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ExternalException)
        {
            System.Diagnostics.Debug.WriteLine(exception.Message);
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test"))
        {
            try { await SearchVerification.RunAsync(); Shutdown(0); }
            catch (Exception exception) { File.WriteAllText("verification-failure.txt", exception.ToString()); Shutdown(1); }
            return;
        }

        var screenshotIndex = Array.IndexOf(e.Args, "--screenshot");
        string? screenshot = screenshotIndex >= 0 && screenshotIndex + 1 < e.Args.Length ? e.Args[screenshotIndex + 1] : null;
        var window = new MainWindow(screenshot);
        MainWindow = window;
        window.Show();
    }
}
