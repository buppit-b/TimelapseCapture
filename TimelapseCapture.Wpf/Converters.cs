using System;
using System.Globalization;
using System.Windows.Data;

namespace TimelapseCapture.Wpf
{
    /// <summary>
    /// True when the bound value equals the ConverterParameter (case-insensitive string compare).
    /// ConvertBack returns the parameter when the control becomes checked, otherwise Binding.DoNothing.
    /// This is the segmented-RadioButton pattern: bind each option's IsChecked with its value as the
    /// ConverterParameter, and the bound string property reflects the selected option.
    /// </summary>
    public sealed class StringEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (!(value is bool b && b) || parameter == null) return Binding.DoNothing;
            // Coerce to the target property's type so this works for both string and int properties.
            if (targetType == typeof(int) && int.TryParse(parameter.ToString(), out var i)) return i;
            return parameter;
        }
    }
}
