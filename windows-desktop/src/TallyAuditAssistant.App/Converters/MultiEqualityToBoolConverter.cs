using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TallyAuditAssistant.App.Converters;

public class MultiEqualityToBoolConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values == null || values.Length < 2)
            return false;

        var val1 = values[0];
        var val2 = values[1];

        if (val1 == null || val2 == null || val1 == DependencyProperty.UnsetValue || val2 == DependencyProperty.UnsetValue)
            return false;

        return val1.ToString() == val2.ToString();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
