using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TallyAuditAssistant.App.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool isNull = value == null || value == DependencyProperty.UnsetValue;
        bool isInverse = parameter is string p && p.Equals("Inverse", StringComparison.OrdinalIgnoreCase);

        if (isInverse)
        {
            return isNull ? Visibility.Visible : Visibility.Collapsed;
        }

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
