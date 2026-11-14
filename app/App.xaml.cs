using System.IO;
using System.Windows;

namespace XtraSearch;

/// <summary>Starts the desktop application and its deterministic verification mode.</summary>
public partial class App : Application
{
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
