using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TallyAuditAssistant.App.Converters;

public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = 0;
        if (value is int i)
        {
            count = i;
        }

        bool isInverse = parameter is string p && p.Equals("Inverse", StringComparison.OrdinalIgnoreCase);

        if (isInverse)
        {
            return count > 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        return count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
