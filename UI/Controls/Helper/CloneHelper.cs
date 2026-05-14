using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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

            // 使用现代 .NET 自带的 System.Text.Json 进行极速深序列化克隆
            // 注意：被深拷贝的类及其内部类不能有循环引用
            string json = JsonSerializer.Serialize(obj);
            return JsonSerializer.Deserialize<T>(json);
        }
    }
}
