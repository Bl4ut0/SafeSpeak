using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SafeSpeak.App.Converters;

public sealed class ArmedToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "_Disarm text to speech" : "_Arm text to speech";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public sealed class SpeakingToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "Speaking" : "Idle";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public sealed class StepToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int currentStep && parameter is string targetStepStr && int.TryParse(targetStepStr, out int targetStep))
        {
            return currentStep == targetStep ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b ? !b : false;
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public sealed class DispositionToBackgroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string disposition = value?.ToString() ?? "";
        return disposition.Equals("Approved", StringComparison.OrdinalIgnoreCase)
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 230, 244, 234)) // Soft Green
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 253, 237, 237)); // Soft Red
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public sealed class DispositionToForegroundBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string disposition = value?.ToString() ?? "";
        return disposition.Equals("Approved", StringComparison.OrdinalIgnoreCase)
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 19, 115, 51)) // Forest Green
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 197, 34, 31)); // Crimson Red
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
