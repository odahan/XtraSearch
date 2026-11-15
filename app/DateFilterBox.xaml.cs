using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace XtraSearch;

/// <summary>Combines an editable ISO date with an optional calendar selection.</summary>
public partial class DateFilterBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DateFilterBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private bool syncingCalendar;

    public DateFilterBox() => InitializeComponent();

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void CalendarOpened(object sender, EventArgs e)
    {
        syncingCalendar = true;
        Picker.DisplayMode = CalendarMode.Month;
        Picker.SelectedDate = DateTime.TryParseExact(Text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date) ? date : null;
        Picker.DisplayDate = Picker.SelectedDate ?? DateTime.Today;
        syncingCalendar = false;
        Picker.Focus();
    }

    private void CalendarDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!syncingCalendar && Picker.SelectedDate is DateTime date)
            SetCurrentValue(TextProperty, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private void CalendarMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is FrameworkElement element &&
            (element is CalendarDayButton || element.TemplatedParent is CalendarDayButton))
            Dispatcher.BeginInvoke(() => CloseCalendar());
    }

    private void CalendarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.Enter && Picker.DisplayMode == CalendarMode.Month))
        {
            CloseCalendar();
            e.Handled = true;
        }
    }

    private void CloseCalendar()
    {
        CalendarPopup.SetCurrentValue(Popup.IsOpenProperty, false);
        CalendarButton.Focus();
    }
}
