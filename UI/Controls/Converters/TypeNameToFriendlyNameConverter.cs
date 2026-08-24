using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI.Converters
{
    public class TypeNameToFriendlyNameConverter : BaseMarkupConverter
    {
        public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Type type)
            {
                return GetFriendlyTypeName(type);
            }

            // 如果你 XAML 里依然绑的是 DataTypeName 字符串，尝试把它还原成 Type
            if (value is string typeName)
            {
                Type parsedType = Type.GetType(typeName);
                if (parsedType != null)
                {
                    return GetFriendlyTypeName(parsedType);
                }
            }

            return value?.ToString() ?? "未知类型";
        }
        private string GetFriendlyTypeName(Type type)
        {
            // 1. 核心判断：它是不是一个集合或数组？(排除 string 本身)
            bool isCollection = false;
            Type elementType = type;

            if (type.IsArray)
            {
                isCollection = true;
                elementType = type.GetElementType(); // 拿到基础类型，比如 int[] 拿到 int
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                isCollection = true;
                elementType = type.GetGenericArguments()[0]; // 拿到 List<T> 里面的 T
            }

            // 2. 基础类型美化映射
            string shortName = elementType.Name switch
            {
                "Int32" => "整数 (Int)",
                "Double" => "小数 (Double)",
                "Single" => "浮点数 (Float)",
                "String" => "文本 (String)",
                "Boolean" => "布尔 (Bool)",
                "Mat" => "图像 (Image)",
                "Point2D" => "二维点 (Point)",
                _ => elementType.Name // 兜底：如果是不认识的类，就显示原名
            };

            // 3. 🌟 灵魂拼接：如果是集合，加上后缀
            if (isCollection)
            {
                // 可以根据你们公司的工业软件习惯叫 "xxx数组" 或 "xxx集合"
                return $"{shortName} 数组 [ ]";
            }

            return shortName;
        }
    }
}
