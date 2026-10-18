using System;
using System.Collections.Generic;
using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 「外观」组的通用属性打包：填充 / 边框 / 粗细 / 圆角。
    ///
    /// 为什么要有这一组助手
    /// ---------
    /// 图元描述符里最刺眼的重复就是这几个键。改造成生成器之前实测：
    /// 17 个图元共 187 条属性声明，「边框色 / 边框粗细 / 字号 / 文字色 / 文字 / 字重 / 填充色 / 圆角」
    /// 这 8 个键在 15 个图元里重复声明了 <b>99 处、630 行</b>（占属性声明总量的 49%）。
    /// <see cref="GeometryProperties"/> 早就用"打包 + With"治过几何属性（17 个图元各借它省掉 6 条），
    /// 但外观这一组没有对应物，于是每加一个图元就再抄一遍。
    ///
    /// <b>它打包的是"暴露基类依赖属性"，不是"新声明依赖属性"</b>
    /// ---------
    /// 这几个属性（<see cref="ScadaElementBase.FillProperty"/> 等）早就声明在基类上了，
    /// 这里只产出指向它们的描述符条目。所以<b>不要</b>给它们加 <c>[ScadaProperty]</c>：
    /// 那会在子类上再生成一个同名依赖属性，遮蔽基类那一份，模板里的
    /// <c>TemplateBinding Fill</c> 会绑到子类新属性上，而基类 Fill 永远不动——
    /// 表现为"属性面板改了没反应"（见 <see cref="ScadaPropertyAttribute"/> 的类注释）。
    ///
    /// 每个方法返回<b>新数组</b>而不是共享实例：注册期会校验"同一图元内属性键不重复"，
    /// 共享可变实例会让某个调用方顺手改到别人的声明。
    /// </summary>
    public static class AppearanceProperties
    {
        /// <summary>填充色（矩形的内部、按钮的底色、指示灯点亮时的灯色）</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Fill(
            string defaultValue = "#FFFFFFFF", string? description = null, string group = "外观",
            string displayName = "填充色")
            => One("Fill", displayName, ElementPropertyKind.Color, group, defaultValue,
                   ScadaElementBase.FillProperty, description);

        /// <summary>边框色/轮廓色（标签按图元语义覆盖：外圈色、轮廓色、管壁色…）</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Stroke(
            string defaultValue = "#FF7A7A7A", string? description = null, string group = "外观",
            string displayName = "边框色")
            => One("Stroke", displayName, ElementPropertyKind.Color, group, defaultValue,
                   ScadaElementBase.StrokeProperty, description);

        /// <summary>边框粗细</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> StrokeThickness(
            string defaultValue = "1", double max = 20, string group = "外观", string? description = null,
            string displayName = "边框粗细")
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = "StrokeThickness", DisplayName = displayName, Kind = ElementPropertyKind.Number,
                    Group = group, DefaultValue = defaultValue, Min = 0, Max = max,
                    TargetProperty = ScadaElementBase.StrokeThicknessProperty, Description = description,
                },
            };

        /// <summary>圆角</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> CornerRadius(
            string defaultValue = "0", string group = "外观", string? description = null)
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                    Group = group, DefaultValue = defaultValue, Min = 0, Max = 200,
                    TargetProperty = ScadaElementBase.CornerRadiusProperty, Description = description,
                },
            };

        /// <summary>把若干组拼成一组（便于一个"外观套餐"一次给全）</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Combine(
            params IReadOnlyList<ElementPropertyDescriptor>[] groups)
        {
            var list = new List<ElementPropertyDescriptor>();
            if (groups is not null)
            {
                for (int i = 0; i < groups.Length; i++)
                {
                    if (groups[i] is { } group)
                        list.AddRange(group);
                }
            }

            return list.ToArray();
        }

        /// <summary>
        /// 把一组描述符里的若干条换个显示名 / 说明（按 Key 对号），返回<b>新数组</b>。
        ///
        /// 为什么需要它
        /// ---------
        /// <see cref="StateVisualElement"/> 那六个状态属性是泵 / 电机 / 管道共用的（一处声明，
        /// 生成器只给得出一个 DisplayName / Description），但这三个图元对同一件事的叫法不一样：
        /// 泵叫「泵壳色」、电机叫「机身色」、管道叫「管身色」；管道连状态都叫「流动状态」
        /// （0=停流 1=流动 2=异常）。若照搬基类那一个名字，属性面板上就会丢掉"这层底色到底指什么"的信息——
        /// 而那不是口味问题，是现场靠它认设备的那一层语言。
        ///
        /// 只换显示名与说明，不换 Key / 默认值 / TargetProperty：落盘键与换算落点仍只有基类一处事实来源。
        /// 返回新数组（不就地改），因为 <see cref="ScadaPropertyCatalog"/> 的结果是按类型缓存的实例。
        /// </summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Rename(
            IReadOnlyList<ElementPropertyDescriptor> source,
            params (string Key, string? DisplayName, string? Description)[] labels)
        {
            if (source is null || source.Count == 0 || labels is null || labels.Length == 0)
                return source ?? Array.Empty<ElementPropertyDescriptor>();

            var renamed = new ElementPropertyDescriptor[source.Count];

            for (int i = 0; i < source.Count; i++)
            {
                var descriptor = source[i];
                string displayName = descriptor.DisplayName;
                string? description = descriptor.Description;

                foreach (var (key, label, note) in labels)
                {
                    if (string.Equals(descriptor.Key, key, StringComparison.Ordinal))
                    {
                        displayName = label ?? displayName;
                        description = note ?? description;
                        break;
                    }
                }

                renamed[i] = string.Equals(displayName, descriptor.DisplayName, StringComparison.Ordinal)
                             && string.Equals(description, descriptor.Description, StringComparison.Ordinal)
                    ? descriptor
                    : new ElementPropertyDescriptor
                    {
                        Key = descriptor.Key,
                        DisplayName = displayName,
                        Kind = descriptor.Kind,
                        DefaultValue = descriptor.DefaultValue,
                        Group = descriptor.Group,
                        Description = description,
                        IsBindable = descriptor.IsBindable,
                        Min = descriptor.Min,
                        Max = descriptor.Max,
                        Choices = descriptor.Choices,
                        TargetProperty = descriptor.TargetProperty,
                    };
            }

            return renamed;
        }

        private static IReadOnlyList<ElementPropertyDescriptor> One(
            string key, string displayName, ElementPropertyKind kind, string group,
            string defaultValue, DependencyProperty target, string? description)
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = key, DisplayName = displayName, Kind = kind, Group = group,
                    DefaultValue = defaultValue, TargetProperty = target, Description = description,
                },
            };
    }

    /// <summary>
    /// 「文字」组的通用属性打包：文字 / 文字颜色 / 字号 / 字重 / 对齐。
    ///
    /// 与 <see cref="AppearanceProperties"/> 同一套理由与同一条纪律：
    /// 打包的是<b>基类已声明的依赖属性</b>，不要用 <c>[ScadaProperty]</c> 再声明一遍。
    ///
    /// 默认值按图元语义给（按钮上的字是白的、指示灯下的标签是深色的），
    /// 所以每个方法都允许覆盖；不给就取 SCADA 里最常见的那个值。
    /// </summary>
    public static class TextProperties
    {
        /// <summary>文字内容（属性面板上的标签由调用方定：按钮叫"文字"、数值域叫"说明字"）</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Text(
            string displayName = "文字", string defaultValue = "", bool isBindable = true,
            string? description = null, string group = "文字",
            ElementPropertyKind kind = ElementPropertyKind.Text)
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = "Text", DisplayName = displayName, Kind = kind,
                    Group = group, DefaultValue = defaultValue, IsBindable = isBindable,
                    TargetProperty = ScadaElementBase.TextProperty, Description = description,
                },
            };

        /// <summary>文字颜色</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Foreground(
            string defaultValue = "#FF202020", string displayName = "文字颜色", string group = "文字",
            string? description = null)
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = "Foreground", DisplayName = displayName, Kind = ElementPropertyKind.Color,
                    Group = group, DefaultValue = defaultValue,
                    TargetProperty = ScadaElementBase.ForegroundProperty, Description = description,
                },
            };

        /// <summary>字号</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> FontSize(
            string defaultValue = "12", string displayName = "字号", string group = "文字",
            string? description = null)
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = "FontSize", DisplayName = displayName, Kind = ElementPropertyKind.Number,
                    Group = group, DefaultValue = defaultValue, Min = 6, Max = 200,
                    TargetProperty = ScadaElementBase.FontSizeProperty, Description = description,
                },
            };

        /// <summary>字重</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> FontWeight(
            string defaultValue = "Normal", string displayName = "字重", string? description = null,
            string group = "文字")
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = "FontWeight", DisplayName = displayName, Kind = ElementPropertyKind.Choice,
                    Group = group, DefaultValue = defaultValue,
                    Choices = new[] { "Normal", "SemiBold", "Bold" },
                    TargetProperty = ScadaElementBase.FontWeightProperty, Description = description,
                },
            };

        /// <summary>水平对齐</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> TextAlignment(
            string defaultValue = "Center", string displayName = "水平对齐", string? description = null,
            string group = "文字")
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = "TextAlignment", DisplayName = displayName, Kind = ElementPropertyKind.Choice,
                    Group = group, DefaultValue = defaultValue,
                    Choices = new[] { "Left", "Center", "Right", "Justify" },
                    TargetProperty = ScadaElementBase.TextAlignmentProperty, Description = description,
                },
            };

        /// <summary>垂直对齐（Stretch 不列出：多行文本在 Stretch 下与 Top 表现一致，列出来只会让人犹豫）</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> VerticalContentAlignment(
            string defaultValue = "Center", string displayName = "垂直对齐", string? description = null,
            string group = "文字")
            => new[]
            {
                new ElementPropertyDescriptor
                {
                    Key = "VerticalContentAlignment", DisplayName = displayName,
                    Kind = ElementPropertyKind.Choice, Group = group, DefaultValue = defaultValue,
                    Choices = new[] { "Top", "Center", "Bottom" },
                    TargetProperty = ScadaElementBase.VerticalContentAlignmentProperty,
                    Description = description,
                },
            };

        /// <summary>把若干组拼成一组</summary>
        public static IReadOnlyList<ElementPropertyDescriptor> Combine(
            params IReadOnlyList<ElementPropertyDescriptor>[] groups)
            => AppearanceProperties.Combine(groups);
    }
}
