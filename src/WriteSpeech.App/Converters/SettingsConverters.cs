using System.Globalization;
using System.Windows;
using System.Windows.Data;
using WriteSpeech.Core.Models;

namespace WriteSpeech.App.Converters;

/// <summary>Converts a boolean to Visibility: true yields Collapsed, false yields Visible (inverse of default BoolToVisibility).</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a boolean to "Enabled"/"Disabled" text, or "Listening for keys..."/"Rebind" when the parameter is "capturing".</summary>
public class BoolToEnabledDisabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string p && p == "capturing")
            return value is true ? "Listening for keys..." : "Rebind";
        return value is true ? "Enabled" : "Disabled";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts an integer number of seconds to a string showing whole minutes (integer division by 60).</summary>
public class SecondsToMinutesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int seconds ? (seconds / 60).ToString() : "0";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a TranscriptionProvider enum to Visibility: Visible if the provider matches the parameter string (supports pipe-separated values like "Local|Parakeet"), Collapsed otherwise.</summary>
public class ProviderToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TranscriptionProvider provider && parameter is string expected)
        {
            var providerStr = provider.ToString();
            // Support pipe-separated values, e.g. "Local|Parakeet"
            return expected.Split('|').Any(v => v == providerStr)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a string value to Visibility: Visible if the value equals the parameter string (exact match), Collapsed otherwise.</summary>
public class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var str = value?.ToString();
        if (str is not null && parameter is string expected)
            return str == expected ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a HotkeyCaptureTarget string to button text: "Listening for keys..." when the value matches the target parameter, "Rebind" otherwise.</summary>
public class CapturingHotkeyTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var capturing = value?.ToString();
        if (capturing is not null && parameter is string target)
            return capturing == target ? "Listening for keys..." : "Rebind";
        return "Rebind";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a string to Visibility: Visible when the string is null or empty, Collapsed when it has content.</summary>
public class EmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Converts a string to Visibility: Visible when the string has content, Collapsed when it is null or empty.</summary>
public class NonEmptyStringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Show Download button when !IsDownloaded and !IsDownloading.</summary>
public class ModelActionVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is bool isDownloaded && values[1] is bool isDownloading)
            return !isDownloaded && !isDownloading ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Show Delete button when IsDownloaded and !IsActive and !IsDownloading.</summary>
public class ModelDeleteVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 3 && values[0] is bool isDownloaded && values[1] is bool isDownloading && values[2] is bool isActive)
            return isDownloaded && !isDownloading && !isActive ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Show Use button when IsDownloaded and !IsActive and !IsDownloading.</summary>
public class ModelUseVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 3 && values[0] is bool isDownloaded && values[1] is bool isDownloading && values[2] is bool isActive)
            return isDownloaded && !isDownloading && !isActive ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Returns true when two values are equal (via string comparison).</summary>
public class EqualityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length == 2 && string.Equals(values[0]?.ToString(), values[1]?.ToString(), StringComparison.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Shows mic level bar only for the selected microphone while testing.</summary>
public class MicLevelVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 3
            && values[0] is int deviceIndex
            && values[1] is int selectedIndex
            && values[2] is bool isTesting)
        {
            return deviceIndex == selectedIndex && isTesting
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
