using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UI.Attributes;

namespace UI.CustomControl.PropertyGrid
{
    /// <summary>
    /// 属性网格的**共享默认值**：生成器清单、处理器管线、属性筛选与分组。
    ///
    /// 【为什么要有它】改造前 FlatPropertyGrid 与 CardPropertyGrid 各自 new 一遍
    /// 完全相同的 6 个生成器、4 个处理器 —— 新增一个生成器要改两处，
    /// 漏一处就变成"卡片式面板有这个控件、扁平面板没有"的诡异差异。
    /// 清单只有一份，"两个面板行为不一致"在结构上就不可能发生。
    /// </summary>
    public static class PropertyGridDefaults
    {
        /// <summary>没有标 GroupPath 的属性归到这一组</summary>
        public const string DefaultGroupName = "默认分组";

        /// <summary>卡片式布局的栅格列数（ColSpan 以它为满宽）</summary>
        public const int GridColumns = 12;

        /// <summary>
        /// 默认生成器。**顺序只表示注册顺序，命中顺序由 Priority 决定**（见 CreateControl）。
        /// </summary>
        public static List<IControlGenerator> CreateGenerators() => new()
        {
            new NestedPropertyGridGenerator(),
            new EnumGenerator(),
            // 动态候选下拉（轴名 / 卡名）：候选来自宿主注册的来源，随方案变化
            new OptionSourceGenerator(),
            // 数值（int/double/…）：优先于下面的 StructValueGenerator，用 NumericBox 换掉裸文本框
            new NumericGenerator(),
            new StructValueGenerator(),
            new BoolStateGenerator(),
            // 兜底：Priority 最低，只有上面都处理不了才轮到它
            new TypeGenerator(),
        };

        /// <summary>
        /// 默认处理器管线。<paramref name="cardStyle"/> 决定布局处理器走卡片式还是扁平式。
        /// </summary>
        public static List<IControlProcessor> CreateProcessors(SuperDisplayAttribute? display, bool cardStyle) => new()
        {
            new PropertyGridLayoutProcessor(display, cardStyle),
            new CommandProcessor(),
            new ValidationProcessor(),
            new PermissionProcessor(),
        };

        /// <summary>
        /// 取"应当显示"的属性：只认带 <see cref="SuperDisplayAttribute"/> 且 Visible == true 的。
        /// 没有该特性的属性一律不渲染 —— 这是属性网格与"随便一个对象"之间的契约边界。
        /// </summary>
        public static List<PropertyInfo> GetVisibleProperties(object bindingObject)
            => bindingObject.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<SuperDisplayAttribute>()?.Visible == true)
                .ToList();

        /// <summary>按 Order 排序后取属性上的 <see cref="SuperDisplayAttribute"/></summary>
        public static SuperDisplayAttribute? DisplayOf(PropertyInfo prop)
            => prop.GetCustomAttribute<SuperDisplayAttribute>();

        /// <summary>
        /// 取分组的第 <paramref name="segment"/> 段（0 基；没有则默认组名）。
        /// 优先用强类型 <see cref="UI.Attributes.SuperDisplayAttribute.Group"/> 数组，
        /// 为空时回退到 <see cref="UI.Attributes.SuperDisplayAttribute.GroupPath"/> 按 '/' 切分。
        /// </summary>
        public static string GroupSegment(PropertyInfo prop, int segment)
        {
            var display = DisplayOf(prop);
            string[] parts;

            if (display?.Group != null && display.Group.Length > 0)
            {
                parts = display.Group;
            }
            else
            {
                var path = display?.GroupPath;
                if (string.IsNullOrWhiteSpace(path)) return DefaultGroupName;
                parts = path!.Split('/');
            }

            return segment >= 0 && segment < parts.Length && !string.IsNullOrWhiteSpace(parts[segment])
                ? parts[segment].Trim()
                : DefaultGroupName;
        }
    }
}
