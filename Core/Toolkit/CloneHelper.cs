using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace UI.Helper
{
    public static class CloneHelper
    {
        /// <summary>
        /// 【浅拷贝】(Shallow Copy)
        /// 创建一个新对象，复制所有值类型字段，但引用类型（如内部的 List、对象）仍然指向原地址。
        /// 极速、低内存消耗，非常适合用来欺骗 WPF 的绑定引擎触发刷新！
        /// </summary>
        public static T ShallowCopy<T>(T obj) where T : class
        {
            if (obj == null) return null;

            // 类型守卫：反射 MemberwiseClone 只能作用于普通堆对象。
            // 字符串字面量（.NET 9 冻结只读段）、基元装箱值、数组属于特殊运行时对象，
            // MemberwiseClone 触碰它们会引发 AccessViolationException（实测启动崩溃）
            if (obj is string || obj.GetType().IsPrimitive) return obj; // 不可变，直接引用即快照
            if (obj is Array arr) return (T)arr.Clone();                // 数组走标准克隆

            // 绕过访问限制，直接调用 .NET 底层 C++ 实现的高性能 MemberwiseClone
            MethodInfo cloneMethod = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);

            return (T)cloneMethod?.Invoke(obj, null);
        }

        /// <summary>
        /// 【深拷贝】(Deep Copy)
        /// 彻底创建一个全新的对象，包含它内部嵌套的所有引用类型也会被全部重新实例化。
        /// 适合用在：图纸算子复制、流程完全备份等场景。
        /// </summary>
        public static T DeepCopy<T>(T obj) where T : class
        {
            if (obj == null) return null;

            // Newtonsoft.Json 极速深序列化克隆（TypeNameHandling.Auto 保多态、Binder 白名单限类型）
            // 注意：被深拷贝的类及其内部类不能有循环引用
            string json = JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                NullValueHandling = NullValueHandling.Ignore
            });
            return JsonConvert.DeserializeObject<T>(json, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            });
        }
    }
}
