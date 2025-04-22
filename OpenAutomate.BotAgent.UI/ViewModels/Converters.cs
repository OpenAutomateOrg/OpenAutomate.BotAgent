using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenAutomate.BotAgent.UI.ViewModels
{
    /// <summary>
    /// Converts boolean values to Visibility values (true = Visible, false = Collapsed)
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool boolean && boolean) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }

    /// <summary>
    /// Converts boolean values to inverted Visibility values (true = Collapsed, false = Visible)
    /// </summary>
    public class BooleanToInvertedVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is bool boolean && boolean) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility != Visibility.Visible;
        }
    }

    /// <summary>
    /// Converts boolean values to inverted boolean values
    /// </summary>
    public class BooleanToInvertedBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool boolean && boolean);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !(value is bool boolean && boolean);
        }
    }
} 