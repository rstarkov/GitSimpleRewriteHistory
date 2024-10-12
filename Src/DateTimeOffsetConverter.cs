using System.Globalization;
using System.Windows.Data;

namespace GitSimpleRewriteHistory;

class DateTimeOffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        if (value.GetType() != typeof(DateTimeOffset))
            return null;
        if (targetType != typeof(string))
            return null;
        return $"{value:dd/MM/yyyy HH:mm:ss zzzz}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        if (value.GetType() != typeof(string))
            return null;
        if (targetType != typeof(DateTimeOffset))
            return null;
        if (DateTimeOffset.TryParseExact((string)value, "dd/MM/yyyy HH:mm:ss zzzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            return result;
        return null;
    }
}
