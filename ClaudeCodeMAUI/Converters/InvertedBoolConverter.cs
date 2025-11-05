using System.Globalization;

namespace ClaudeCodeMAUI.Converters
{
    /// <summary>
    /// Converter per invertire un valore booleano.
    /// Utilizzato nei binding XAML per mostrare/nascondere elementi in base al valore opposto di una proprietà.
    /// Esempio: IsVisible="{Binding HasName, Converter={StaticResource InvertedBoolConverter}}"
    /// </summary>
    public class InvertedBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return false;
        }
    }
}
