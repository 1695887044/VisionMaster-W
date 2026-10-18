using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 收集一个图元控件类（含中间基类）由 <c>[ScadaProperty]</c> 生成的属性描述符。
    ///
    /// 为什么需要"沿继承链收集"
    /// ---------
    /// 图元库里有中间基类：<see cref="StateVisualElement"/> 声明了泵/电机/管道共用的六个属性
    /// （状态 + 四色 + 派生画刷）。生成器把描述符写在<b>声明它的那个类</b>上，
    /// 所以子类要拿到的必须是"自己 + 全部基类"的并集——少了基类那一份，
    /// 泵就会丢掉"状态"这个属性，属性面板上少一行、绑定引擎也找不到它。
    ///
    /// 为什么要走反射
    /// ---------
    /// 生成的方法是 <c>internal static</c>，而"哪个类有这个方法"只有编译期知道
    /// （没用 <c>[ScadaProperty]</c> 的类，生成器压根不产出这个文件）。
    /// 枚举一次继承链上的静态方法是最直接的答案，且<b>只在注册期跑一次</b>
    /// （<see cref="ElementRegistry"/> 的静态构造里，17 个图元各一次），
    /// 结果进缓存，运行期零反射。
    ///
    /// 顺序：<b>基类在前、子类在后</b>，各自保持源码次序。属性面板按"分组首次出现的次序"
    /// 排列（见 <c>ScadaPropertyViewModel.Rebuild</c>），所以这条次序决定了面板上谁在上面，
    /// 与原先手写描述符时的排版口径一致。
    ///
    /// <c>public</c> 而不是 <c>internal</c>：图元库拆成了多个程序集（内置图元在 VM.Scada.Controls，
    /// 曲线图元在 VM.Scada.Controls.Charts），后者的清单要调本方法来收集属性，internal 跨不过去。
    /// </summary>
    public static class ScadaPropertyCatalog
    {
        /// <summary>生成器产出的收集方法名。改名要同时改 <c>ScadaPropertyGenerator.AppendCatalog</c>。</summary>
        private const string MethodName = "AddOwnGeneratedProperties";

        private static readonly ConcurrentDictionary<Type, IReadOnlyList<ElementPropertyDescriptor>> Cache = new();

        /// <summary>
        /// 取一个图元控件类应声明的全部 <c>[ScadaProperty]</c> 属性（基类在前）。
        ///
        /// 返回空表是正常状态：只用基类通用属性的图元（如纯文本、纯矩形）本来就没有私有属性。
        /// </summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Collect(Type controlType)
        {
            ArgumentNullException.ThrowIfNull(controlType);
            return Cache.GetOrAdd(controlType, static type => Build(type));
        }

        private static IReadOnlyList<ElementPropertyDescriptor> Build(Type controlType)
        {
            // 从 object 往下收集再反转 → 基类在前、子类在后，各类内部保持源码次序。
            var chain = new List<Type>();
            for (var type = controlType; type is not null && type != typeof(object); type = type.BaseType)
                chain.Add(type);
            chain.Reverse();

            var result = new List<ElementPropertyDescriptor>();
            foreach (var type in chain)
            {
                var method = type.GetMethod(
                    MethodName,
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly,
                    binder: null,
                    types: new[] { typeof(List<ElementPropertyDescriptor>) },
                    modifiers: null);

                method?.Invoke(null, new object[] { result });
            }

            return result.Count == 0 ? Array.Empty<ElementPropertyDescriptor>() : result.ToArray();
        }
    }
}
