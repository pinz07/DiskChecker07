using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DiskChecker.Converters
{
    /// <summary>
    /// Конвертер размера байт в человеко-читаемую строку.
    /// </summary>
    public class SizeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long size)
            {
                if (size >= 1_073_741_824L) return $"{size / (1024.0 * 1024.0 * 1024.0):F2} ГБ";
                if (size >= 1_048_576L) return $"{size / (1024.0 * 1024.0):F2} МБ";
                if (size >= 1024L) return $"{size / 1024.0:F2} КБ";
                return $"{size} Б";
            }
            return "0 Б";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер bool → Visibility.
    /// </summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Инверсный конвертер bool → Visibility (true = Collapsed).
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер double МБ → long Байт для отображения текста ползунка.
    /// </summary>
    public class MbToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double mb)
            {
                if (mb >= 1024) return $"{mb / 1024.0:F1} ГБ";
                return $"{mb:F0} МБ";
            }
            return "0 МБ";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Инверсный конвертер bool → bool (true → false и наоборот).
    /// Используется для IsEnabled привязок.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b) return !b;
            return true;
        }
    }
    
    // Directory = folder (reddish), File = green
    public class BoolToLightRedGreenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isDir && isDir)
                return new SolidColorBrush(Color.FromArgb(51, 232, 89, 89)); // 20% #E85959
            return new SolidColorBrush(Color.FromArgb(51, 192, 238, 90)); // 20% #C0EE5A
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    
    public class BoolToDarkRedGreenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isDir && isDir)
                return new SolidColorBrush(Color.FromRgb(232, 89, 89)); 
            return new SolidColorBrush(Color.FromRgb(140, 178, 62)); // Darker lime for text
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер для размера шрифта: item.ShowLabel ? fontSize : 0
    /// </summary>
    public class LabelVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 && values[0] is double width && values[1] is double height)
            {
                return (width > 60 && height > 24) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Конвертер для привязки RadioButton.IsChecked к значению перечисления.
    /// ConverterParameter — строковое имя значения enum.
    /// </summary>
    public class EnumToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string param && value != null)
                return value.ToString() == param;
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value — это bool (IsChecked). targetType здесь — тип enum-свойства в VM.
            // Если RadioButton не нажат — не обновляем.
            if (value is not true) return Binding.DoNothing;
            if (parameter is not string param) return Binding.DoNothing;

            try
            {
                // targetType может быть Nullable<T> — достанем underlying тип
                var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
                if (enumType.IsEnum)
                    return Enum.Parse(enumType, param);
            }
            catch { /* ignored */ }

            return Binding.DoNothing;
        }
    }
}
