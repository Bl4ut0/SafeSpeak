using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SafeSpeak.App.Converters;

public sealed class ArmedToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? "TTS armed" : "TTS disarmed";
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
