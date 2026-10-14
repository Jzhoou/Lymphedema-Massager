using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace Upcomputer.UI.Controls
{
    public class ValueToAngleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double)
            {
                double val = (double)value;
                // 假设值范围是0-100，映射到135-405度
                double angle = 135 + (val / 100.0) * (405 - 135);
                return Math.Min(405, Math.Max(135, angle));
            }
            return 135;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
