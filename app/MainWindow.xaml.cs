using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace XtraSearch;

/// <summary>Hosts the WPF view and captures it without activating the window.</summary>
public partial class MainWindow : Window
{
    private readonly string? screenshot;
    private int frames;
    private double searchThreadsHeight = 160;
    private readonly MainViewModel viewModel = new();

    public MainWindow(string? screenshot = null)
    {
        this.screenshot = screenshot;
        InitializeComponent();
        DataContext = viewModel;
        if (screenshot != null)
        {
            ShowActivated = false;
            ShowInTaskbar = false;
            SourceInitialized += (_, _) =>
            {
                var handle = new WindowInteropHelper(this).Handle;
                var style = GetWindowLongPtr(handle, -20).ToInt64();
                SetWindowLongPtr(handle, -20, new IntPtr(style | 0x08000000));
                SetWindowPos(handle, new IntPtr(1), 0, 0, 0, 0, 0x0013);
            };
            Loaded += (_, _) => StartCapture();
        }
        else SourceInitialized += (_, _) =>
        {
            var enabled = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref enabled, sizeof(int));
        };
        Closed += (_, _) => viewModel.Shutdown();
    }

    private void StartCapture()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            InvalidateVisual();
            if (++frames < 120) return;
            timer.Stop();
            UpdateLayout();
            var content = (FrameworkElement)Content;
            var width = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
            var height = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var path = Path.GetFullPath(screenshot!);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(path)) encoder.Save(stream);
            Close();
        };
        timer.Start();
    }

    private void ResultsDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (viewModel.OpenFileCommand.CanExecute(null)) viewModel.OpenFileCommand.Execute(null);
    }

    private void SearchThreadsToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { IsChecked: true })
        {
            var parent = (System.Windows.Controls.Grid)SearchThreadsRow.Parent;
            var available = parent.ActualHeight - parent.RowDefinitions[0].ActualHeight - 6;
            var paneHeight = Math.Clamp(searchThreadsHeight, 64, Math.Max(64, available - 64));
            SearchThreadsRow.MinHeight = 64;
            parent.RowDefinitions[1].Height = new GridLength(Math.Max(64, available - paneHeight), GridUnitType.Star);
            SearchThreadsRow.Height = new GridLength(paneHeight, GridUnitType.Star);
        }
        else
        {
            if (SearchThreadsRow.ActualHeight > 0) searchThreadsHeight = SearchThreadsRow.ActualHeight;
            SearchThreadsRow.MinHeight = 0;
            SearchThreadsRow.Height = new GridLength(0);
        }
    }

    private void ResultsRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(ResultsGrid, source) is not DataGridRow { Item: SearchResult result } row)
            return;

        ResultsGrid.SelectedItem = result;
        row.Focus();
        e.Handled = true;
        ShowResultMenu(result, row.PointToScreen(e.GetPosition(row)));
    }

    private void ResultsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && viewModel.OpenFileCommand.CanExecute(null))
        {
            viewModel.OpenFileCommand.Execute(null);
            e.Handled = true;
        }
        else if ((e.Key == Key.Apps || (e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) &&
                 ResultsGrid.SelectedItem is SearchResult result)
        {
            var row = ResultsGrid.ItemContainerGenerator.ContainerFromItem(result) as DataGridRow;
            var anchor = row == null ? ResultsGrid.PointToScreen(new Point(0, 0)) : row.PointToScreen(new Point(0, row.ActualHeight));
            e.Handled = true;
            ShowResultMenu(result, anchor);
        }
    }

    private void ShowResultMenu(SearchResult result, Point screenPosition)
    {
        try
        {
            ShellContextMenu.Show(this, result.PhysicalPath, screenPosition, Keyboard.Modifiers);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException or Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, $"Unable to show the context menu for:\n{result.PhysicalPath}\n\n{exception.Message}",
                "XtraSearch", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
