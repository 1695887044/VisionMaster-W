using System.Collections.Generic;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 内置图元清单（S2 交付的第一批：矩形 / 椭圆 / 文本 / 按钮 / 指示灯；S7 起陆续扩充）。
    ///
    /// 这个类只回答"我们自带哪些图元、每个图元有哪些属性"，不负责注册——
    /// 注册由 <see cref="ElementRegistry"/> 在类型初始化时自动完成（见其静态构造），
    /// 于是宿主（主程序、断言工程、将来任何一个引用本库的程序）都不需要记得
    /// "先注册再使用"这一步，也就不会出现"少调一行，工具箱空了"这种问题。
    ///
    /// 新增一类图元要动的地方，一共三处，都在本库内：
    /// ① Elements/ 下写控件类（继承 <see cref="ScadaElementBase"/>，自带静态构造换 DefaultStyleKey）；
    /// ② Themes/ 下写样式并在 Generic.xaml 里加一行合并；
    /// ③ 本清单里加一条描述符。
    /// 领域层（VM.Scada）、序列化层、编辑器一行不改——.vms 里只留一个字符串 TypeKey。
    ///
    /// TypeKey 用 "Hmi." 前缀：它是写进 .vms 的长期标识，一旦发布就不能再改，
    /// 所以取值要能表达"这是人机界面的图元"，而不是实现细节（类名/命名空间都可能重构）。
    ///
    /// 关于 <see cref="ElementDescriptor.Events"/>（本图元能配哪些事件钩子）的声明口径：
    /// <b>只声明"已经有人发得出来"的事件</b>。属性面板按这份清单长行，清单里有一条发不出来的事件，
    /// 用户就会配出一个永远不响的钩子——那比少一条更糟（他会怀疑自己配错了，而不是软件没做）。
    /// 于是：
    /// ① 按下/释放由画布在运行态代发（见 ScadaCanvas 的鼠标处理），本阶段先只开给"操作"类图元；
    ///    底图、区域框这类纯装饰图元不声明——技术上画布点谁都发得出来，但"能点"不等于"该配"，
    ///    这条产品规则就写在描述符上。哪天要开放给矩形，这里加一行即可，别处都不用改。
    /// ② 值改变（<see cref="ScadaEventType.ValueChanged"/>）仍然<b>一律不声明</b>，但理由换了：
    ///    S6 的数据泵已经能把变量值写进图元属性（<see cref="ScadaElementBase.TryApplyRuntimeValue"/>），
    ///    可"这次写进去的值和上一次是不是同一个"没有第二个人知道——数据泵在值没变时跳过 SetValue，
    ///    却仍按"写成功"返回（见其实现），于是全链路没有任何一处能判定"值确实变了"。
    ///    要开这条事件，得先给数据泵加一条"值确实变了"的回传通道（动基类写入契约 + 绑定器 + 宿主 +
    ///    本库之外的断言白名单），那是事件系统的活，不属于图元库扩充。先空着，比先长出一个永远不响的钩子强。
    /// </summary>
    public static class BuiltInElements
    {
        /// <summary>全部内置图元描述符</summary>
        public static IReadOnlyList<ElementDescriptor> All { get; } = new ElementDescriptor[]
        {
            new()
            {
                TypeKey = "Hmi.Rectangle",
                DisplayName = "矩形",
                ControlType = typeof(RectangleElement),
                Category = "基础",
                DefaultWidth = 120,
                DefaultHeight = 60,
                Description = "带填充与边框的矩形，中间可放一段文字（设备本体、区域背景、状态块）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFFFFFFF", IsBindable = true,
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "0", Min = 0, Max = 200,
                        Description = "半径超过短边一半时 WPF 会自行收敛成胶囊形",
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "文字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Ellipse",
                DisplayName = "椭圆",
                ControlType = typeof(EllipseElement),
                Category = "基础",
                DefaultWidth = 100,
                DefaultHeight = 100,
                Description = "圆形/椭圆的设备或状态点（罐体、电机、风机）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFFFFFFF", IsBindable = true,
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "文字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Text",
                DisplayName = "文本",
                ControlType = typeof(TextElement),
                Category = "基础",
                DefaultWidth = 120,
                DefaultHeight = 24,
                Description = "标题、说明、单位；把文字属性绑到变量上就是动态数值显示",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Text", DisplayName = "内容", Kind = ElementPropertyKind.MultilineText,
                        Group = "文字", DefaultValue = "文本", IsBindable = true,
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020", IsBindable = true,
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "14", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "标题常用 Bold；Normal 与 SemiBold 在小字号下区别不大",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "TextAlignment", DisplayName = "水平对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Center",
                        Choices = new[] { "Left", "Center", "Right", "Justify" },
                        TargetProperty = ScadaElementBase.TextAlignmentProperty,
                    },
                    new()
                    {
                        Key = "VerticalContentAlignment", DisplayName = "垂直对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Center",
                        Choices = new[] { "Top", "Center", "Bottom" },
                        Description = "Stretch 不列出：多行文本在 Stretch 下与 Top 表现一致，列出来只会让人犹豫",
                        TargetProperty = ScadaElementBase.VerticalContentAlignmentProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Button",
                DisplayName = "按钮",
                ControlType = typeof(ButtonElement),
                Category = "操作",
                DefaultWidth = 88,
                DefaultHeight = 32,
                Description = "操作员按下的动作入口；按下/释放各是一个事件钩子，点下去干什么在属性面板「事件」里配",
                Events = new[] { ScadaEventType.Pressed, ScadaEventType.Released },
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Text", DisplayName = "文字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = "按钮", IsBindable = true,
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF2D7DD2", IsBindable = true,
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF1F5C9E", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "3", Min = 0, Max = 200,
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Indicator",
                DisplayName = "指示灯",
                ControlType = typeof(IndicatorElement),
                Category = "指示",
                DefaultWidth = 40,
                DefaultHeight = 56,
                Description = "一根开关量变量的可视化身；把「点亮」属性绑到变量即可",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "IsOn", DisplayName = "点亮", Kind = ElementPropertyKind.Bool,
                        Group = "状态", DefaultValue = "False", IsBindable = true,
                        Description = "运行态把工程变量的布尔值绑到这里",
                        TargetProperty = IndicatorElement.IsOnProperty,
                    },
                    new()
                    {
                        Key = "OnColor", DisplayName = "点亮色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF34C759", IsBindable = true,
                        TargetProperty = IndicatorElement.OnColorProperty,
                    },
                    new()
                    {
                        Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF3A3A3A", IsBindable = true,
                        TargetProperty = IndicatorElement.OffColorProperty,
                    },
                    new()
                    {
                        Key = "Shape", DisplayName = "形状", Kind = ElementPropertyKind.Choice,
                        Group = "外观", DefaultValue = "Circle",
                        Choices = new[] { "Circle", "Square" },
                        TargetProperty = IndicatorElement.ShapeProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "标签", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "显示在灯下方的一行字（如「1#电机」）",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "标签颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "标签字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.ProgressBar",
                DisplayName = "棒图",
                ControlType = typeof(ProgressBarElement),
                Category = "数值",
                DefaultWidth = 160,
                DefaultHeight = 20,
                Description = "一个数值在量程里的可视化身；把「当前值」绑到变量上即可（产量完成率、料仓余量、阀门开度）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0", IsBindable = true,
                        Description = "运行态把工程变量绑到这里；条长 = (当前值 − 下限) ÷ (上限 − 下限)，超出量程按两端收敛",
                        TargetProperty = ProgressBarElement.ValueProperty,
                    },
                    new()
                    {
                        Key = "Minimum", DisplayName = "量程下限", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0",
                        Description = "下限不小于上限时画空条，而不是让非法量程把整页渲染打断",
                        TargetProperty = ProgressBarElement.MinimumProperty,
                    },
                    new()
                    {
                        Key = "Maximum", DisplayName = "量程上限", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "100",
                        TargetProperty = ProgressBarElement.MaximumProperty,
                    },
                    new()
                    {
                        Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "0.#",
                        Description = "标准 .NET 数字格式串，可带单位（如 0.0 ℃）；写错了退回通用格式，不会让画面出错",
                        TargetProperty = ProgressBarElement.ValueFormatProperty,
                    },
                    new()
                    {
                        Key = "Orientation", DisplayName = "走向", Kind = ElementPropertyKind.Choice,
                        Group = "外观", DefaultValue = "Horizontal",
                        Choices = new[] { "Horizontal", "Vertical" },
                        Description = "横向从左往右长；纵向从下往上长（液位、料位天生是竖着的）",
                        TargetProperty = ProgressBarElement.OrientationProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF2D7DD2", IsBindable = true,
                        Description = "已填充那一段的颜色",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "空槽色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A", IsBindable = true,
                        Description = "未填充那一段的颜色；浅色画面上要调亮，否则整条看不出边界",
                        TargetProperty = ProgressBarElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "2", Min = 0, Max = 200,
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    },
                    new()
                    {
                        Key = "ShowValue", DisplayName = "显示数值", Kind = ElementPropertyKind.Bool,
                        Group = "文字", DefaultValue = "True",
                        TargetProperty = ProgressBarElement.ShowValueProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "数值颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "数值字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "11", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "数值字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Bold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "默认 Bold：条上的字压在填充色上，不加粗在小尺寸下容易糊",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.IOField",
                DisplayName = "数值域",
                ControlType = typeof(IOFieldElement),
                Category = "数值",
                DefaultWidth = 140,
                DefaultHeight = 28,
                Description = "带边框的数值显示框：左边一行说明字，右边「数值 + 单位」；把「数值」绑到变量上即可（温度、计数、位置）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Value", DisplayName = "数值", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0", IsBindable = true,
                        Description = "运行态把工程变量绑到这里；框里显示的是它按「数值格式」格式化后的样子",
                        TargetProperty = IOFieldElement.ValueProperty,
                    },
                    new()
                    {
                        Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "0.##",
                        Description = "标准 .NET 数字格式串：0.0 定一位小数、F2 补零到两位、#,##0 加千分位；" +
                                      "写错了退回通用格式，不会让画面出错",
                        TargetProperty = IOFieldElement.ValueFormatProperty,
                    },
                    new()
                    {
                        Key = "Unit", DisplayName = "单位", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = string.Empty,
                        Description = "跟在数值后面的一小段字（℃ / mm / pcs）；留空则只显数值",
                        TargetProperty = IOFieldElement.UnitProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "说明字", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "显示在数值左边的一行字（如「温度」「1#工位」）；留空则数值独占整行",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "14", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "TextAlignment", DisplayName = "数值对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Right",
                        Choices = new[] { "Left", "Center", "Right", "Justify" },
                        Description = "默认 Right：小数点在右边排成一条线，一列数值扫起来才快；说明字不受它影响，恒靠左",
                        TargetProperty = ScadaElementBase.TextAlignmentProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "填充色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFFFFFFF", IsBindable = true,
                        Description = "框内底色；深色画面上要跟着调暗，否则一列白框会盖过画面主体",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF7A7A7A", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "CornerRadius", DisplayName = "圆角", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "2", Min = 0, Max = 200,
                        TargetProperty = ScadaElementBase.CornerRadiusProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Lamp",
                DisplayName = "多态灯",
                ControlType = typeof(LampElement),
                Category = "指示",
                DefaultWidth = 40,
                DefaultHeight = 56,
                Description = "停机/运行/警告/报警四态合一的状态灯；把「状态」绑到变量即可（枚举名或 0~3 的整数都认）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "State", DisplayName = "状态", Kind = ElementPropertyKind.Choice,
                        Group = "状态", DefaultValue = "Off",
                        Choices = new[] { "Off", "On", "Warning", "Alarm" },
                        IsBindable = true,
                        Description = "运行态把工程变量绑到这里：整数 0=熄灭 1=点亮 2=警告 3=报警，写枚举名（On/Warning/Alarm）也认",
                        TargetProperty = LampElement.StateProperty,
                    },
                    new()
                    {
                        Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF3A3A3A", IsBindable = true,
                        TargetProperty = LampElement.OffColorProperty,
                    },
                    new()
                    {
                        Key = "OnColor", DisplayName = "点亮色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FF34C759", IsBindable = true,
                        TargetProperty = LampElement.OnColorProperty,
                    },
                    new()
                    {
                        Key = "WarningColor", DisplayName = "警告色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FFFFB020", IsBindable = true,
                        TargetProperty = LampElement.WarningColorProperty,
                    },
                    new()
                    {
                        Key = "AlarmColor", DisplayName = "报警色", Kind = ElementPropertyKind.Color,
                        Group = "状态", DefaultValue = "#FFE03A2B", IsBindable = true,
                        TargetProperty = LampElement.AlarmColorProperty,
                    },
                    new()
                    {
                        Key = "Shape", DisplayName = "形状", Kind = ElementPropertyKind.Choice,
                        Group = "外观", DefaultValue = "Circle",
                        Choices = new[] { "Circle", "Square" },
                        Description = "与指示灯共用同一套形状词汇：多灯密集排列时方形更省地方",
                        TargetProperty = LampElement.ShapeProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "边框色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "边框粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "标签", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "显示在灯下方的一行字（如「1#主轴」）",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "标签颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "标签字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "标签字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Normal",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Clock",
                DisplayName = "时钟",
                ControlType = typeof(ClockElement),
                Category = "指示",
                DefaultWidth = 180,
                DefaultHeight = 28,
                Description = "走时的系统日期时间；格式串自己写（如 HH:mm:ss），刷新节拍默认每秒一次",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Format", DisplayName = "格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "yyyy-MM-dd HH:mm:ss",
                        Description = "标准 .NET 日期时间格式串：HH:mm:ss 只显时间，带 dddd 会出星期；" +
                                      "写错了退回默认形状，不会让整页渲染中断",
                        TargetProperty = ClockElement.FormatProperty,
                    },
                    new()
                    {
                        Key = "Interval", DisplayName = "刷新节拍", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "1", Min = 0.1, Max = 60,
                        Description = "单位秒。落在 0.1~60 之外会被收敛进区间（0 或负数不会变成一个失控的定时器）；" +
                                      "跳动会对齐整拍，不慢半拍",
                        TargetProperty = ClockElement.IntervalProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "标签", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "显示在时间下方的一行字（如「系统时间」「1#线」）；留空则只显时间",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FF202020",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "16", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "默认 SemiBold：时间是要一眼扫到的信息，全 Normal 在深色底上偏虚",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "TextAlignment", DisplayName = "水平对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Center",
                        Choices = new[] { "Left", "Center", "Right", "Justify" },
                        TargetProperty = ScadaElementBase.TextAlignmentProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Gauge",
                DisplayName = "表盘",
                ControlType = typeof(GaugeElement),
                Category = "数值",
                DefaultWidth = 140,
                DefaultHeight = 140,
                Description = "圆盘刻度上的一根指针；把「当前值」绑到变量上即可（压力、转速、温度）",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0", IsBindable = true,
                        Description = "运行态把工程变量绑到这里；指针角度 = 起始角 + 扫过角 × " +
                                      "(当前值 − 下限) ÷ (上限 − 下限)，超出量程按两端收敛",
                        TargetProperty = GaugeElement.ValueProperty,
                    },
                    new()
                    {
                        Key = "Minimum", DisplayName = "量程下限", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0",
                        Description = "下限不小于上限时指针停在起始角，而不是让非法量程把整页渲染打断",
                        TargetProperty = GaugeElement.MinimumProperty,
                    },
                    new()
                    {
                        Key = "Maximum", DisplayName = "量程上限", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "100",
                        TargetProperty = GaugeElement.MaximumProperty,
                    },
                    new()
                    {
                        Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "0.#",
                        Description = "标准 .NET 数字格式串（0.0 定一位小数、F2 补零到两位）；" +
                                      "写错了退回通用格式，不会让画面出错",
                        TargetProperty = GaugeElement.ValueFormatProperty,
                    },
                    new()
                    {
                        Key = "Unit", DisplayName = "单位", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = string.Empty,
                        Description = "跟在数值后面的一小段字（MPa / rpm / ℃）；留空则只显数值",
                        TargetProperty = GaugeElement.UnitProperty,
                    },
                    new()
                    {
                        Key = "StartAngle", DisplayName = "起始角", Kind = ElementPropertyKind.Number,
                        Group = "刻度", DefaultValue = "135", Min = -360, Max = 360,
                        Description = "度。0° 指向右（3 点钟方向）、角度顺时针增加，所以 135° 是左下（7 点半）；" +
                                      "默认 135° 配 270° 缺口落在正下方，正是放读数的地方",
                        TargetProperty = GaugeElement.StartAngleProperty,
                    },
                    new()
                    {
                        Key = "SweepAngle", DisplayName = "扫过角", Kind = ElementPropertyKind.Number,
                        Group = "刻度", DefaultValue = "270", Min = -360, Max = 360,
                        Description = "度。正数顺时针、负数逆时针，绝对值 360 画整圈；超出 ±360 会收敛（刻度不会绕回来叠在一起）",
                        TargetProperty = GaugeElement.SweepAngleProperty,
                    },
                    new()
                    {
                        Key = "TickDivisions", DisplayName = "刻度格数", Kind = ElementPropertyKind.Number,
                        Group = "刻度", DefaultValue = "5", Min = 0, Max = 60,
                        Description = "格数 + 1 = 刻度线根数（含两端），所以默认 5 格就是 0/20/40/60/80/100 六个读数点；0 = 不画刻度",
                        TargetProperty = GaugeElement.TickDivisionsProperty,
                    },
                    new()
                    {
                        Key = "Fill", DisplayName = "指针色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FFE03A2B", IsBindable = true,
                        Description = "指针与中心轴环的颜色；深色盘面上红色指针一眼扫得到",
                        TargetProperty = ScadaElementBase.FillProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "盘底色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A", IsBindable = true,
                        Description = "表盘面的填充色；浅色画面上要调亮，否则刻度线看不出来",
                        TargetProperty = GaugeElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "ScaleColor", DisplayName = "刻度色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF9AA5B1", IsBindable = true,
                        Description = "量程弧与刻度线的颜色；要和盘底色拉开，否则操作员读不出格",
                        TargetProperty = GaugeElement.ScaleColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "外圈色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "外圈粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "ShowValue", DisplayName = "显示数值", Kind = ElementPropertyKind.Bool,
                        Group = "文字", DefaultValue = "True",
                        Description = "关掉只留指针（读数由旁边的数值域承担），一个没有读数的表盘在工业现场几乎没用，所以默认开",
                        TargetProperty = GaugeElement.ShowValueProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "数值颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FFFFFFFF",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "数值字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "13", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "数值字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        Description = "默认 SemiBold：读数压在盘面上，全 Normal 在小表盘上偏虚",
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    },
                    new()
                    {
                        Key = "TextAlignment", DisplayName = "数值对齐", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "Center",
                        Choices = new[] { "Left", "Center", "Right", "Justify" },
                        TargetProperty = ScadaElementBase.TextAlignmentProperty,
                    }),
            },

            new()
            {
                TypeKey = "Hmi.Valve",
                DisplayName = "阀门",
                ControlType = typeof(ValveElement),
                Category = "工艺",
                DefaultWidth = 72,
                DefaultHeight = 64,
                Description = "管路上一只阀（蝴蝶结符号）；开度绑变量，0=全关、100=全开，中间值就是调节阀",
                Properties = GeometryProperties.With(
                    new()
                    {
                        Key = "Opening", DisplayName = "开度", Kind = ElementPropertyKind.Number,
                        Group = "数据", DefaultValue = "0", Min = 0, Max = 100, IsBindable = true,
                        Description = "运行态把工程变量绑到这里（0~100 的实数）；开关阀只给 0 或 100，" +
                                      "中间值就是调节阀。超出 0~100 按两端收敛，坏值不会把整页渲染打断",
                        TargetProperty = ValveElement.OpeningProperty,
                    },
                    new()
                    {
                        Key = "OpeningFormat", DisplayName = "开度格式", Kind = ElementPropertyKind.Text,
                        Group = "数据", DefaultValue = "0.#",
                        Description = "标准 .NET 数字格式串（0.0 定一位小数、F0 取整）；" +
                                      "写错了退回通用格式，不会让画面出错",
                        TargetProperty = ValveElement.OpeningFormatProperty,
                    },
                    new()
                    {
                        Key = "TrackColor", DisplayName = "空腔色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF3A3A3A", IsBindable = true,
                        Description = "阀体空腔的底色，也是全关时阀体的样子（没开就没有介质）",
                        TargetProperty = ValveElement.TrackColorProperty,
                    },
                    new()
                    {
                        Key = "ThrottleColor", DisplayName = "中间位色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF2F80ED", IsBindable = true,
                        Description = "0 < 开度 < 100 时介质填充的颜色；蓝色是「正在调节」的通用语汇",
                        TargetProperty = ValveElement.ThrottleColorProperty,
                    },
                    new()
                    {
                        Key = "OpenColor", DisplayName = "全开色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF34C759", IsBindable = true,
                        Description = "开度 ≥ 100 时介质填充的颜色；绿色 = 通路打开",
                        TargetProperty = ValveElement.OpenColorProperty,
                    },
                    new()
                    {
                        Key = "Stroke", DisplayName = "轮廓色", Kind = ElementPropertyKind.Color,
                        Group = "外观", DefaultValue = "#FF5A5A5A", IsBindable = true,
                        TargetProperty = ScadaElementBase.StrokeProperty,
                    },
                    new()
                    {
                        Key = "StrokeThickness", DisplayName = "轮廓粗细", Kind = ElementPropertyKind.Number,
                        Group = "外观", DefaultValue = "1", Min = 0, Max = 20,
                        TargetProperty = ScadaElementBase.StrokeThicknessProperty,
                    },
                    new()
                    {
                        Key = "Text", DisplayName = "位号", Kind = ElementPropertyKind.Text,
                        Group = "文字", DefaultValue = string.Empty, IsBindable = true,
                        Description = "压在阀体上方凹口的一行字（如「FV-101」）；管路图上靠它认设备",
                        TargetProperty = ScadaElementBase.TextProperty,
                    },
                    new()
                    {
                        Key = "ShowValue", DisplayName = "显示开度", Kind = ElementPropertyKind.Bool,
                        Group = "文字", DefaultValue = "True",
                        Description = "关掉只留阀体（读数交给旁边的数值域），默认开：阀开没开全靠这个数说话",
                        TargetProperty = ValveElement.ShowValueProperty,
                    },
                    new()
                    {
                        Key = "Foreground", DisplayName = "文字颜色", Kind = ElementPropertyKind.Color,
                        Group = "文字", DefaultValue = "#FFFFFFFF",
                        Description = "默认白色：文字压在深色阀体上，深色字会糊成一片",
                        TargetProperty = ScadaElementBase.ForegroundProperty,
                    },
                    new()
                    {
                        Key = "FontSize", DisplayName = "字号", Kind = ElementPropertyKind.Number,
                        Group = "文字", DefaultValue = "12", Min = 6, Max = 200,
                        TargetProperty = ScadaElementBase.FontSizeProperty,
                    },
                    new()
                    {
                        Key = "FontWeight", DisplayName = "字重", Kind = ElementPropertyKind.Choice,
                        Group = "文字", DefaultValue = "SemiBold",
                        Choices = new[] { "Normal", "SemiBold", "Bold" },
                        TargetProperty = ScadaElementBase.FontWeightProperty,
                    }),
            },
        };
    }
}
