using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 图元注册表：<see cref="ScadaElement.TypeKey"/> → <see cref="ElementDescriptor"/>。
    ///
    /// 它是"字符串类型键"与"真实控件类型"之间唯一的桥。有了它，.vms 里只需存
    /// <c>"TypeKey": "Hmi.Rectangle"</c>，序列化层就不必认识任何图元类型
    /// （派生类方案会往文件里写 <c>$type</c>，正好踩在序列化白名单的校验路径上）。
    ///
    /// 线程安全用 <see cref="ConcurrentDictionary{TKey,TValue}"/>：注册发生在程序启动
    /// （模块初始化，UI 线程），读取发生在渲染/绑定/属性面板（可能不止一个线程），
    /// 两边不需要谁去加锁。键比较用 <see cref="StringComparer.OrdinalIgnoreCase"/>——
    /// 手写的 .vms 若把 "basic.rectangle" 写错大小写仍能打开，这是白送的容错。
    ///
    /// 注册失败<b>不抛异常</b>而是返回可读原因：注册表在程序启动时被调用，
    /// 抛异常会把整个程序的启动路径变成"改错一个默认值就打不开"；而把原因返回给调用方，
    /// 由断言工程（ScadaChecks）在构建期一次性全部挡掉，两边都不吃亏。
    /// </summary>
    public static class ElementRegistry
    {
        private static readonly ConcurrentDictionary<string, ElementDescriptor> Descriptors =
            new(StringComparer.OrdinalIgnoreCase);

        static ElementRegistry()
        {
            // 内置图元在类型初始化时自动挂上：宿主不必记得"先注册再使用"。
            // 失败项不抛异常，收集在 BuiltInErrors 里交给断言工程（见 Register 注释）。
            BuiltInErrors = RegisterBuiltIns();
        }

        /// <summary>
        /// 内置图元注册失败的原因（正常为空）。由静态构造填充，断言工程据此在构建期挡掉
        /// "改了描述符却没生效"这类静默失败——运行期没人会去 Register 的返回值上打日志。
        /// </summary>
        public static IReadOnlyList<string> BuiltInErrors { get; }

        /// <summary>
        /// 注册 <see cref="BuiltInElements.All"/> 里的全部图元，返回失败原因（空表示全成功）。
        ///
        /// 刻意不公开：它<b>不</b>幂等（重复注册会以"已注册"失败），只该在静态构造里跑一次。
        /// 外部要扩展图元库请直接调 <see cref="Register"/>。
        /// </summary>
        private static IReadOnlyList<string> RegisterBuiltIns()
        {
            var errors = new List<string>();

            foreach (var descriptor in BuiltInElements.All)
            {
                var error = Register(descriptor);
                if (error != null)
                    errors.Add(error);
            }

            return errors;
        }

        /// <summary>
        /// 注册一类图元。返回 null 表示成功，否则是失败原因（重复注册、描述符自检不过）。
        /// </summary>
        public static string? Register(ElementDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            var error = descriptor.Validate();
            if (error != null)
                return error;

            if (!Descriptors.TryAdd(descriptor.TypeKey, descriptor))
                return $"图元类型键 {descriptor.TypeKey} 已注册（重复注册会让工具箱出现两个同名图元）";

            return null;
        }

        /// <summary>是否已注册该类型键</summary>
        public static bool IsRegistered(string? typeKey)
            => !string.IsNullOrWhiteSpace(typeKey) && Descriptors.ContainsKey(typeKey!);

        /// <summary>按类型键找描述符；未注册返回 null（调用方决定"当成未知图元"还是报错）</summary>
        public static ElementDescriptor? Find(string? typeKey)
            => string.IsNullOrWhiteSpace(typeKey) ? null
             : Descriptors.TryGetValue(typeKey!, out var descriptor) ? descriptor : null;

        /// <summary>
        /// 全部已注册图元，按"分组 → 显示名"排序。
        ///
        /// 排序而不是保持注册顺序：字典本就无序，且工具箱要的是稳定可预期的排列
        /// （分组内按名字，找东西靠眼睛扫，不靠记注册次序）。
        /// </summary>
        public static IReadOnlyList<ElementDescriptor> All
            => Descriptors.Values
                .OrderBy(d => d.Category, StringComparer.CurrentCulture)
                .ThenBy(d => d.DisplayName, StringComparer.CurrentCulture)
                .ToArray();

        /// <summary>全量复检（断言工程在启动时调一次，把"注册了但描述符不自洽"的项全部列出来）</summary>
        public static IReadOnlyList<string> ValidateAll()
        {
            var errors = new List<string>();

            foreach (var descriptor in All)
            {
                var error = descriptor.Validate();
                if (error != null)
                    errors.Add(error);
            }

            return errors;
        }

        /// <summary>按类型键造一个新图元模型；类型未注册时抛异常（把笔误当场暴露，而不是造出一个空白图元）</summary>
        public static ScadaElement CreateElement(string typeKey, double x = 0, double y = 0)
            => Require(typeKey).CreateElement(x, y);

        /// <summary>
        /// 按图元的类型键造控件并绑好模型。
        ///
        /// 只做"造 + 绑"：位置/尺寸等落地由控件的 Element 变更回调完成（见 ScadaElementBase），
        /// 这样直接 new 出来的控件与注册表造的控件行为完全一致，没有第二条初始化路径。
        /// </summary>
        public static ScadaElementBase CreateControl(ScadaElement element)
        {
            ArgumentNullException.ThrowIfNull(element);

            var control = Require(element.TypeKey).CreateControl();
            control.Element = element;
            return control;
        }

        /// <summary>按"类型键 + 属性键"找属性描述符（属性面板与绑定引擎按它取默认值/编辑器类型）</summary>
        public static ElementPropertyDescriptor? FindProperty(string typeKey, string? propertyKey)
        {
            if (string.IsNullOrEmpty(propertyKey))
                return null;

            var properties = Find(typeKey)?.Properties;
            if (properties is null)
                return null;

            for (int i = 0; i < properties.Count; i++)
            {
                if (string.Equals(properties[i].Key, propertyKey, StringComparison.Ordinal))
                    return properties[i];
            }

            return null;
        }

        private static ElementDescriptor Require(string typeKey)
            => Find(typeKey) ?? throw new InvalidOperationException(
                $"图元类型 {typeKey} 未注册（已注册：{string.Join("、", Descriptors.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))}）");
    }
}
