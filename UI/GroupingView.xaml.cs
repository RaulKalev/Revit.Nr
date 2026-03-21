using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Renumber.UI
{
    /// <summary>
    /// Code-behind for GroupingView.xaml.
    /// DataContext is set externally by DuplicateSheetsWindow.
    /// </summary>
    public partial class GroupingView : System.Windows.Controls.UserControl
    {
        public GroupingView()
        {
            InitializeComponent();
        }
    }

    /// <summary>
    /// Converts a ratio (0..1+) and a parent width to a pixel width for progress bars.
    /// Clamps the result between 0 and the parent width (never exceeds 100%).
    /// Used via MultiBinding: values[0] = ratio, values[1] = container ActualWidth.
    /// </summary>
    public class RatioToWidthConverter : IMultiValueConverter
    {
        /// <summary>Singleton instance for use with x:Static in XAML.</summary>
        public static readonly RatioToWidthConverter Instance = new RatioToWidthConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return 0.0;

            // values[0] = ratio (double), values[1] = container width (double)
            if (!(values[0] is double ratio) || !(values[1] is double containerWidth))
                return 0.0;

            // Clamp ratio between 0 and 1 for visual display
            double clampedRatio = Math.Max(0.0, Math.Min(ratio, 1.0));
            return clampedRatio * containerWidth;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
