﻿﻿﻿﻿﻿﻿﻿using System;
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
        /// 判断一个「上游输出端口」能不能绑到「下游输入端口」——端口绑定的**唯一判据**。
        ///
        /// 为什么单独抽一个方法而不是各处各写一遍 IsTypeCompatible：
        /// ① 数组输出 → 标量端口这条"取元素"路径（带索引绑定）是合法的，
        ///    只按 IsTypeCompatible(double[], double) 判会把 double[]→double 这种合法绑定误杀；
        /// ② 绑定弹窗的"列表过滤"与"双击时校验"必须是同一把尺子，
        ///    否则 UI 放过去的、校验又拦下来（或反过来），用户会看到自相矛盾的行为。
        /// </summary>
        /// <param name="source">上游输出端口类型</param>
        /// <param name="target">下游输入端口类型（期望类型）</param>
        public static bool CanBindTo(Type source, Type target)
        {
            if (source == null || target == null) return false;

            // ★ object 一律放行（双向）。
            //
            // 这一条是实测出来的，不是理论考虑：**全局变量的默认类型就是 object**
            // （新建变量时不指定类型），而绑定弹窗会按"当前选中的输入端口类型"过滤候选。
            // 用户选中一个 Double 输入端口后，所有全局变量都因
            // IsTypeCompatible(object, double) == false 被滤掉 ——
            // 界面表现是"点了一个上游节点，右边的端口列表空着"，反馈原话是
            // "点击返回原有内容无响应"。
            //
            // 为什么静态这层不该拦：object 的含义是"**运行期**才知道真实类型"（装箱值）。
            // 真实类型由流程运行时校验，静态这里拦下来只会把
            // "类型待定"的合法用法全部禁掉，而拦不住真正的错配（真错了运行期会报）。
            // 两个方向都放行：object → 任意（值可能装得下），任意 → object（装箱永远成立）。
            if (source == typeof(object) || target == typeof(object)) return true;

            // 数组 → 标量：允许"按下标取一个元素"（绑定时会弹索引输入框）
            if (source.IsArray && !target.IsArray)
            {
                var element = source.GetElementType();
                return element != null && IsTypeCompatible(element, target);
            }

            return IsTypeCompatible(source, target);
        }

        /// <summary>
        /// 数组输出取元素时的元素类型（供"索引绑定"使用）；非数组返回自身
        /// </summary>
        public static Type GetBindableElementType(Type source)
            => source != null && source.IsArray ? (source.GetElementType() ?? source) : source;

        /// <summary>
        /// 把端口定义里的 DataTypeName 解析成 Type —— 解析不出时返回 typeof(object)。
        ///
        /// 为什么不能用 GetActualTypeFromLink：那个方法对**未知**类型名返回 typeof(double)，
        /// 于是"HRegion 端口撞上 double 期望"会被判成兼容（数值互转），把不能绑的放过去。
        /// 绑定的判据方向必须相反：认不出来 = 什么都接得住（object），
        /// 最坏结果是"该过滤的没过滤掉"（用户还能自己看），而不是"能绑的被藏起来"。
        ///
        /// 全名优先，再退到"在所有已加载程序集里按名找"：
        /// 仓库里的端口声明走 AssemblyQualifiedName（PluginService / FlowQueryHelper / 各插件
        /// DynamicPortInfo 都是这么写的），Type.GetType 直接命中；
        /// 但插件自己手写的固定端口偶尔只写短名（"HalconDotNet.HImage"），
        /// 那种情况下 Type.GetType 返回 null，只能靠逐程序集查找兜住。
        /// </summary>
        public static Type ResolveType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return typeof(object);

            var resolved = Type.GetType(typeName, throwOnError: false, ignoreCase: true);
            if (resolved != null) return resolved;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    resolved = assembly.GetType(typeName, throwOnError: false, ignoreCase: true);
                    if (resolved != null) return resolved;
                }
                catch
                {
                    // 动态程序集 / 反射被拒：跳过，继续找下一个
                }
            }

            return typeof(object);
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
