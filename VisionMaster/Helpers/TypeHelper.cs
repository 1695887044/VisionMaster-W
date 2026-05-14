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
        public static bool IsTypeCompatible(Type source, Type target)
        {
            // 1. 完全相同，或者存在继承关系 (比如子类可以赋给父类)
            if (target.IsAssignableFrom(source))
                return true;

            // 2. 目标如果是 object，来者不拒
            if (target == typeof(object))
                return true;

            // 3. 目标如果是 string，任何类型都能通过 .ToString() 转成文本
            if (target == typeof(string))
                return true;

            // 4. 🌟 智能数值兼容：允许 int 绑定到 double (因为我们底层 OutputPort 加了 Convert.ChangeType)
            if (IsNumericType(source) && IsNumericType(target))
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否为基础数值类型
        /// </summary>
        public static bool IsNumericType(Type type)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }
        public static Type GetActualTypeFromLink(string typeName)
        {
            if (typeName == null) return typeof(double); // 断线兜底

            // 假设它拿到的是 "System.Double", "System.String" 这样的字符串


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
