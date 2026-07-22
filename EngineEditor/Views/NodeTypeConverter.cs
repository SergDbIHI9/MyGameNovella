using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EngineEditor.Converters
{
    // Возвращает true, если NodeType (value) совпадает со строкой-параметром
    public class NodeTypeEqualsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is string s && parameter is string p && string.Equals(s, p, StringComparison.Ordinal);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    // Возвращает true, если NodeType (value) НЕ совпадает со строкой-параметром
    public class NodeTypeNotEqualsConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return !(value is string s && parameter is string p && string.Equals(s, p, StringComparison.Ordinal));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}