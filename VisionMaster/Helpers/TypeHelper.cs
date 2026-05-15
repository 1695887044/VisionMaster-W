﻿﻿﻿﻿﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// 类型帮助工具类
    /// 提供类型兼容性检查、表达式安全类型判断等功能
    /// </summary>
    public static class TypeHelper
    {
        /// <summary>
        /// 判断类型是否可安全用于表达式计算
        /// 只允许字符串和值类型，防止注入攻击
        /// </summary>
        public static bool IsSafeExpressionType(Type type)
        {
            if (type == null) return false;

            if (type == typeof(string)) return true;

            if (type.IsValueType) return true;

            return false;
        }

        /// <summary>
        /// 判断类型是否兼容（用于端口绑定验证）
        /// </summary>
        public static bool IsTypeCompatible(Type source, Type target)
        {
            if (target.IsAssignableFrom(source))
                return true;

            if (target == typeof(object))
                return true;

            if (target == typeof(string))
                return true;

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

        /// <summary>
        /// 从类型名字符串获取实际类型
        /// 使用硬编码匹配提升性能
        /// </summary>
        public static Type GetActualTypeFromLink(string typeName)
        {
            if (typeName == null) return typeof(double);

            if (string.IsNullOrWhiteSpace(typeName))
                return typeof(double);

            switch (typeName.ToLower())
            {
                case "system.double":
                case "double":
                    return typeof(double);

                case "system.single":
                case "float":
                    return typeof(float);

                case "system.int32":
                case "int":
                    return typeof(int);

                case "system.boolean":
                case "bool":
                    return typeof(bool);

                case "system.string":
                case "string":
                    return typeof(string);

                default:
                    try
                    {
                        return Type.GetType(typeName, throwOnError: false, ignoreCase: true) ?? typeof(object);
                    }
                    catch
                    {
                        return typeof(object);
                    }
            }
        }
    }
}
