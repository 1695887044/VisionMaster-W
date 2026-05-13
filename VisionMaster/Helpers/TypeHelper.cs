using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Helpers
{
    public static class TypeHelper
    {
        public static bool IsSafeExpressionType(Type type)
        {
            if (type == null) return false;

            // 1. 允许字符串
            if (type == typeof(string)) return true;

            // 2. 允许所有值类型 (数字、布尔、枚举、DateTime等)
            if (type.IsValueType) return true;

            // 拒绝其他所有复杂的 Class 引用类型
            return false;
        }

        public static Type ResolveTypeFromString(string typeStr)
        {
            if (string.IsNullOrWhiteSpace(typeStr)) return typeof(double);
            switch (typeStr.ToLower())
            {
                case "system.string": case "string": return typeof(string);
                case "system.boolean": case "bool": return typeof(bool);
                default: return typeof(double);
            }
        }
        public static Type GetActualTypeFromLink(dynamic link)
        {
            if (link == null) return typeof(double); // 断线兜底

            // 🌟 TODO: 将 DataTypeName 替换为你对象中真实存储类型名称的属性！
            // 假设它拿到的是 "System.Double", "System.String" 这样的字符串
            string typeName = link.DataTypeName?.ToString();

            if (string.IsNullOrWhiteSpace(typeName))
                return typeof(double);

            // 采用硬编码字典/Switch匹配，比反射 Type.GetType() 性能快几十倍！
            switch (typeName.ToLower())
            {
                // 1. 基础数值类型
                case "system.double":
                case "double":
                    return typeof(double);

                case "system.single":
                case "float":
                    return typeof(float);

                case "system.int32":
                case "int":
                    return typeof(int);

                // 2. 布尔类型
                case "system.boolean":
                case "bool":
                    return typeof(bool);

                // 3. 字符串类型
                case "system.string":
                case "string":
                    return typeof(string);

                // 4. 其他复杂类型 (返回真实 Type，交由 IsSafeExpressionType 负责拦截)
                default:
                    try
                    {
                        // 如果是标准库里的，或者带有完整命名空间的类，尝试反射获取
                        return Type.GetType(typeName, throwOnError: false, ignoreCase: true) ?? typeof(object);
                    }
                    catch
                    {
                        return typeof(object); // 解析失败直接作为 object 处理
                    }
            }
        }
    }
}
