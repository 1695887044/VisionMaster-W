using System.Globalization;

namespace G.ValueConverter;

public class GetRectToIntStringConverter : MarkupValueConverterBase
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Rect rect)
        {
            if (rect.IsEmpty)
                return string.Empty;
            // 四舍五入转为整数
            int x = (int)Math.Round(rect.X);
            int y = (int)Math.Round(rect.Y);
            int width = (int)Math.Round(rect.Width);
            int height = (int)Math.Round(rect.Height);

            return $"{x},{y},{width},{height}";
        }
        return string.Empty;
    }

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
   
        if (value is string strValue)
        {
            if (string.IsNullOrEmpty(strValue))
                return Rect.Empty;
            string[] parts = strValue.Split(',');
            if (parts.Length == 4)
            {
                if (int.TryParse(parts[0], out int x) &&
                    int.TryParse(parts[1], out int y) &&
                    int.TryParse(parts[2], out int width) &&
                    int.TryParse(parts[3], out int height))
                {
                    return new Rect(x, y, width, height);
                }
            }
        }
        return DependencyProperty.UnsetValue; // 转换失败时返回默认值
    }
}

