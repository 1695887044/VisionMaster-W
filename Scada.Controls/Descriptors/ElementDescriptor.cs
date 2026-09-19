using System;
using System.Collections.Generic;
using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 一类图元（"矩形""指示灯""趋势图"）的完整声明：它叫什么、怎么造、有哪些属性。
    ///
    /// 这是图元库唯一的"说明书"，同时也是注册表的注册单元。新增一类图元 =
    /// 写一个控件类 + 加一条本描述符 + 在工具箱里出现，模型层与序列化层一行不改
    /// （<see cref="ScadaElement.TypeKey"/> 只是个字符串，领域层不认识任何取值）。
    ///
    /// <see cref="Properties"/> 里必须完整包含 <see cref="GeometryProperties.Standard"/>
    /// （位置/尺寸/旋转）——注册期会校验，因为属性面板与绑定引擎都假定这几项永远在。
    /// 惯用写法：<c>Properties = GeometryProperties.With(…特有属性…)</c>。
    ///
    /// 为什么不做成接口 + 多个实现类：图元的差异全在"数据"上（键、默认值、目标依赖属性），
    /// 不在"行为"上（行为在控件类里）。用接口只会得到一个字段声明比接口方法还多的类，
    /// 白白多一层间接。真需要外界扩展图元库时，它们提供的也是这个类的实例。
    /// </summary>
    public sealed class ElementDescriptor
    {
        /// <summary>图元类型键（写入 <see cref="ScadaElement.TypeKey"/>，如 "Hmi.Rectangle"）。大小写不敏感</summary>
        public required string TypeKey { get; init; }

        /// <summary>给人看的名字（工具箱按钮、图层列表的默认名）</summary>
        public required string DisplayName { get; init; }

        /// <summary>
        /// 承载本图元外观的控件类型。必须派生自 <see cref="ScadaElementBase"/> 且有无参公开构造。
        ///
        /// 存 <see cref="Type"/> 而不是"造控件的委托"：类型是工具箱、注册期校验、将来的
        /// 拖放预览都要用的信息，而委托只能回答"怎么造"。造实例这一处用
        /// <see cref="Activator"/> 一次的代价可以忽略（一个图元只在被创建时造一次控件）。
        /// </summary>
        public required Type ControlType { get; init; }

        /// <summary>工具箱分组（"基础""指示""图表"…）；同时决定工具箱里的排列顺序</summary>
        public string Category { get; init; } = "基础";

        /// <summary>新建图元时的默认宽（画面像素）</summary>
        public double DefaultWidth { get; init; } = 120;

        /// <summary>新建图元时的默认高（画面像素）</summary>
        public double DefaultHeight { get; init; } = 40;

        /// <summary>本图元支持的全部属性（几何 + 特有），顺序即属性面板里的显示顺序</summary>
        public IReadOnlyList<ElementPropertyDescriptor> Properties { get; init; } = GeometryProperties.Standard;

        /// <summary>
        /// 本类图元支持的<b>事件钩子</b>清单（"这个图元能配哪些事件"的唯一出处）。
        ///
        /// 为什么必须声明在描述符上，而不是界面里写死"按钮有点击、矩形没有"：这条知识属于图元类型，
        /// 而图元类型是可扩展的（第三方往本库加一类图元）。界面写死的话，别人加的图元永远配不上事件——
        /// 同一个病根在属性侧已经用 <see cref="Properties"/> 治过一遍，事件侧照抄同一个药方。
        ///
        /// 默认空集：纯装饰的图元（底图、区域框、连线）本来就没有事件，"没有可配的事件"是正常状态，
        /// 不是没配好。
        ///
        /// S5 起这份清单<b>开始被消费</b>：属性面板按它长事件行，画布按它发事件。所以声明一条就要
        /// 同时有一条真能发出的路径——清单里出现发不出来的事件，用户配上就是永远不响的钩子。
        /// 各图元具体声明了什么、为什么这么声明，写在 <see cref="BuiltInElements"/> 的类注释里
        /// （那里是内置图元唯一的事实出处，本注释不重复一遍以免两处对不上）。
        /// </summary>
        public IReadOnlyList<ScadaEventType> Events { get; init; } = Array.Empty<ScadaEventType>();

        /// <summary>补充说明（工具箱悬停提示；可为空）</summary>
        public string? Description { get; init; }

        /// <summary>
        /// 造一个"新建状态"的图元模型（只填模型，不碰 WPF）。
        ///
        /// 名字先填显示名（图层列表里不至于一排空白）；重名由 S3 编辑器在放置时去重，
        /// 因为"唯一名"是编辑器的语义，模型层不强制（强制了会让复制/粘贴多一层失败路径）。
        /// </summary>
        public ScadaElement CreateElement(double x = 0, double y = 0)
            => new()
            {
                TypeKey = TypeKey,
                Name = DisplayName,
                X = x,
                Y = y,
                Width = DefaultWidth,
                Height = DefaultHeight,
            };

        /// <summary>造一个承载本图元的控件实例（尚未绑定模型，调用方负责赋 <c>Element</c>）</summary>
        public ScadaElementBase CreateControl()
        {
            if (Activator.CreateInstance(ControlType) is ScadaElementBase control)
                return control;

            // 注册期已校验过派生关系与无参构造，走到这里只可能是注册表被绕过（直接 new 描述符用）。
            throw new InvalidOperationException(
                $"{TypeKey} 的 ControlType {ControlType.FullName} 不能实例化为 ScadaElementBase");
        }

        /// <summary>
        /// 注册期自检。返回 null 表示通过，否则是给人看的失败原因
        /// （注册表把原因原样返回，断言与日志据此定位，避免"注册了却没生效"的哑失败）。
        ///
        /// 这里顺手校验收不到运行期报错的两类笔误：
        /// ① 目标依赖属性挂到了不相干的控件类型上——不会抛异常，只是永远不生效；
        /// ② 默认值写法与目标类型对不上（如给 Brush 写 "#GGGGGG"）——只在真正创建图元时才炸。
        /// 两类都属于"不报错的静默失败"，必须在注册期挡掉。
        /// </summary>
        internal string? Validate()
        {
            if (string.IsNullOrWhiteSpace(TypeKey))
                return "TypeKey 为空";

            if (string.IsNullOrWhiteSpace(DisplayName))
                return $"{TypeKey} 缺少显示名";

            if (ControlType is null)
                return $"{TypeKey} 没给 ControlType";

            if (!typeof(ScadaElementBase).IsAssignableFrom(ControlType))
                return $"{TypeKey} 的 ControlType {ControlType.FullName} 不是 ScadaElementBase 派生类";

            if (ControlType.IsAbstract)
                return $"{TypeKey} 的 ControlType {ControlType.Name} 是抽象类（DefaultStyleKey 无法在抽象类上成立）";

            if (ControlType.GetConstructor(Type.EmptyTypes) is null)
                return $"{TypeKey} 的 ControlType {ControlType.Name} 缺少公开无参构造（WPF 控件样式必须有无参构造）";

            if (DefaultWidth <= 0 || DefaultHeight <= 0)
                return $"{TypeKey} 的默认尺寸非法（{DefaultWidth} × {DefaultHeight}）";

            if (Properties is null || Properties.Count == 0)
                return $"{TypeKey} 没有声明任何属性（至少要含几何属性）";

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var property in Properties)
            {
                if (property is null)
                    return $"{TypeKey} 的属性清单里有 null 项";

                var error = property.Validate();
                if (error != null)
                    return $"{TypeKey}.{property.Key}：{error}";

                if (!seen.Add(property.Key))
                    return $"{TypeKey} 的属性键 {property.Key} 重复声明";

                if (property.IsGeometry && property.TargetProperty != null)
                    return $"{TypeKey}.{property.Key} 是几何键，由基类统一落到 FrameworkElement，不应声明 TargetProperty";

                if (property.TargetProperty is { } dp)
                {
                    // OwnerType 是声明该依赖属性的类：WidthProperty 的 OwnerType 是 FrameworkElement，
                    // 于是"矩形声明 WidthProperty"也合法（这正是我们要的继承语义）。
                    if (!IsPropertyOn(dp, ControlType))
                        return $"{TypeKey}.{property.Key} 的目标属性 {dp.OwnerType.Name}.{dp.Name} 不在 {ControlType.Name} 上";

                    if (!string.IsNullOrEmpty(property.DefaultValue))
                    {
                        try
                        {
                            ScadaElementBase.ConvertFromString(dp.PropertyType, property.DefaultValue);
                        }
                        catch (Exception ex)
                        {
                            return $"{TypeKey}.{property.Key} 的默认值 \"{property.DefaultValue}\" " +
                                   $"转不成 {dp.PropertyType.Name}：{ex.Message}";
                        }
                    }
                }

                if (property.Kind == ElementPropertyKind.Choice
                    && !string.IsNullOrEmpty(property.DefaultValue)
                    && !Contains(property.Choices, property.DefaultValue))
                {
                    return $"{TypeKey}.{property.Key} 的默认值 \"{property.DefaultValue}\" 不在候选项里";
                }
            }

            // 几何属性必须齐全：属性面板、绑定引擎、"适应画面"都假定它们存在。
            foreach (var geometryKey in ElementValueAccess.KnownGeometryKeys)
            {
                if (!seen.Contains(geometryKey))
                    return $"{TypeKey} 缺少几何属性 {geometryKey}（请用 GeometryProperties.With(…)）";
            }

            // 事件清单只校"重复声明"这一种笔误：取值是枚举，写错名字编译期就过不了。
            // 重复声明会让运行态对同一个事件执行两遍动作，而 S5 的属性面板会长出两行同名勾选框——
            // 都属于"不报错的静默失败"，与上面属性清单同一待遇。
            if (Events is null)
                return $"{TypeKey} 的 Events 为 null（没有事件就给空集，别给 null）";

            var declaredEvents = new HashSet<ScadaEventType>();
            foreach (var declared in Events)
            {
                if (!declaredEvents.Add(declared))
                    return $"{TypeKey} 的事件 {declared} 重复声明";
            }

            return null;
        }

        /// <summary>
        /// 目标依赖属性能不能真正落在 <paramref name="controlType"/> 上。
        ///
        /// 不能只看 <see cref="DependencyProperty.OwnerType"/>：WPF 的
        /// Control.Foreground / FontSize / FontWeight 实际是 TextElement 的同名属性经
        /// AddOwner(typeof(Control)) 挂上来的，OwnerType 永远停在 TextElement，
        /// 可它们在任意 Control 派生类上都完全可用。只看 OwnerType 会把这类合法属性误判为非法
        /// （曾经就因此让 5 个内置图元全部注册失败）。
        ///
        /// 所以先看派生关系，再交给 WPF 本体回答：GetMetadata 天然沿继承链查找，
        /// AddOwner 挂上来的也认；该类型及其基类都没注册过元数据时它抛异常，即"挂上去也不生效"。
        /// </summary>
        private static bool IsPropertyOn(DependencyProperty dp, Type controlType)
        {
            if (dp.OwnerType.IsAssignableFrom(controlType))
                return true;

            try
            {
                dp.GetMetadata(controlType);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static bool Contains(IReadOnlyList<string> choices, string value)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                if (string.Equals(choices[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
